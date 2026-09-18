using System.Data;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;

namespace SXA.RTX.Sync.Core.Sync;

public static class SqlDiscoveryService
{
    public static async Task<IReadOnlyList<string>> GetLocalInstancesAsync(CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Registro local (instancias instaladas)
        TryAddFromRegistry(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL", result);
        TryAddFromRegistry(@"SOFTWARE\WOW6432Node\Microsoft\Microsoft SQL Server\Instance Names\SQL", result);

        // LocalDB
        foreach (var lb in GetLocalDbInstances())
        {
            result.Add(lb);
        }

        // Siempre ofrecer opciones basicas
        result.Add(".");
        result.Add("(local)");
        result.Add(@".\SQLEXPRESS");
        result.Add("(localdb)\\MSSQLLocalDB");

        return result.OrderBy(x => x).ToList();
    }

    public static async Task<IReadOnlyList<string>> GetRemoteInstancesAsync(string host, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return Array.Empty<string>();
        }

        host = host.Trim();

        // Intento via SQL Browser (UDP 1434)
        try
        {
            var browser = await QuerySqlBrowserAsync(host, ct);
            if (browser.Count > 0)
            {
                return browser;
            }
        }
        catch { }

        // Sin Browser, sugerir default y SQLEXPRESS
        return new[] { host, $@"{host}\SQLEXPRESS" };
    }

    public static async Task<IReadOnlyList<string>> GetDatabasesAsync(string connectionString, CancellationToken ct)
    {
        var list = new List<string>();
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        using var cmd = new SqlCommand("SELECT name FROM sys.databases WHERE state = 0 ORDER BY name;", conn) { CommandTimeout = 10 };
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(reader.GetString(0));
        }
        return list;
    }

    public static string BuildLocalConnectionString(string instance, string database)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = string.IsNullOrWhiteSpace(instance) ? "." : instance.Trim(),
            InitialCatalog = string.IsNullOrWhiteSpace(database) ? "master" : database.Trim(),
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 5
        };
        return builder.ConnectionString;
    }

    public static string BuildRemoteConnectionString(string host, string instance, string database, string user, string password, string? port = null)
    {
        var dataSource = host.Trim();
        if (!string.IsNullOrWhiteSpace(instance))
        {
            var inst = instance.Trim();
            // Si instance ya contiene host\, no duplicar
            if (!inst.StartsWith(host + "\\", StringComparison.OrdinalIgnoreCase) && !inst.Contains("\\"))
            {
                dataSource = $@"{host}\{inst}";
            }
            else if (inst.Contains("\\"))
            {
                dataSource = inst;
            }
        }
        if (!string.IsNullOrWhiteSpace(port))
        {
            dataSource = $"{dataSource},{port.Trim()}";
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = dataSource,
            InitialCatalog = string.IsNullOrWhiteSpace(database) ? "master" : database.Trim(),
            IntegratedSecurity = false,
            UserID = user.Trim(),
            Password = password,
            TrustServerCertificate = true,
            ConnectTimeout = 5
        };
        return builder.ConnectionString;
    }

    private static void TryAddFromRegistry(string path, HashSet<string> result)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(path)
                ?? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(path);
            if (key is null) return;
            foreach (var name in key.GetValueNames())
            {
                if (string.Equals(name, "MSSQLSERVER", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(".");
                    result.Add("(local)");
                    result.Add(Environment.MachineName);
                }
                else
                {
                    result.Add($@".\{name}");
                    result.Add($@"{Environment.MachineName}\{name}");
                    result.Add($@"(local)\{name}");
                }
            }
        }
        catch { }
    }

    private static IReadOnlyList<string> GetLocalDbInstances()
    {
        var result = new List<string>();
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("sqllocaldb", "info")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return result;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = line.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    result.Add($@"(localdb)\{name}");
                }
            }
        }
        catch { }
        return result;
    }

    private static async Task<List<string>> QuerySqlBrowserAsync(string host, CancellationToken ct)
    {
        var result = new List<string>();
        using var udp = new UdpClient();
        udp.Client.ReceiveTimeout = 3000;
        var endpoint = new IPEndPoint(IPAddress.Parse(host), 1434);
        // Si host es nombre, resolver
        if (!IPAddress.TryParse(host, out _))
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct);
            var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ipv4 is null) return result;
            endpoint = new IPEndPoint(ipv4, 1434);
        }

        var request = new byte[] { 0x02 };
        await udp.SendAsync(request, request.Length, endpoint);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var response = await udp.ReceiveAsync(cts.Token);
            var text = Encoding.ASCII.GetString(response.Buffer);
            // Formato: ServerName;INSTANCE;IsClustered;No;Version;...;;ServerName;INSTANCE2;...
            var entries = text.Split(";;", StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in entries)
            {
                string? instanceName = null;
                var parts = entry.Split(';');
                for (int i = 0; i + 1 < parts.Length; i += 2)
                {
                    if (parts[i].Equals("InstanceName", StringComparison.OrdinalIgnoreCase))
                    {
                        instanceName = parts[i + 1];
                    }
                }
                if (!string.IsNullOrWhiteSpace(instanceName))
                {
                    if (instanceName.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(host);
                    }
                    else
                    {
                        result.Add($@"{host}\{instanceName}");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
    }
}
