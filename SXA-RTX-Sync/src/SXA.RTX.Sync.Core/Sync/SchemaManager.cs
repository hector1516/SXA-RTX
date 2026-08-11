using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SXA.RTX.Sync.Core.Configuration;

namespace SXA.RTX.Sync.Core.Sync;

public sealed class SchemaManager
{
    private readonly SyncOptions _options;
    private readonly ILogger<SchemaManager> _logger;
    private readonly HashSet<string> _ensuredLocal = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ensuredRemote = new(StringComparer.OrdinalIgnoreCase);

    public SchemaManager(IOptions<SyncOptions> options, ILogger<SchemaManager> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public void ClearCaches()
    {
        lock (_ensuredLocal) { _ensuredLocal.Clear(); }
        lock (_ensuredRemote) { _ensuredRemote.Clear(); }
    }

    public async Task EnsureAllAsync(CancellationToken ct)
    {
        foreach (var table in _options.Tables)
        {
            if (!table.Enabled)
            {
                continue;
            }

            await EnsureLocalSchemaAsync(table, ct);
            if (table.AutoCreateRemote)
            {
                await EnsureRemoteSchemaAsync(table, ct);
            }
        }
    }

    public async Task EnsureLocalSchemaAsync(SyncTableConfig table, CancellationToken ct)
    {
        var localQ = SqlName.Quote(table.LocalTable);
        var logQ = SqlName.Quote(_options.SyncLogTable);
        var key = $"L|{localQ}|{table.KeyColumn}";

        lock (_ensuredLocal)
        {
            if (_ensuredLocal.Contains(key))
            {
                return;
            }
        }

        await using var conn = new SqlConnection(_options.LocalConnectionString);
        await conn.OpenAsync(ct);
        await EnsureSyncLogTableAsync(conn, logQ, ct);
        await EnsureTriggerAsync(conn, localQ, logQ, table.KeyColumn, table.LocalTable, ct);

        lock (_ensuredLocal)
        {
            _ensuredLocal.Add(key);
        }
    }

    public async Task EnsureRemoteSchemaAsync(SyncTableConfig table, CancellationToken ct)
    {
        var remoteQ = SqlName.Quote(table.RemoteTable);
        var key = $"R|{remoteQ}";

        lock (_ensuredRemote)
        {
            if (_ensuredRemote.Contains(key))
            {
                return;
            }
        }

        var localColumns = await DatabaseScanner.GetColumnsAsync(_options.LocalConnectionString, table.LocalTable, ct);

        await using var remote = new SqlConnection(_options.RemoteConnectionString);
        await remote.OpenAsync(ct);

        await EnsureRemoteTableAsync(remote, remoteQ, localColumns, table.KeyColumn, ct);
        await EnsureOriginColumnAsync(remote, remoteQ, _options.OriginColumn, ct);

        lock (_ensuredRemote)
        {
            _ensuredRemote.Add(key);
        }
    }

    internal async Task EnsureRemoteTableAsync(
        SqlConnection remote,
        string remoteQ,
        IReadOnlyList<ColumnInfo> localColumns,
        string keyColumn,
        CancellationToken ct)
    {
        var exists = await ObjectExistsAsync(remote, remoteQ, ct);
        if (exists)
        {
            return;
        }

        var columnDefs = new List<string>();
        foreach (var c in localColumns)
        {
            if (c.IsComputed || c.IsRowVersion)
            {
                continue;
            }

            var nullable = c.IsNullable ? "NULL" : "NOT NULL";
            columnDefs.Add($"    {SqlName.Identifier(c.Name)} {SqlName.TypeDefinition(c)} {nullable}");
        }

        var origin = SqlName.Identifier(_options.OriginColumn);
        var originDefault = SqlName.ConstraintName(remoteQ, "OrigenDF");
        columnDefs.Add($"    {origin} nvarchar(64) NOT NULL CONSTRAINT [{originDefault}] DEFAULT (N'')");

        var keyCol = localColumns.FirstOrDefault(k =>
            string.Equals(k.Name, keyColumn, StringComparison.OrdinalIgnoreCase) && !k.IsComputed && !k.IsRowVersion);
        var pk = keyCol is not null
            ? $",\n    CONSTRAINT [{SqlName.ConstraintName(remoteQ, "PK")}] PRIMARY KEY ({SqlName.Identifier(keyCol.Name)}, {origin})"
            : "";

        var create = $"""
            CREATE TABLE {remoteQ} (
            {string.Join(",\n", columnDefs)}{pk}
            );
            """;

        using var cmd = new SqlCommand(create, remote) { CommandTimeout = 60 };
        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Tabla remota creada: {table} (PK compuesta con {origin})", remoteQ, origin);
    }

    private static async Task EnsureOriginColumnAsync(
        SqlConnection remote,
        string remoteQ,
        string originColumn,
        CancellationToken ct)
    {
        var defaultName = SqlName.ConstraintName(remoteQ, "OrigenDF");
        var sql = $"""
            IF COL_LENGTH(N'{remoteQ.Replace("'", "''")}', N'{originColumn.Replace("'", "''")}') IS NULL
                ALTER TABLE {remoteQ}
                    ADD {SqlName.Identifier(originColumn)} nvarchar(64) NOT NULL
                        CONSTRAINT [{defaultName}] DEFAULT (N'');
            """;
        using var cmd = new SqlCommand(sql, remote) { CommandTimeout = 60 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task EnsureSyncLogTableAsync(SqlConnection conn, string logQ, CancellationToken ct)
    {
        var create = $"""
            IF OBJECT_ID(N'{logQ.Replace("'", "''")}') IS NULL
            BEGIN
                CREATE TABLE {logQ} (
                    Id         bigint IDENTITY(1,1) NOT NULL,
                    TableName  nvarchar(255) NOT NULL,
                    KeyValue   nvarchar(255) NOT NULL,
                    Operation  nchar(1)      NOT NULL DEFAULT N'I',
                    Status     int           NOT NULL DEFAULT 0,
                    Attempts   int           NOT NULL DEFAULT 0,
                    CreatedAt  datetime2     NOT NULL DEFAULT SYSDATETIME(),
                    ClaimedAt  datetime2     NULL,
                    DoneAt     datetime2     NULL,
                    LastError  nvarchar(max) NULL,
                    CONSTRAINT [PK_SXA_SyncLog] PRIMARY KEY (Id)
                );
                CREATE INDEX [IX_SXA_SyncLog_status]
                    ON {logQ} (Status, TableName, CreatedAt) INCLUDE (Id, KeyValue);
            END
            """;
        using var cmd = new SqlCommand(create, conn) { CommandTimeout = 60 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task EnsureTriggerAsync(
        SqlConnection conn,
        string localQ,
        string logQ,
        string keyColumn,
        string localTableFull,
        CancellationToken ct)
    {
        var trigger = SqlName.TriggerName(localQ);
        var key = keyColumn.Trim('[', ']');
        var inner = $"""
            CREATE OR ALTER TRIGGER {trigger}
            ON {localQ} AFTER INSERT, UPDATE
            AS
            BEGIN
                SET NOCOUNT ON;
                INSERT INTO {logQ} (TableName, KeyValue, Operation)
                SELECT N'{localTableFull.Replace("'", "''")}',
                       CONVERT(nvarchar(255), i.[{key}]),
                       CASE WHEN EXISTS (SELECT 1 FROM deleted d WHERE d.[{key}] = i.[{key}])
                            THEN N'U' ELSE N'I' END
                FROM inserted AS i;
            END
            """;

        var sql = $"EXEC(N'{inner.Replace("'", "''")}');";

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> ObjectExistsAsync(SqlConnection conn, string qualified, CancellationToken ct)
    {
        const string query = "SELECT OBJECT_ID(@qualified);";
        using var cmd = new SqlCommand(query, conn) { CommandTimeout = 15 };
        cmd.Parameters.Add("@qualified", SqlDbType.NVarChar, 300).Value = qualified;
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null && result is not DBNull;
    }
}