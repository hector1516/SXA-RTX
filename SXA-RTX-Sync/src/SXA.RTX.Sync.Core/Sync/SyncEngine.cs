using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SXA.RTX.Sync.Core.Configuration;

namespace SXA.RTX.Sync.Core.Sync;

public sealed record SyncTableResult(string Table, int Claimed, int Inserted, int Failed);

public sealed class SyncEngine
{
    private readonly SyncOptions _options;
    private readonly ILogger<SyncEngine> _logger;
    private readonly Dictionary<string, TableInfo> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _tableLock = new();

    private sealed record TableInfo(
        string LocalQ,
        string RemoteQ,
        string KeyColumn,
        ColumnInfo KeyColumnInfo,
        List<ColumnInfo> RemoteColumns);

    private sealed record LogEntry(long Id, string TableName, string KeyValue, string Operation);

    public SyncEngine(IOptions<SyncOptions> options, ILogger<SyncEngine> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public void ClearCaches()
    {
        lock (_tableLock) { _tables.Clear(); }
    }

    public async Task<List<SyncTableResult>> SyncAllAsync(string deviceId, CancellationToken ct)
    {
        var results = new List<SyncTableResult>();
        foreach (var table in _options.Tables)
        {
            ct.ThrowIfCancellationRequested();
            if (!table.Enabled)
            {
                continue;
            }

            try
            {
                results.Add(await SyncTableAsync(table, deviceId, ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo al sincronizar la tabla {table}", table.LocalTable);
                results.Add(new SyncTableResult(table.LocalTable, 0, 0, 0));
            }
        }
        return results;
    }

    private async Task<SyncTableResult> SyncTableAsync(SyncTableConfig config, string deviceId, CancellationToken ct)
    {
        var info = await GetTableInfoAsync(config, ct);
        var logQ = SqlName.Quote(_options.SyncLogTable);
        var claimed = 0;
        var inserted = 0;
        var failed = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            List<LogEntry> entries;
            await using (var local = new SqlConnection(_options.LocalConnectionString))
            {
                await local.OpenAsync(ct);
                entries = await ClaimLogBatchAsync(local, logQ, config.LocalTable, ct);
            }

            if (entries.Count == 0)
            {
                break;
            }

            claimed += entries.Count;
            foreach (var entry in entries)
            {
                try
                {
                    await ProcessEntryAsync(info, entry, deviceId, ct);
                    inserted++;
                }
                catch (SqlException sqlEx) when (IsDuplicateKey(sqlEx))
                {
                    inserted++;
                    await MarkAsync(logQ, entry.Id, 2, null, ct);
                    _logger.LogDebug("[{table}] fila {key} ya existía en remoto; se marcó como sincronizada",
                        info.LocalQ, entry.KeyValue);
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "[{table}] falló la fila clave {key}", info.LocalQ, entry.KeyValue);
                    await MarkAsync(logQ, entry.Id, -1, Truncate(ex.Message), ct);
                }
            }

            if (entries.Count < _options.BatchSize)
            {
                break;
            }
        }

        if (claimed > 0)
        {
            _logger.LogInformation("[{table}] reclamadas={claimed} insertadas={inserted} fallidas={failed}",
                info.LocalQ, claimed, inserted, failed);
        }

        return new SyncTableResult(info.LocalQ, claimed, inserted, failed);
    }

    private async Task ProcessEntryAsync(
        TableInfo info,
        LogEntry entry,
        string deviceId,
        CancellationToken ct)
    {
        var row = await ReadBusinessRowAsync(info, entry.KeyValue, ct);
        if (row.Count == 0)
        {
            _logger.LogWarning("[{table}] la fila {key} ya no existe; se marca como hecha",
                info.LocalQ, entry.KeyValue);
            return;
        }

        await InsertOrUpdateRemoteAsync(info, row, entry.Operation, deviceId, ct);
        await MarkAsync(SqlName.Quote(_options.SyncLogTable), entry.Id, 2, null, ct);
    }

    private async Task InsertOrUpdateRemoteAsync(
        TableInfo info,
        Dictionary<string, object?> row,
        string operation,
        string deviceId,
        CancellationToken ct)
    {
        if (operation.Equals("U", StringComparison.OrdinalIgnoreCase))
        {
            var exists = await RowExistsAsync(info, Convert.ToString(row[info.KeyColumn]), deviceId, ct);
            if (exists)
            {
                await UpdateRemoteAsync(info, row, deviceId, ct);
                return;
            }
        }

        await InsertRemoteAsync(info, row, deviceId, ct);
    }

    private static object ConvertKeyValue(string value, string typeName)
    {
        return typeName.ToLowerInvariant() switch
        {
            "int" or "smallint" or "tinyint" => int.Parse(value, CultureInfo.InvariantCulture),
            "bigint" => long.Parse(value, CultureInfo.InvariantCulture),
            "decimal" or "numeric" or "money" or "smallmoney" => decimal.Parse(value, CultureInfo.InvariantCulture),
            "float" or "real" => double.Parse(value, CultureInfo.InvariantCulture),
            "bit" => value is "1" or "true" or "True",
            "datetime" or "datetime2" or "smalldatetime" or "date" or "time" or "datetimeoffset"
                => DateTime.Parse(value, CultureInfo.InvariantCulture),
            "uniqueidentifier" => Guid.Parse(value),
            _ => value
        };
    }

    private async Task<Dictionary<string, object?>> ReadBusinessRowAsync(
        TableInfo info,
        string keyValue,
        CancellationToken ct)
    {
        var conn = new SqlConnection(_options.LocalConnectionString);
        await using var _ = conn;
        await conn.OpenAsync(ct);

        var sql = $"SELECT * FROM {info.LocalQ} WHERE {SqlName.Identifier(info.KeyColumn)} = @key;";
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        var keyParam = new SqlParameter("@key", SqlTypeMapper.Map(info.KeyColumnInfo.TypeName))
        {
            Value = ConvertKeyValue(keyValue, info.KeyColumnInfo.TypeName)
        };
        cmd.Parameters.Add(keyParam);

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                result[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            break; // solo una por PK
        }

        return result;
    }

    private async Task InsertRemoteAsync(
        TableInfo info,
        Dictionary<string, object?> row,
        string deviceId,
        CancellationToken ct)
    {
        var remoteColumns = info.RemoteColumns
            .Where(c => row.ContainsKey(c.Name) && !c.IsComputed && !c.IsRowVersion)
            .ToList();

        if (remoteColumns.Count == 0)
        {
            throw new InvalidOperationException($"No hay columnas coincidentes para insertar en {info.RemoteQ}.");
        }

        var originCol = info.RemoteColumns.FirstOrDefault(c =>
            string.Equals(c.Name, _options.OriginColumn, StringComparison.OrdinalIgnoreCase));
        if (originCol is null)
        {
            throw new InvalidOperationException(
                $"La columna remota '{_options.OriginColumn}' no existe en {info.RemoteQ}.");
        }

        var useIdentityInsert = remoteColumns.Any(c => c.IsIdentity);
        var cols = new List<string>();
        var ps = new List<string>();
        var parameters = new List<SqlParameter>();
        var index = 0;

        foreach (var col in remoteColumns)
        {
            cols.Add($"[{col.Name}]");
            ps.Add($"@p{index}");
            parameters.Add(CreateParameter($"@p{index}", row[col.Name], col.TypeName));
            index++;
        }

        cols.Add($"[{originCol.Name}]");
        ps.Add("@origin");
        parameters.Add(new SqlParameter("@origin", SqlDbType.NVarChar) { Size = 64, Value = deviceId });

        var insertSql = $"INSERT INTO {info.RemoteQ} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", ps)});";

        var conn = new SqlConnection(_options.RemoteConnectionString);
        await using var _ = conn;
        await conn.OpenAsync(ct);

        var identityInsertOn = false;
        try
        {
            if (useIdentityInsert)
            {
                await ExecAsync(conn, $"SET IDENTITY_INSERT {info.RemoteQ} ON;", ct);
                identityInsertOn = true;
            }

            using var cmd = new SqlCommand(insertSql, conn) { CommandTimeout = 60 };
            cmd.Parameters.AddRange(parameters.ToArray());
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (identityInsertOn)
            {
                await ExecAsync(conn, $"SET IDENTITY_INSERT {info.RemoteQ} OFF;", ct);
            }
        }
    }

    private async Task UpdateRemoteAsync(
        TableInfo info,
        Dictionary<string, object?> row,
        string deviceId,
        CancellationToken ct)
    {
        var remoteColumns = info.RemoteColumns
            .Where(c => row.ContainsKey(c.Name) && !c.IsComputed && !c.IsRowVersion)
            .ToList();

        if (remoteColumns.Count == 0)
        {
            throw new InvalidOperationException($"No hay columnas coincidentes para actualizar en {info.RemoteQ}.");
        }

        var originCol = info.RemoteColumns.FirstOrDefault(c =>
            string.Equals(c.Name, _options.OriginColumn, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"La columna remota '{_options.OriginColumn}' no existe en {info.RemoteQ}.");

        var sets = new List<string>();
        var parameters = new List<SqlParameter>();
        var index = 0;

        foreach (var col in remoteColumns)
        {
            sets.Add($"[{col.Name}] = @u{index}");
            parameters.Add(CreateParameter($"@u{index}", row[col.Name], col.TypeName));
            index++;
        }

        var whereKey = $"[{info.KeyColumn}] = @key";
        parameters.Add(new SqlParameter("@key", row[info.KeyColumn] ?? DBNull.Value));
        parameters.Add(new SqlParameter("@origin", SqlDbType.NVarChar) { Size = 64, Value = deviceId });

        var sql = $"""
            UPDATE {info.RemoteQ}
            SET {string.Join(", ", sets)}
            WHERE {whereKey} AND [{originCol.Name}] = @origin;
            """;

        var conn = new SqlConnection(_options.RemoteConnectionString);
        await using var _ = conn;
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };
        cmd.Parameters.AddRange(parameters.ToArray());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<bool> RowExistsAsync(
        TableInfo info,
        object? keyValue,
        string deviceId,
        CancellationToken ct)
    {
        var originCol = info.RemoteColumns.FirstOrDefault(c =>
            string.Equals(c.Name, _options.OriginColumn, StringComparison.OrdinalIgnoreCase));
        if (originCol is null)
        {
            return false;
        }

        var sql = $"SELECT COUNT(*) FROM {info.RemoteQ} WHERE [{info.KeyColumn}] = @key AND [{originCol.Name}] = @origin;";

        var conn = new SqlConnection(_options.RemoteConnectionString);
        await using var _ = conn;
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.Add(new SqlParameter("@key", keyValue ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@origin", SqlDbType.NVarChar) { Size = 64, Value = deviceId });
        var count = (int)await cmd.ExecuteScalarAsync(ct);
        return count > 0;
    }

    private static SqlParameter CreateParameter(string name, object? value, string remoteType)
    {
        if (value is null)
        {
            var p = new SqlParameter(name, SqlTypeMapper.Map(remoteType));
            p.Value = DBNull.Value;
            return p;
        }

        return value switch
        {
            string s => new SqlParameter(name, SqlDbType.NVarChar) { Size = Math.Max(s.Length, 1), Value = s },
            byte[] b => new SqlParameter(name, SqlDbType.VarBinary) { Size = -1, Value = b },
            _ => new SqlParameter(name, value)
        };
    }

    private async Task<List<LogEntry>> ClaimLogBatchAsync(
        SqlConnection connection,
        string logQ,
        string localQ,
        CancellationToken ct)
    {
        var sql = $"""
            UPDATE {logQ}
            SET Status = 1, ClaimedAt = GETUTCDATE(), Attempts = Attempts + 1
            OUTPUT INSERTED.Id, INSERTED.TableName, INSERTED.KeyValue, INSERTED.Operation
            WHERE Id IN (
                SELECT TOP(@batch) Id
                FROM {logQ}
                WHERE TableName = @tableName
                  AND (Status = 0
                       OR (Status = 1 AND ClaimedAt < DATEADD(MINUTE, -@reclaim, GETUTCDATE()))
                       OR (Status = -1 AND Attempts < @maxRetries))
                ORDER BY CreatedAt, Id
            );
            """;

        var result = new List<LogEntry>();
        using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        cmd.Parameters.Add("@batch", SqlDbType.Int).Value = _options.BatchSize;
        cmd.Parameters.Add("@tableName", SqlDbType.NVarChar, 255).Value = localQ;
        cmd.Parameters.Add("@reclaim", SqlDbType.Int).Value = _options.ReclaimAfterMinutes;
        cmd.Parameters.Add("@maxRetries", SqlDbType.Int).Value = Math.Max(_options.MaxRetries, 1);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new LogEntry(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return result;
    }

    private async Task MarkAsync(
        string logQ,
        long id,
        int status,
        string? error,
        CancellationToken ct)
    {
        var sql = $"""
            UPDATE {logQ}
            SET Status = @status,
                DoneAt = GETUTCDATE(),
                LastError = CASE WHEN @status = -1 THEN @error ELSE NULL END
            WHERE Id = @id AND Status = 1;
            """;

        var conn = new SqlConnection(_options.LocalConnectionString);
        await using var _ = conn;
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.Add("@status", SqlDbType.Int).Value = status;
        cmd.Parameters.Add("@error", SqlDbType.NVarChar, -1).Value = (object?)error ?? DBNull.Value;
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<TableInfo> GetTableInfoAsync(SyncTableConfig config, CancellationToken ct)
    {
        lock (_tableLock)
        {
            if (_tables.TryGetValue(config.LocalTable, out var cached))
            {
                return cached;
            }
        }

        var localQ = SqlName.Quote(config.LocalTable);
        var remoteQ = SqlName.Quote(config.RemoteTable);

        List<ColumnInfo> localColumns;
        await using (var local = new SqlConnection(_options.LocalConnectionString))
        {
            await local.OpenAsync(ct);
            localColumns = await DatabaseIntrospection.GetColumnsAsync(local, localQ, ct);
        }

        if (localColumns.Count == 0)
        {
            throw new InvalidOperationException($"La tabla local {localQ} no existe o no es accesible.");
        }

        var keyColInfo = localColumns.FirstOrDefault(c =>
            string.Equals(c.Name, config.KeyColumn, StringComparison.OrdinalIgnoreCase));
        if (keyColInfo is null)
        {
            throw new InvalidOperationException($"La columna clave '{config.KeyColumn}' no existe en {localQ}.");
        }

        List<ColumnInfo> remoteColumns;
        await using (var remote = new SqlConnection(_options.RemoteConnectionString))
        {
            await remote.OpenAsync(ct);
            remoteColumns = await DatabaseIntrospection.GetColumnsAsync(remote, remoteQ, ct);
            if (remoteColumns.Count == 0)
            {
                throw new InvalidOperationException($"La tabla remota {remoteQ} no existe. Cree el esquema primero.");
            }
        }

        var info = new TableInfo(localQ, remoteQ, config.KeyColumn, keyColInfo, remoteColumns);

        lock (_tableLock)
        {
            _tables[config.LocalTable] = info;
        }

        return info;
    }

    private static bool IsDuplicateKey(SqlException ex)
    {
        return ex.Number is 2601 or 2627;
    }

    private static string Truncate(string message, int max = 8000)
    {
        return message.Length <= max ? message : message[..max];
    }

    private static async Task ExecAsync(SqlConnection connection, string sql, CancellationToken ct)
    {
        using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 30 };
        await cmd.ExecuteNonQueryAsync(ct);
    }
}