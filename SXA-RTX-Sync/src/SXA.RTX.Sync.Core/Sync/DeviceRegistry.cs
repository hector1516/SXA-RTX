using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SXA.RTX.Sync.Core.Configuration;

namespace SXA.RTX.Sync.Core.Sync;

public sealed class DeviceRegistry
{
    private readonly SyncOptions _options;
    private readonly ILogger<DeviceRegistry> _logger;
    private bool _schemaEnsured;

    public DeviceRegistry(IOptions<SyncOptions> options, ILogger<DeviceRegistry> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public void ClearCache()
    {
        _schemaEnsured = false;
    }

    public async Task RegisterAsync(
        string deviceId,
        string? machineName,
        string? machineType,
        string? model,
        CancellationToken ct)
    {
        var tableQ = SqlName.Quote(_options.DeviceCatalogTable);
        await using var connection = new SqlConnection(_options.RemoteConnectionString);
        await connection.OpenAsync(ct);

        if (!_schemaEnsured)
        {
            await EnsureSchemaAsync(connection, tableQ, ct);
            _schemaEnsured = true;
        }

        const string mergeSql = """
            MERGE {0} AS t
            USING (SELECT @deviceId AS DeviceId) AS s
              ON t.DeviceId = s.DeviceId
            WHEN MATCHED THEN
                UPDATE SET t.NombrePC = @name, t.TipoMaquina = @type, t.Modelo = @model, t.UltimoContacto = GETUTCDATE()
            WHEN NOT MATCHED THEN
                INSERT (DeviceId, NombrePC, TipoMaquina, Modelo, PrimerContacto, UltimoContacto)
                VALUES (@deviceId, @name, @type, @model, GETUTCDATE(), GETUTCDATE());
            """;

        using var cmd = new SqlCommand(string.Format(mergeSql, tableQ), connection) { CommandTimeout = 30 };
        cmd.Parameters.Add("@deviceId", SqlDbType.NVarChar, 64).Value = deviceId;
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 255).Value = (object?)machineName ?? DBNull.Value;
        cmd.Parameters.Add("@type", SqlDbType.NVarChar, 32).Value = (object?)machineType ?? DBNull.Value;
        cmd.Parameters.Add("@model", SqlDbType.NVarChar, 255).Value = (object?)model ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task EnsureSchemaAsync(SqlConnection connection, string tableQ, CancellationToken ct)
    {
        const string checkSql = "SELECT OBJECT_ID(@table);";
        using (var check = new SqlCommand(checkSql, connection) { CommandTimeout = 15 })
        {
            check.Parameters.Add("@table", SqlDbType.NVarChar, 300).Value = tableQ;
            var result = await check.ExecuteScalarAsync(ct);
            if (result is not DBNull && result is not null)
            {
                return;
            }
        }

        var createSql = $"""
            IF OBJECT_ID(N'{tableQ.Replace("'", "''")}') IS NULL
            BEGIN
                CREATE TABLE {tableQ} (
                    DeviceId        nvarchar(64)  NOT NULL PRIMARY KEY,
                    NombrePC        nvarchar(255) NULL,
                    TipoMaquina     nvarchar(32)  NULL,
                    Modelo          nvarchar(255) NULL,
                    PrimerContacto  datetime      NOT NULL,
                    UltimoContacto  datetime      NOT NULL
                );
            END
            """;

        using var create = new SqlCommand(createSql, connection) { CommandTimeout = 30 };
        await create.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Catálogo de PCs creado en remoto: {table}", tableQ);
    }
}