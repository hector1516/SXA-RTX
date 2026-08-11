using System.Text.Json;

namespace SXA.RTX.Sync.Core.Configuration;

public sealed class ConfigStore
{
    private readonly string _path;

    public ConfigStore(string configFile)
    {
        var configured = string.IsNullOrWhiteSpace(configFile) ? "appsettings.json" : configFile;
        _path = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured);
    }

    public string PathFile => _path;

    public SyncOptions LoadOrDefaults()
    {
        if (!File.Exists(_path))
        {
            return new SyncOptions();
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_path));
            if (doc.RootElement.TryGetProperty("Sync", out var sync) &&
                sync.ValueKind != JsonValueKind.Null)
            {
                return sync.Deserialize<SyncOptions>() ?? new SyncOptions();
            }
        }
        catch (JsonException)
        {
            // Config inválida: se devuelven valores por defecto.
        }

        return new SyncOptions();
    }

    public void Save(SyncOptions options)
    {
        var root = new Dictionary<string, object?> { ["Sync"] = options };
        var json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
    }
}