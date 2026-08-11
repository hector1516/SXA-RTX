namespace SXA.RTX.Sync.Core.Configuration;

public sealed class SyncOptions
{
    public string LocalConnectionString { get; set; } = "";
    public string RemoteConnectionString { get; set; } = "";
    public string OriginColumn { get; set; } = "OrigenPC";
    public int PollIntervalSeconds { get; set; } = 10;
    public int BatchSize { get; set; } = 500;
    public int ReclaimAfterMinutes { get; set; } = 15;
    public int MaxRetries { get; set; } = 5;
    public string SyncLogTable { get; set; } = "dbo.SXA_SyncLog";
    public string HeartbeatTable { get; set; } = "dbo.SXA_Heartbeat";
    public string DeviceCatalogTable { get; set; } = "dbo.SXA_PCs";
    public string DeviceConfigFile { get; set; } = "device.config";
    public string MachineType { get; set; } = "";
    public string MachineName { get; set; } = "";
    public List<SyncTableConfig> Tables { get; set; } = new();
}

public sealed class SyncTableConfig
{
    public string LocalTable { get; set; } = "";
    public string RemoteTable { get; set; } = "";
    public string KeyColumn { get; set; } = "Id";
    public bool Enabled { get; set; } = true;
    public bool AutoCreateRemote { get; set; } = true;
}