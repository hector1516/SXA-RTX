using SXA.RTX.Sync.Core.Configuration;
using SXA.RTX.Sync.Core.Device;
using SXA.RTX.Sync.Core.Sync;

namespace SXA.RTX.Sync.Tray;

public static class TrayComposition
{
    public static (ConfigStore Store, SyncOptions Options) LoadConfig()
    {
        var store = new ConfigStore("appsettings.json");
        var options = store.LoadOrDefaults();
        return (store, options);
    }

    public static bool IsConfigValid(SyncOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.LocalConnectionString)
            && !string.IsNullOrWhiteSpace(options.RemoteConnectionString)
            && options.Tables.Count > 0;
    }
}