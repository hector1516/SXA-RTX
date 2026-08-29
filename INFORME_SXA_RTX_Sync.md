# SXA RTX Sync — Informe para el Cliente

**Sincronizador de pruebas RTX para equipos VTi / VTech**
Versión 1.1.0 — 11 de agosto de 2026

---

### 1. Resumen
SXA RTX Sync mantiene sincronizados los resultados de las máquinas de prueba (PCs con SQL Express local) hacia la base central de la empresa, de forma automática, sin intervención del operador y sin detener el trabajo aunque falle la red.

La aplicación vive en la **bandeja de Windows** (junto al reloj). No es un servicio: el usuario la ve, la pausa y revisa su estado.

---

### 2. Alcances

**Incluye**
- Sincronización **local → remoto** de las tablas que usted elija (solo INSERT y UPDATE).
- Creación automática en el servidor central de la tabla que no exista, con prefijo del tipo de máquina: `VTi_Tabla` o `VTech_Tabla`. Dos PCs del mismo tipo comparten la misma tabla.
- Columna `OrigenPC` en el central para saber de qué PC vino cada registro + catálogo `SXA_PCs` con tipo y modelo.
- Detección de errores sin detener el proceso, con aviso en bandeja y log.
- Actualización automática desde GitHub.

**No incluye**
- Sincronización de borrados ni de cambios de esquema (si agrega columnas, debe volver a configurar).
- Sincronización inversa (central → local).
- Reportes web — va en la siguiente fase (`feature/web-reportes`).

---

### 3. Cómo funciona (simple)
```
Máquina de prueba (SQL Express)  →  Trigger  →  Cola local (SXA_SyncLog)
                                          ↓
                                   Motor cada 10s toma un lote y hace UPSERT en el SQL central
                                          ↓
                                   Tabla central con OrigenPC + catálogo de PCs
```
Si la red se cae, la cola queda pendiente y se reintenta sola con espera creciente.

---

### 4. Instalación por primera vez (por PC)

1. Requisitos: Windows 10/11, .NET 10 Desktop Runtime, SQL Express local con autenticación de Windows (sin contraseña, `Integrated Security=true`).
2. Descargar `Setup_SXA_RTX_Sync_v1.1.0.exe` de `github.com/hector1516/SXA-RTX/releases` y ejecutar. Se instala en `%LOCALAPPDATA%\Programs\SXA RTX Sync`.
3. Dejar marcada **"Iniciar con Windows"** en el instalador (opcional).
4. Al abrir queda oculta en la bandeja. Doble clic para abrir el panel.

> **Alternativa ZIP:** descomprimir `SXA-RTX-Sync-v1.1.0-win-x64.zip` en `C:\Apps\SXA-RTX-Sync` y ejecutar el `.exe`. Para arranque automático crear acceso directo en `Win+R` → `shell:startup`.

---

### 5. Configuración (una sola vez)

Bandeja → **Configuración**:
- **Conexión Local:** ej. `Server=.\SQLEXPRESS;Database=MiDB;Integrated Security=true;TrustServerCertificate=True;`
- **Conexión Remota:** ej. `Server=192.168.1.10;Database=SXACentral;Integrated Security=true;...`
- **Tipo de máquina:** `VTi` o `VTech`  y  **Nombre del PC**
- **Tablas:** `Escanear local` y `Escanear remoto` → marcar las locales en "Usar" → `Auto-generar pares` → revisar la clave → `Guardar`.

Usted decide **qué tablas** se sincronizan. Puede agregar más después repitiendo el paso.

---

### 6. Tiempos de sincronización

- **Poll:** cada **10 segundos** el motor revisa la cola.
- **Lote:** hasta **500 filas** por ciclo.
- **Reintentos:** hasta 5, con espera que crece si falla la red (máx. 5 min).
- En la práctica es **casi tiempo real** (segundos) si la red está bien; si no, se pone al día solo al volver.

Consulte **Estado** (bandeja → Estado) para ver pendientes, sincronizadas y el catálogo de PCs.

---

### 7. Actualizaciones

Publicamos cada versión en GitHub Releases. La app, si `AutoCheckUpdates` está activo (por defecto sí), **chequea al arrancar y cada 60 minutos** en `github.com/hector1516/SXA-RTX/releases/latest`.

- Si `AutoInstallUpdates=true` (por defecto) **descarga, preserva su configuración y se reinicia sola**.
- Si no, avisa con globo y menú "Instalar actualización...".
- También puede forzar: bandeja → `Buscar actualizaciones...`.

Para publicar una versión nueva: subir versión en el proyecto → `.\publish.ps1` → `.\publish.ps1 -Push` (crea el tag y sube el ZIP/Setup).

La actualización **nunca sobreescribe** `appsettings.json` ni `device.config`.

---

### 8. Requisitos y seguridad

- Autenticación contra SQL por **usuario de Windows** (no se guardan contraseñas).
- Cada PC se identifica con un hash estable (`device.config`).
- No requiere abrir puertos extra, solo acceso SQL al central.

---

### 9. Soporte

- **Logs:** `%LOCALAPPDATA%\SXA-RTX\logs\sync.log` (rotativo, 5 MB) y ventana `Errores...` en la bandeja.
- **Pausa:** bandeja → Pausar/Reanudar.
- **Salir:** bandeja → Salir (pide confirmación).

---

*Documento preparado para entrega al cliente. Para dudas técnicas contactar al equipo de SXA.*
