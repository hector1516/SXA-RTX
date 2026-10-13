# Patrón de conexión SQL (reutilizable)

Diseño usado en SXA-RTX Sync para conectar a **SQL local (Windows Auth)** y **SQL remoto (IP + usuario/contraseña)** de forma eficiente y segura. Pensado para copiarse a otra app.

## Principios

1. **Nunca** concatenar cadenas a mano: usar `SqlConnectionStringBuilder`.
2. **Local = Windows Auth** (`Integrated Security=true`), sin usuario/contraseña.
3. **Remoto = IP + instancia opcional + usuario/contraseña**.
4. **Endurecer** toda conexión: `Max Pool Size` bajo y `Connect Timeout` corto (no saturar el servidor).
5. **Descubrir** instancias y bases de datos, no pedirlas a mano.
6. **Distinguir error de credenciales** (18456) del resto para no martillar el servidor.
7. Todo con **parámetros** y **nombres entre corchetes**.

---

## 1. Construir cadenas (con blindaje)

```csharp
using Microsoft.Data.SqlClient;

public static class ConnectionFactory
{
    public const int MaxPoolSize = 10;
    public const int ConnectTimeoutSeconds = 8;

    // Local: Windows Auth, sin contraseña
    public static string Local(string instance, string database) =>
        new SqlConnectionStringBuilder
        {
            DataSource = string.IsNullOrWhiteSpace(instance) ? "." : instance.Trim(),
            InitialCatalog = string.IsNullOrWhiteSpace(database) ? "master" : database.Trim(),
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 5,
            MaxPoolSize = MaxPoolSize
        }.ConnectionString;

    // Remoto: IP + instancia + usuario/contraseña
    public static string Remote(string host, string instance, string database,
                               string user, string password, string? port = null)
    {
        var ds = host.Trim();
        if (!string.IsNullOrWhiteSpace(instance))
        {
            var inst = instance.Trim();
            ds = inst.Contains('\\') ? inst : $@"{host}\{inst}";
        }
        if (!string.IsNullOrWhiteSpace(port)) ds = $"{ds},{port.Trim()}";

        return new SqlConnectionStringBuilder
        {
            DataSource = ds,
            InitialCatalog = string.IsNullOrWhiteSpace(database) ? "master" : database.Trim(),
            IntegratedSecurity = false,
            UserID = user.Trim(),
            Password = password,
            TrustServerCertificate = true,
            ConnectTimeout = 5,
            MaxPoolSize = MaxPoolSize
        }.ConnectionString;
    }

    // Endurece una cadena ya existente (cap de pool y timeout)
    public static string Harden(string cs, int maxPool = MaxPoolSize, int timeout = ConnectTimeoutSeconds)
    {
        if (string.IsNullOrWhiteSpace(cs)) return cs;
        try
        {
            var b = new SqlConnectionStringBuilder(cs);
            if (b.MaxPoolSize <= 0 || b.MaxPoolSize > maxPool) b.MaxPoolSize = maxPool;
            if (b.ConnectTimeout <= 0 || b.ConnectTimeout > timeout) b.ConnectTimeout = timeout;
            return b.ConnectionString;
        }
        catch { return cs; }
    }
}
```

**Por qué el blindaje**: una app con contraseña mal que reintenta fuerte abre muchas conexiones y satura el motor (afecta a otras PCs). Pool bajo + timeout corto + backoff evita eso.

---

## 2. Descubrir instancias y bases

### Local (registro + LocalDB + opciones básicas)

```csharp
// Instancias instaladas (registro, 64 y 32 bits)
TryAddFromRegistry(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL", result);
TryAddFromRegistry(@"SOFTWARE\WOW6432Node\...\Instance Names\SQL", result);

// LocalDB
// ejecuta: sqllocaldb info  -> cada línea -> (localdb)\<nombre>

// Básicas: ".", "(local)", ".\SQLEXPRESS", "(localdb)\MSSQLLocalDB"
```

### Remoto (SQL Browser, UDP 1434)

```csharp
using var udp = new UdpClient();
udp.Client.ReceiveTimeout = 3000;
var ep = new IPEndPoint(IPAddress.Parse(host), 1434);
await udp.SendAsync(new byte[] { 0x02 }, 1, ep);
var resp = await udp.ReceiveAsync(cts.Token);
var text = Encoding.ASCII.GetString(resp.Buffer);
// Formato: ServerName;InstanceName;MSSQLSERVER;IsClustered;No;Version;...;;
// Si no responde (Browser apagado/firewall) -> dejar instancia editable a mano.
```

### Bases de datos

