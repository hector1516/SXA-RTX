using System.Data;
using Microsoft.Data.SqlClient;

namespace SXA.RTX.Sync.Core.Sync;

public sealed record ScannedColumn(
    string Name,
    string TypeDef,
    bool IsNullable,
    bool IsIdentity,
    bool IsComputed,
    bool IsRowVersion);

public sealed record ScannedTable(
    string Schema,
    string Name,
    string FullName,
    IReadOnlyList<ScannedColumn> Columns);

public sealed record ColumnComparison(
    IReadOnlyList<string> MissingOnRemote,
    IReadOnlyList<string> Incompatible,
    bool Compatible)
{
    public static ColumnComparison Evaluate(
        IReadOnlyList<ScannedColumn> local,
        IReadOnlyList<ScannedColumn> remote,
        IReadOnlySet<string> ignoreOnRemote)
    {
        var missing = new List<string>();
        var incompatible = new List<string>();

        foreach (var col in local)
        {
            if (col.IsRowVersion)
            {
                // no se inserta; se ignora
                continue;
            }

            var remoteCol = remote.FirstOrDefault(r =>
                string.Equals(r.Name, col.Name, StringComparison.OrdinalIgnoreCase));
            if (remoteCol is null)
            {
                missing.Add(col.Name);
                continue;
            }

            if (ignoreOnRemote.Contains(remoteCol.Name))
            {
                continue;
            }

            if (!string.Equals(col.TypeDef, remoteCol.TypeDef, StringComparison.OrdinalIgnoreCase))
            {
                incompatible.Add($"{col.Name} ({col.TypeDef} != {remoteCol.TypeDef})");
            }
        }

        return new ColumnComparison(missing, incompatible, missing.Count == 0 && incompatible.Count == 0);
    }
}

public static class DatabaseScanner
{
    private static readonly HashSet<string> IgnoredTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "SXA_SyncLog", "SXA_PCs", "sysdiagrams"
    };

    public static async Task<List<ScannedTable>> ScanTablesAsync(string connectionString, CancellationToken ct)
    {
        const string query = """
            SELECT s.name AS [schema], t.name AS [table]
            FROM sys.tables AS t
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name;
            """;

        var tables = new List<(string Schema, string Name)>();
        await using (var conn = new SqlConnection(connectionString))
        {
            await conn.OpenAsync(ct);
            using var cmd = new SqlCommand(query, conn) { CommandTimeout = 30 };
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                tables.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        var result = new List<ScannedTable>();
        foreach (var (schema, name) in tables)
        {
            if (IgnoredTables.Contains(name))
            {
                continue;
            }

            var fullName = $"{schema}.{name}";
            var columns = await GetColumnsAsync(connectionString, fullName, ct);
            result.Add(new ScannedTable(
                schema,
                name,
                fullName,
                columns.Select(c => new ScannedColumn(
                    c.Name,
                    SqlName.TypeDefinition(c),
                    c.IsNullable,
                    c.IsIdentity,
                    c.IsComputed,
                    c.IsRowVersion)).ToList()));
        }

        return result;
    }

    internal static async Task<List<ColumnInfo>> GetColumnsAsync(
        string connectionString,
        string qualifiedName,
        CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        return await DatabaseIntrospection.GetColumnsAsync(conn, SqlName.Quote(qualifiedName), ct);
    }

    public static async Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct)
    {
        return await Task.Run(async () =>
        {
            try
            {
                await using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync(ct);
                return true;
            }
            catch
            {
                return false;
            }
        }, ct);
    }

    public static async Task<string> DetectKeyColumnAsync(
        string connectionString,
        string schema,
        string table,
        CancellationToken ct)
    {
        const string query = """
            SELECT TOP 1 kcu.COLUMN_NAME
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS AS tc
            INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE AS kcu
                ON kcu.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
               AND kcu.TABLE_NAME = tc.TABLE_NAME
               AND kcu.TABLE_SCHEMA = tc.TABLE_SCHEMA
            WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
              AND tc.TABLE_SCHEMA = @schema
              AND tc.TABLE_NAME = @table
              AND (SELECT COUNT(*) FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE AS k2
                   WHERE k2.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
                     AND k2.TABLE_NAME = tc.TABLE_NAME) = 1;
            """;

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(query, conn) { CommandTimeout = 20 };
        cmd.Parameters.Add("@schema", SqlDbType.NVarChar, 128).Value = schema;
        cmd.Parameters.Add("@table", SqlDbType.NVarChar, 128).Value = table;
        var key = await cmd.ExecuteScalarAsync(ct) as string;
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        var columns = await DatabaseIntrospection.GetColumnsAsync(conn, SqlName.Quote($"{schema}.{table}"), ct);
        var idCol = columns.FirstOrDefault(c =>
            string.Equals(c.Name, "Id", StringComparison.OrdinalIgnoreCase) && !c.IsComputed);
        return idCol?.Name ?? columns.FirstOrDefault()?.Name ?? "Id";
    }
}