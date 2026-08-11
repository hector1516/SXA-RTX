using System.Management;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using SXA.RTX.Sync.Core.Configuration;

namespace SXA.RTX.Sync.Core.Device;

[SupportedOSPlatform("windows")]
public sealed class DeviceIdentity
{
    private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Cryptography";
    private const string MachineGuidValue = "MachineGuid";
    private readonly SyncOptions _options;

    public DeviceIdentity(IOptions<SyncOptions> options)
    {
        _options = options.Value;
    }

    public string DeviceId { get; private set; } = "";
    public string MachineGuid { get; private set; } = "";
    public string SmbiosUuid { get; private set; } = "";
    public string MachineName { get; private set; } = "";
    public string Model { get; private set; } = "";
    public DateTime GeneratedAt { get; private set; }

    public async Task LoadOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var file = ResolveConfigPath();
        if (File.Exists(file))
        {
            var saved = JsonSerializer.Deserialize<DeviceIdentityFile>(await File.ReadAllTextAsync(file, cancellationToken));
            if (saved is { DeviceId.Length: > 0 })
            {
                DeviceId = saved.DeviceId;
                MachineGuid = saved.MachineGuid ?? "";
                SmbiosUuid = saved.SmbiosUuid ?? "";
                MachineName = saved.MachineName ?? "";
                Model = saved.Model ?? "";
                GeneratedAt = saved.GeneratedAt;
                return;
            }
        }

        MachineGuid = ReadMachineGuid();
        SmbiosUuid = ReadSmbiosUuid();
        Model = ReadModel();
        MachineName = Environment.MachineName;
        DeviceId = ComputeDeviceId(MachineGuid, SmbiosUuid, MachineName);
        GeneratedAt = DateTime.UtcNow;

        var payload = new DeviceIdentityFile
        {
            DeviceId = DeviceId,
            MachineGuid = MachineGuid,
            SmbiosUuid = SmbiosUuid,
            MachineName = MachineName,
            Model = Model,
            GeneratedAt = GeneratedAt
        };

        var tmp = file + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        File.Move(tmp, file, overwrite: true);
    }

    private string ResolveConfigPath()
    {
        var configured = _options.DeviceConfigFile;
        var path = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured);
        return path;
    }

    private static string ReadMachineGuid()
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(RegistryKeyPath);
            return key?.GetValue(MachineGuidValue, "").ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string ReadSmbiosUuid()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct");
            foreach (var obj in searcher.Get())
            {
                var value = obj["UUID"]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // WMI puede no estar disponible en algunos entornos; se ignora.
        }

        return "";
    }

    private static string ReadModel()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_ComputerSystemProduct");
            foreach (var obj in searcher.Get())
            {
                var value = obj["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // WMI puede no estar disponible en algunos entornos; se ignora.
        }

        return "";
    }

    private static string ComputeDeviceId(string machineGuid, string smbiosUuid, string machineName)
    {
        var raw = $"{machineGuid}|{smbiosUuid}|{machineName}";
        if (string.IsNullOrWhiteSpace(machineGuid) && string.IsNullOrWhiteSpace(smbiosUuid))
        {
            throw new InvalidOperationException(
                "No se pudo obtener un identificador de hardware (MachineGuid ni SMBIOS UUID).");
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return "PC-" + Convert.ToHexString(hash)[..16];
    }
}

internal sealed class DeviceIdentityFile
{
    public string DeviceId { get; set; } = "";
    public string MachineGuid { get; set; } = "";
    public string SmbiosUuid { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTime GeneratedAt { get; set; }
}