```csharp
public static async Task<IReadOnlyList<string>> DatabasesAsync(string cs, CancellationToken ct)
{
    var list = new List<string>();
    await using var conn = new SqlConnection(ConnectionFactory.Harden(cs));
    await conn.OpenAsync(ct);
    using var cmd = new SqlCommand("SELECT name FROM sys.databases WHERE state = 0 ORDER BY name;", conn) { CommandTimeout = 10 };
    using var r = await cmd.ExecuteReaderAsync(ct);
    while (await r.ReadAsync(ct)) list.Add(r.GetString(0));
    return list;
}
```

---

## 3. Probar conexión y detectar credenciales malas

```csharp
public static async Task<(bool Ok, string Message, bool IsAuthError)> TestAsync(string cs, CancellationToken ct)
{
    try
    {
        await using var conn = new SqlConnection(ConnectionFactory.Harden(cs));
        await conn.OpenAsync(ct);
        using var cmd = new SqlCommand("SELECT 1;", conn) { CommandTimeout = 10 };
        await cmd.ExecuteScalarAsync(ct);
        return (true, "Conexión correcta.", false);
    }
    catch (Exception ex)
    {
        var isAuth = IsLoginFailure(ex);
        return (false, isAuth ? "Credenciales rechazadas." : ex.Message, isAuth);
    }
}

public static bool IsLoginFailure(Exception ex)
{
    for (var e = ex; e is not null; e = e.InnerException)
        if (e is SqlException s && s.Number == 18456) return true;   // Login failed
    return false;
}
```

**Uso**: al guardar la configuración, validar local y remoto; si es `IsAuthError`, avisar y **no** martillar.

---

## 4. Backoff + auto-pausa ante credenciales malas

- En el ciclo de sincronización, al detectar `18456`: lanzar una excepción propia y aplicar **espera máxima** (ej. 5 min).
- Tras N (ej. 3) fallos de credenciales seguidos: **pausar** el envío y avisar; reanudar al corregir. Así una contraseña mal no afecta al resto.

```csharp
catch (SyncAuthException) { failures = 6; authFailures++; if (authFailures >= 3) Pause(); }
catch (Exception)          { failures = Math.Min(failures + 1, 6); }
var delay = Math.Min(pollSeconds * Math.Pow(2, failures), 300); // tope 5 min
```

---

## 5. Upsert idempotente con PK compuesta (varias PCs → misma tabla)

**Idea**: cada fila remota lleva la columna de origen (`OrigenPC`) con el **DeviceId estable** de la PC; la PK es `(Id, OrigenPC)`.

- INSERT: si la clave ya existe → **error 2601/2627** → se marca como sincronizado (no duplica).
- UPDATE: `WHERE [Id] = @key AND [OrigenPC] = @origin`.

```csharp
private static bool IsDuplicateKey(SqlException ex) => ex.Number is 2601 or 2627;

// INSERT
var sql = $"INSERT INTO {remoteQ} ({cols}) VALUES ({ps});";
// UPDATE
var sql = $"UPDATE {remoteQ} SET {sets} WHERE [{key}] = @key AND [{origin}] = @origin;";
```

- Identificadores siempre entre corchetes: `[dbo].[Tabla]`, `[Columna]`.
- Parámetros tipados; nunca interpolación de valores.

---

## 6. Arranque no bloqueante

- Mostrar UI de inmediato; la inicialización (crear esquema, descubrir) en segundo plano con **timeout** y `try/catch`, reportando progreso.
- Nunca dejar la app "pegada" en "Iniciando…".

---

## 7. Auto-creación de esquema (idempotente)

- Local: `IF OBJECT_ID(...) IS NULL` para la tabla cola + `CREATE OR ALTER TRIGGER` (AFTER INSERT, UPDATE) que encola la PK.
- Remoto: `IF OBJECT_ID(...) IS NULL` para crear la tabla espejo + columna `OrigenPC` + PK compuesta `(Id, OrigenPC)`.

---

## 8. Configuración como JSON

- Guardar/leer `SyncOptions` (cadenas, tipo de máquina, tablas) con `System.Text.Json`.
- Exportar/Importar para clonar la config en otra PC.

---

## Resumen para el otro agente

1. `SqlConnectionStringBuilder` para todo (nunca strings a mano).
2. Local = `Integrated Security=true`; Remoto = usuario/contraseña.
3. `Harden()` → `Max Pool Size` bajo + `Connect Timeout` corto en **cada** conexión.
4. Descubrir instancias (registro/LocalDB local; UDP 1434 remoto) y bases (`sys.databases`).
5. `18456` → excepción propia → backoff máximo + auto-pausa.
6. Upsert con PK `(Id, OrigenPC)`; duplicado 2601/2627 = ya sincronizado.
7. Arranque en segundo plano con timeout y progreso.
