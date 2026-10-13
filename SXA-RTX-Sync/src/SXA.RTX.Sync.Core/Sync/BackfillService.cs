using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SXA.RTX.Sync.Core.Configuration;

namespace SXA.RTX.Sync.Core.Sync;

public sealed record BackfillTableResult(string Table, int Enqueued, string? DateColumn, string? Note);

public sealed class BackfillService
{
    private readonly SyncOptions _options;
    private readonly ILogger<BackfillService> _logger;

    public BackfillService(IOptions<SyncOptions> options, ILogger<BackfillService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<BackfillTableResult>> RunAsync(int lookbackDays, bool force, CancellationToken ct)
    {
        var results = new List<BackfillTableResult>();
        var from = DateTime.Now.Date.AddDays(-Math.Max(lookbackDays, 0));
        var to = DateTime.Now.AddMinutes(5);

        await using var conn = new SqlConnection(ConnectionStringHelper.Harden(_options.LocalConnectionString));
        await conn.OpenAsync(ct);
        await EnsureMetaTableAsync(conn, ct);

        foreach (var table in _options.Tables)
        {
            ct.ThrowIfCancellationRequested();
            if (!table.Enabled)
            {
                continue;
            }

            try
            {
                var markerKey = $"Backfill:{table.LocalTable}";
                if (!force && await IsMarkerSetAsync(conn, markerKey, ct))
                {
                    results.Add(new BackfillTableResult(table.LocalTable, 0, null, "Ya se hizo antes"));
                    continue;
                }

                var localQ = SqlName.Quote(table.LocalTable);
                var key = table.KeyColumn.Trim('[', ']');
                var columns = await DatabaseIntrospection.GetColumnsAsync(conn, localQ, ct);
                var dateCol = PickDateColumn(columns);
                if (dateCol is null)
                {
                    results.Add(new BackfillTableResult(table.LocalTable, 0, null, "Sin columna de fecha; se omite"));
                    continue;
                }

                var logQ = SqlName.Quote(_options.SyncLogTable);
                var sql = $"""
                    INSERT INTO {logQ} (TableName, KeyValue, Operation)
                    SELECT N'{table.LocalTable.Replace("'", "''")}',
                           CONVERT(nvarchar(255), [{key}]),
                           N'I'
                    FROM {localQ}
                    WHERE [{dateCol}] >= @from AND [{dateCol}] < @to;
                    """;
                using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
                cmd.Parameters.Add("@from", SqlDbType.DateTime2).Value = from;
                cmd.Parameters.Add("@to", SqlDbType.DateTime2).Value = to;
                var enqueued = await cmd.ExecuteNonQueryAsync(ct);

                await SetMarkerAsync(conn, markerKey, DateTime.UtcNow.ToString("o"), ct);
                _logger.LogInformation("Historial {table}: {n} filas encoladas (desde {from:yyyy-MM-dd}, columna {col})",
                    table.LocalTable, enqueued, from, dateCol);
                results.Add(new BackfillTableResult(table.LocalTable, enqueued, dateCol, null));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo encolar el historial de {table}", table.LocalTable);
                results.Add(new BackfillTableResult(table.LocalTable, 0, null, ex.Message));
            }
        }

        return results;
    }

    private static string? PickDateColumn(IReadOnlyList<ColumnInfo> columns)
    {
        var names = new[] { "fecha", "fechahora", "fecha_hora", "fechaalta", "created", "creado", "alta", "timestamp", "date" };
        foreach (var candidate in names)
        {
            var col = columns.FirstOrDefault(c =>
                !c.IsComputed && !c.IsRowVersion &&
                (c.TypeName.Contains("date", StringComparison.OrdinalIgnoreCase) ||
                 c.TypeName.Contains("time", StringComparison.OrdinalIgnoreCase)) &&
                c.Name.Replace("_", "").Contains(candidate, StringComparison.OrdinalIgnoreCase));
            if (col is not null)
            {
                return col.Name;
            }
        }

        // Cualquier columna de tipo fecha/hora
        return columns.FirstOrDefault(c =>
            !c.IsComputed && !c.IsRowVersion &&
            (c.TypeName.Contains("date", StringComparison.OrdinalIgnoreCase) ||
             c.TypeName.Contains("time", StringComparison.OrdinalIgnoreCase)))?.Name;
    }

    private static async Task EnsureMetaTableAsync(SqlConnection conn, CancellationToken ct)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.SXA_SyncMeta', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.SXA_SyncMeta (
                    [Key]   nvarchar(200) NOT NULL PRIMARY KEY,
                    [Value] nvarchar(400) NULL
                );
            END
            """;
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> IsMarkerSetAsync(SqlConnection conn, string key, CancellationToken ct)
    {
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.SXA_SyncMeta WHERE [Key] = @k;", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add("@k", SqlDbType.NVarChar, 200).Value = key;
        return (int)await cmd.ExecuteScalarAsync(ct) > 0;
    }

    private static async Task SetMarkerAsync(SqlConnection conn, string key, string value, CancellationToken ct)
    {
        const string sql = """
            MERGE dbo.SXA_SyncMeta AS t
            USING (SELECT @k AS [Key]) AS s ON t.[Key] = s.[Key]
            WHEN MATCHED THEN UPDATE SET [Value] = @v
            WHEN NOT MATCHED THEN INSERT ([Key], [Value]) VALUES (@k, @v);
            """;
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 15 };
        cmd.Parameters.Add("@k", SqlDbType.NVarChar, 200).Value = key;
        cmd.Parameters.Add("@v", SqlDbType.NVarChar, 400).Value = value;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
