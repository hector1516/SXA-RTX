using Microsoft.Extensions.Options;
using SXA.RTX.Sync.Core.Configuration;
using SXA.RTX.Sync.Core.Device;
using SXA.RTX.Sync.Core.Sync;

namespace SXA.RTX.Sync.Worker;

public sealed class SyncWorker(
    ILogger<SyncWorker> logger,
    IOptions<SyncOptions> options,
    DeviceIdentity deviceIdentity,
    SchemaManager schemaManager,
    SyncEngine syncEngine,
    DeviceRegistry deviceRegistry) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await deviceIdentity.LoadOrCreateAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "No se pudo determinar la identidad del dispositivo. El servicio se detendrá.");
            throw;
        }

        logger.LogInformation(
            "Dispositivo: {deviceId} (máquina {machine}, modelo {model}, tipo {type}, GUID {guid}, SMBIOS {uuid})",
            deviceIdentity.DeviceId,
            deviceIdentity.MachineName,
            deviceIdentity.Model,
            options.Value.MachineType,
            deviceIdentity.MachineGuid,
            deviceIdentity.SmbiosUuid);

        var opts = options.Value;
        var failures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await schemaManager.EnsureAllAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo asegurar el esquema (SyncLog/triggers/tabla remota).");
            }

            try
            {
                await deviceRegistry.RegisterAsync(
                    deviceIdentity.DeviceId,
                    string.IsNullOrWhiteSpace(opts.MachineName) ? deviceIdentity.MachineName : opts.MachineName,
                    opts.MachineType,
                    deviceIdentity.Model,
                    stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "El registro del PC en remoto falló (puede ser normal si el servidor no responde).");
            }

            try
            {
                var results = await syncEngine.SyncAllAsync(deviceIdentity.DeviceId, stoppingToken);
                var total = results.Sum(r => r.Inserted);
                if (total > 0)
                {
                    logger.LogInformation("Ciclo completado: {total} filas insertadas en remoto", total);
                }
                failures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                failures = Math.Min(failures + 1, 6);
                logger.LogError(ex, "Error en el ciclo de sincronización (intento {failures})", failures);
            }

            var delaySeconds = Math.Min(opts.PollIntervalSeconds * Math.Pow(2, failures), 300);
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }
    }
}