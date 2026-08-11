using SXA.RTX.Sync.Core.Configuration;
using SXA.RTX.Sync.Core.Device;
using SXA.RTX.Sync.Core.Sync;
using SXA.RTX.Sync.Worker;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddOptions<SyncOptions>()
    .Bind(builder.Configuration.GetSection("Sync"))
    .Validate(o =>
        !string.IsNullOrWhiteSpace(o.LocalConnectionString)
        && !string.IsNullOrWhiteSpace(o.RemoteConnectionString)
        && o.Tables.Count > 0
        && o.BatchSize > 0,
        "Configuración 'Sync' incompleta: cadenas de conexión y al menos una tabla requeridas.")
    .ValidateOnStart();

builder.Services.AddSingleton<DeviceIdentity>();
builder.Services.AddSingleton<SyncEngine>();
builder.Services.AddSingleton<SchemaManager>();
builder.Services.AddSingleton<DeviceRegistry>();
builder.Services.AddHostedService<SyncWorker>();

if (OperatingSystem.IsWindows())
{
    builder.Services.AddWindowsService(options => options.ServiceName = "SXA RTX Sync");
}

var host = builder.Build();
host.Run();