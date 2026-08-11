using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SXA.RTX.Sync.Tray;

public sealed record ErrorRecord(DateTime Time, string Source, string Message, string? Stack = null);

internal static class Diagnostics
{
    private const int MaxRecentErrors = 200;
    private static readonly object Gate = new();
    private static readonly ConcurrentQueue<ErrorRecord> Recent = new();

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SXA-RTX", "logs");

    public static string LogPath { get; } = Path.Combine(LogDirectory, "sync.log");

    public static event Action<ErrorRecord>? ErrorRaised;

    public static IReadOnlyList<ErrorRecord> RecentErrors => Recent.ToArray();

    public static void Info(string category, string message) => Append("INFO", category, message);
    public static void Warn(string category, string message) => Append("WARN", category, message);

    public static void Error(string category, string message, Exception? exception = null)
    {
        var stack = exception?.ToString();
        Append("ERROR", category, message, stack);
        var record = new ErrorRecord(DateTime.Now, category, message, stack);
        Recent.Enqueue(record);
        while (Recent.Count > MaxRecentErrors)
        {
            Recent.TryDequeue(out _);
        }

        ErrorRaised?.Invoke(record);
    }

    public static void Append(string level, string category, string message, string? stack = null)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > 5 * 1024 * 1024)
                {
                    var rotated = Path.Combine(LogDirectory, $"sync-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                    try { File.Move(LogPath, rotated, overwrite: true); } catch { /* en uso, se ignora */ }
                }

                using var writer = new StreamWriter(LogPath, append: true);
                writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{category}] {message}");
                if (!string.IsNullOrWhiteSpace(stack))
                {
                    foreach (var line in stack.Split('\n'))
                    {
                        writer.WriteLine("    " + line.TrimEnd('\r'));
                    }
                }
            }
            catch
            {
                // Nunca dejar que un fallo de logging detenga la app.
            }
        }
    }
}

internal sealed class FileLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName);

    public void Dispose() { }
}

internal sealed class FileLogger(string categoryName) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var level = logLevel switch
        {
            LogLevel.Trace or LogLevel.Debug => "DBG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error or LogLevel.Critical => "ERROR",
            _ => "INFO"
        };

        var shortCategory = categoryName.Split('.').LastOrDefault() ?? categoryName;
        if (level == "ERROR")
        {
            Diagnostics.Error(shortCategory, formatter(state, exception), exception);
        }
        else
        {
            Diagnostics.Append(level, shortCategory, formatter(state, exception));
        }
    }
}
