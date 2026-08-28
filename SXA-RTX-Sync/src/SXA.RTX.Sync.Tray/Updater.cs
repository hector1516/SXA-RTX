using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using SXA.RTX.Sync.Core.Configuration;

namespace SXA.RTX.Sync.Tray;

public sealed record UpdateInfo(Version Version, string TagName, string AssetUrl, string Notes)
{
    public string DisplayName => $"v{Version.ToString(3)}";
}

public sealed class Updater
{
    private static readonly HttpClient Http = CreateHttpClient();
    private readonly SyncOptions _options;
    private readonly string _appDir;

    public Updater(SyncOptions options)
    {
        _options = options;
        _appDir = AppContext.BaseDirectory;
    }

    public Version CurrentVersion { get; } =
        typeof(Updater).Assembly.GetName().Version ?? new Version(1, 0, 0);

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SXA-RTX-Sync", "1.0"));
        return client;
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct)
    {
        var repo = string.IsNullOrWhiteSpace(_options.UpdateRepo) ? "hector1516/SXA-RTX" : _options.UpdateRepo;
        var url = $"https://api.github.com/repos/{repo}/releases/latest";
        try
        {
            using var response = await Http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                Diagnostics.Warn("Actualizador", $"GitHub respondió {(int)response.StatusCode}.");
                return null;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            var version = ParseVersion(tag);
            if (version is null)
            {
                return null;
            }

            if (version <= CurrentVersion)
            {
                return null;
            }

            var assetUrl = FindZipAsset(root);
            if (assetUrl is null)
            {
                Diagnostics.Warn("Actualizador", $"La versión {tag} no tiene ZIP publicado.");
                return null;
            }

            return new UpdateInfo(version, tag, assetUrl, body);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Diagnostics.Warn("Actualizador", $"No se pudo consultar actualizaciones: {ex.Message}");
            return null;
        }
    }

    private static string? FindZipAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets))
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (name.StartsWith("SXA-RTX-Sync-", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (asset.TryGetProperty("browser_download_url", out var u))
                {
                    return u.GetString();
                }
            }
        }

        return null;
    }

    private static Version? ParseVersion(string tag)
    {
        var match = Regex.Match(tag, @"v?(\d+)\.(\d+)\.(\d+)");
        if (!match.Success)
        {
            return null;
        }

        return new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
    }

    public async Task ApplyAsync(UpdateInfo info, CancellationToken ct)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "SXA-RTX-update");
        try
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }

            Directory.CreateDirectory(tempRoot);

            var zipPath = Path.Combine(tempRoot, "update.zip");
            using (var fs = File.Create(zipPath))
            {
                using var response = await Http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                await response.Content.CopyToAsync(fs, ct);
            }

            var extractDir = Path.Combine(tempRoot, "new");
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            var batPath = Path.Combine(_appDir, "update.bat");
            File.WriteAllText(batPath, BuildBatch(extractDir), new System.Text.UTF8Encoding(false));

            Process.Start(new ProcessStartInfo(batPath)
            {
                UseShellExecute = true,
                WorkingDirectory = _appDir,
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = $"\"{extractDir}\""
            });
        }
        catch (Exception ex)
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch { }
            Diagnostics.Error("Actualizador", "No se pudo aplicar la actualización.", ex);
            throw;
        }
    }

    private static string BuildBatch(string sourceDir)
    {
        return """
            @echo off
            setlocal
            set "SRC=%~1"
            set "DST=%~dp0"
            ping -n 4 127.0.0.1 >nul
            if exist "%DST%appsettings.json"  copy /y "%DST%appsettings.json"  "%DST%appsettings.json.bak"  >nul
            if exist "%DST%device.config"     copy /y "%DST%device.config"     "%DST%device.config.bak"     >nul
            xcopy /y /q /e /i "%SRC%\*" "%DST%" >nul
            if exist "%DST%appsettings.json.bak"  move /y "%DST%appsettings.json.bak"  "%DST%appsettings.json"  >nul
            if exist "%DST%device.config.bak"     move /y "%DST%device.config.bak"     "%DST%device.config"     >nul
            rmdir /s /q "%SRC%" >nul 2>nul
            start "" "%DST%SXA.RTX.Sync.Tray.exe"
            del "%~f0"
            endlocal
            """;
    }
}
