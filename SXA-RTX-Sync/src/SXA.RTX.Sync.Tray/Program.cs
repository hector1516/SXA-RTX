using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SXA.RTX.Sync.Core.Device;
using SXA.RTX.Sync.Core.Sync;

namespace SXA.RTX.Sync.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += OnThreadException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var (store, options) = TrayComposition.LoadConfig();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddDebug();
            builder.AddProvider(new FileLoggerProvider());
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var iopts = Options.Create(options);

        var identity = new DeviceIdentity(iopts);
        var schema = new SchemaManager(iopts, loggerFactory.CreateLogger<SchemaManager>());
        var engine = new SyncEngine(iopts, loggerFactory.CreateLogger<SyncEngine>());
        var registry = new DeviceRegistry(iopts, loggerFactory.CreateLogger<DeviceRegistry>());
        var manager = new SyncManager(
            store, engine, schema, registry, identity,
            loggerFactory.CreateLogger<SyncManager>(), options);

        loggerFactory.CreateLogger("SXA.RTX.Sync.Tray.Program").LogInformation(
            "Inicio de SXA RTX Sync. Log de errores: {log}", Diagnostics.LogPath);

        ShowSplash();

        Application.Run(new MainForm(manager));
    }

    private static void ShowSplash()
    {
        try
        {
            var thread = new Thread(() =>
            {
                using var splash = new SplashScreen();
                Application.Run(splash);
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }
        catch (Exception ex)
        {
            Diagnostics.Error("Splash", "No se pudo mostrar la pantalla de inicio.", ex);
        }
    }

    private static void OnThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
    {
        Diagnostics.Error("UI", "Error no controlado en la interfaz. La app continúa.", e.Exception);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Diagnostics.Error("App", "Excepción no controlada (el proceso finalizará).",
            e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        Diagnostics.Error("Task", "Error en una tarea en segundo plano.", e.Exception);
    }
}
