using Microsoft.Extensions.Logging;
using SXA.RTX.Sync.Core.Configuration;
using SXA.RTX.Sync.Core.Device;
using SXA.RTX.Sync.Core.Sync;

namespace SXA.RTX.Sync.Tray;

public sealed class SyncManager : IAsyncDisposable
{
    private readonly ConfigStore _store;
    private readonly SyncEngine _engine;
    private readonly SchemaManager _schema;
    private readonly DeviceRegistry _registry;
    private readonly ILogger<SyncManager> _logger;
    private SyncOptions _options;
    private CancellationTokenSource _cts = new();
    private volatile bool _paused;
    private volatile bool _exiting;
    private Task? _loop;

    public DeviceIdentity Identity { get; }
    public SyncOptions CurrentOptions => _options;
    public bool IsPaused => _paused;
    public event Action<string>? LogUpdated;
    public event Action? StateChanged;

    public SyncManager(
        ConfigStore store,
        SyncEngine engine,
        SchemaManager schema,
        DeviceRegistry registry,
        DeviceIdentity identity,
        ILogger<SyncManager> logger,
        SyncOptions options)
    {
        _store = store;
        _engine = engine;
        _schema = schema;
        _registry = registry;
        Identity = identity;
        _logger = logger;
        _options = options;
    }

    public async Task<bool> InitializeAsync()
    {
        try
        {
            await Identity.LoadOrCreateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "No se pudo obtener la identidad del dispositivo.");
            return false;
        }

        try
        {
            await _schema.EnsureAllAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo asegurar el esquema. Revise las cadenas de conexión.");
        }

        return true;
    }

    public void Start()
    {
        if (_loop is { IsCompleted: false })
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loop = RunLoopAsync(_cts.Token);
    }

    public void Pause()
    {
        _paused = true;
        LogUpdated?.Invoke("Sincronización en pausa.");
        StateChanged?.Invoke();
    }

    public void Resume()
    {
        _paused = false;
        StateChanged?.Invoke();
    }

    public async Task ReconfigureAsync(SyncOptions newOptions, CancellationToken ct)
    {
        var current = _options;
        current.LocalConnectionString = newOptions.LocalConnectionString;
        current.RemoteConnectionString = newOptions.RemoteConnectionString;
        current.OriginColumn = newOptions.OriginColumn;
        current.PollIntervalSeconds = newOptions.PollIntervalSeconds;
        current.BatchSize = newOptions.BatchSize;
        current.ReclaimAfterMinutes = newOptions.ReclaimAfterMinutes;
        current.MaxRetries = newOptions.MaxRetries;
        current.SyncLogTable = newOptions.SyncLogTable;
        current.HeartbeatTable = newOptions.HeartbeatTable;
        current.DeviceCatalogTable = newOptions.DeviceCatalogTable;
        current.DeviceConfigFile = newOptions.DeviceConfigFile;
        current.MachineType = newOptions.MachineType;
        current.MachineName = newOptions.MachineName;
        current.Tables = newOptions.Tables;

        _store.Save(current);
        _schema.ClearCaches();
        _engine.ClearCaches();
        _registry.ClearCache();
        await _schema.EnsureAllAsync(ct);
        LogUpdated?.Invoke("Configuración aplicada.");
        StateChanged?.Invoke();
    }

    public async Task StopAsync()
    {
        _exiting = true;
        _cts.Cancel();
        if (_loop is not null)
        {
            await Task.WhenAny(_loop, Task.Delay(3000));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            if (_paused)
            {
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
                continue;
            }

            try
            {
                await _registry.RegisterAsync(
                    Identity.DeviceId,
                    string.IsNullOrWhiteSpace(_options.MachineName) ? Identity.MachineName : _options.MachineName,
                    _options.MachineType,
                    Identity.Model,
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Registro del PC en remoto falló.");
            }

            try
            {
                var results = await _engine.SyncAllAsync(Identity.DeviceId, ct);
                var total = results.Sum(r => r.Inserted);
                if (total > 0)
                {
                    LogUpdated?.Invoke($"Sincronizadas {total} filas ({DateTime.Now:HH:mm:ss}).");
                }
                failures = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                failures = Math.Min(failures + 1, 6);
                _logger.LogError(ex, "Error en el ciclo de sincronización.");
                LogUpdated?.Invoke($"Error: {ex.Message}");
            }

            if (_exiting)
            {
                break;
            }

            var delay = Math.Min(_options.PollIntervalSeconds * Math.Pow(2, failures), 300);
            try { await Task.Delay(TimeSpan.FromSeconds(delay), ct); } catch (OperationCanceledException) { break; }
        }
    }
}