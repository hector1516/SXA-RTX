using System.Data;
using Microsoft.Data.SqlClient;

namespace SXA.RTX.Sync.Tray;

public sealed class StatusForm : Form
{
    private readonly SyncManager _manager;
    private readonly DataGridView _dgvSummary;
    private readonly DataGridView _dgvRecent;
    private readonly DataGridView _dgvDevices;
    private readonly FlatButton _btnRefresh;
    private readonly Label _lblInfo;
    private readonly System.Windows.Forms.Timer _timer;

    public StatusForm(SyncManager manager)
    {
        _manager = manager;
        Text = "Estado de sincronización";
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        ClientSize = new Size(1024, 720);
        MinimumSize = new Size(900, 640);
        UiTheme.Apply(this);
        Icon = IconLoader.AppIcon;

        var header = new HeaderPanel
        {
            Dock = DockStyle.Top,
            Height = 136,
            Title = "Estado de sincronización",
            Subtitle = "Resumen de tablas, últimas filas y PCs registrados en el catálogo.",
            LogoSize = 116
        };

        var topBar = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = UiTheme.Bg };
        _lblInfo = new Label
        {
            Dock = DockStyle.Fill,
            Text = ".",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(24, 0, 0, 0),
            ForeColor = UiTheme.TextDim,
            Font = UiTheme.SmallFont,
            BackColor = Color.Transparent
        };
        _btnRefresh = new FlatButton
        {
            Text = "Refrescar",
            Dock = DockStyle.Right,
            Width = 180,
            FillColor = UiTheme.Primary,
            FillColorHover = UiTheme.PrimaryHover,
            Font = UiTheme.BodyFont
        };
        topBar.Controls.Add(_lblInfo);
        topBar.Controls.Add(_btnRefresh);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiTheme.Bg,
            Padding = new Padding(16, 0, 16, 16)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 34));

        _dgvSummary = CreateGrid();
        _dgvRecent = CreateGrid();
        _dgvDevices = CreateGrid();
        root.Controls.Add(MakeSection("Resumen por tabla", _dgvSummary), 0, 0);
        root.Controls.Add(MakeSection("Últimos sincronizados (local)", _dgvRecent), 0, 1);
        root.Controls.Add(MakeSection("PCs registrados (catálogo remoto)", _dgvDevices), 0, 2);

        var host = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        host.Controls.Add(root);
        host.Controls.Add(topBar);

        Controls.Add(host);
        Controls.Add(header);

        _btnRefresh.Click += async (_, _) => await RefreshAllAsync();
        Load += async (_, _) => await RefreshAllAsync();

        _timer = new System.Windows.Forms.Timer { Interval = 15000 };
        _timer.Tick += async (_, _) => await RefreshAllAsync();
        _timer.Start();
    }

    private static Panel MakeSection(string title, DataGridView grid)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 10), BackColor = UiTheme.Bg };
        var label = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Text = title,
            ForeColor = UiTheme.Accent,
            Font = new Font(UiTheme.BodyFont, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = UiTheme.Bg
        };
        grid.Dock = DockStyle.Fill;
        panel.Controls.Add(grid);
        panel.Controls.Add(label);
        return panel;
    }

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            RowTemplate = { Height = 38 }
        };
        UiTheme.StyleGrid(grid);
        grid.ColumnHeadersHeight = 44;
        return grid;
    }

    private async Task RefreshAllAsync()
    {
        _btnRefresh.Enabled = false;
        _lblInfo.Text = "Actualizando...";
        try
        {
            await LoadSummaryAsync();
            await LoadRecentAsync();
            await LoadDevicesAsync();
            _lblInfo.Text = $"Actualizado: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _lblInfo.Text = $"Error: {ex.Message}";
            Diagnostics.Error("Estado", "No se pudo actualizar el estado.", ex);
        }
        finally
        {
            _btnRefresh.Enabled = true;
        }
    }

    private async Task LoadSummaryAsync()
    {
        _dgvSummary.Rows.Clear();
        _dgvSummary.Columns.Clear();
        _dgvSummary.Columns.Add("TableName", "Tabla");
        _dgvSummary.Columns.Add("Pend", "Pendientes");
        _dgvSummary.Columns.Add("EnProceso", "En proceso");
        _dgvSummary.Columns.Add("Ok", "Sincronizadas");
        _dgvSummary.Columns.Add("Err", "Errores");
        _dgvSummary.Columns.Add("Ultima", "Última");

        const string sql = """
            SELECT TableName,
                   SUM(CASE WHEN Status = 0 THEN 1 ELSE 0 END) AS [Pend],
                   SUM(CASE WHEN Status = 1 THEN 1 ELSE 0 END) AS [EnProceso],
                   SUM(CASE WHEN Status = 2 THEN 1 ELSE 0 END) AS [Ok],
                   SUM(CASE WHEN Status = -1 THEN 1 ELSE 0 END) AS [Err],
                   MAX(DoneAt) AS [Ultima]
            FROM dbo.SXA_SyncLog
            GROUP BY TableName
            ORDER BY TableName;
            """;

        await using var conn = new SqlConnection(_manager.CurrentOptions.LocalConnectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 20 };
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var t = Convert.ToString(reader["TableName"]) ?? "";
            var ult = reader["Ultima"] == DBNull.Value ? "" : Convert.ToDateTime(reader["Ultima"]).ToString("yyyy-MM-dd HH:mm");
            _dgvSummary.Rows.Add(t, reader["Pend"], reader["EnProceso"], reader["Ok"], reader["Err"], ult);
        }
    }

    private async Task LoadRecentAsync()
    {
        _dgvRecent.Rows.Clear();
        _dgvRecent.Columns.Clear();
        _dgvRecent.Columns.Add("TableName", "Tabla");
        _dgvRecent.Columns.Add("KeyValue", "Clave");
        _dgvRecent.Columns.Add("DoneAt", "Fecha");
        _dgvRecent.Columns.Add("LastError", "Nota");

        const string sql = """
            SELECT TOP 30 TableName, KeyValue, DoneAt, LastError
            FROM dbo.SXA_SyncLog
            WHERE Status = 2
            ORDER BY DoneAt DESC, Id DESC;
            """;

        await using var conn = new SqlConnection(_manager.CurrentOptions.LocalConnectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 20 };
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var done = reader["DoneAt"] == DBNull.Value ? "" : Convert.ToDateTime(reader["DoneAt"]).ToString("yyyy-MM-dd HH:mm");
            _dgvRecent.Rows.Add(
                Convert.ToString(reader["TableName"]) ?? "",
                Convert.ToString(reader["KeyValue"]) ?? "",
                done,
                reader["LastError"] == DBNull.Value ? "" : Convert.ToString(reader["LastError"]));
        }
    }

    private async Task LoadDevicesAsync()
    {
        _dgvDevices.Rows.Clear();
        _dgvDevices.Columns.Clear();
        _dgvDevices.Columns.Add("DeviceId", "Dispositivo");
        _dgvDevices.Columns.Add("MachineName", "PC");
        _dgvDevices.Columns.Add("MachineType", "Tipo");
        _dgvDevices.Columns.Add("Model", "Modelo");
        _dgvDevices.Columns.Add("LastSeenAt", "Último contacto");

        var table = QuoteTable(_manager.CurrentOptions.DeviceCatalogTable);
        var sql = $"""
            SELECT TOP 50 DeviceId, NombrePC, TipoMaquina, Modelo, UltimoContacto
            FROM {table}
            ORDER BY UltimoContacto DESC;
            """;

        await using var conn = new SqlConnection(_manager.CurrentOptions.RemoteConnectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 20 };
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var seen = reader["UltimoContacto"] == DBNull.Value ? "" : Convert.ToDateTime(reader["UltimoContacto"]).ToString("yyyy-MM-dd HH:mm (UTC)");
            _dgvDevices.Rows.Add(
                Convert.ToString(reader["DeviceId"]) ?? "",
                Convert.ToString(reader["NombrePC"]) ?? "",
                Convert.ToString(reader["TipoMaquina"]) ?? "",
                Convert.ToString(reader["Modelo"]) ?? "",
                seen);
        }
    }

    private static string QuoteTable(string qualified)
    {
        var parts = qualified.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return "[dbo].[SXA_PCs]";
        }

        if (parts.Length == 1)
        {
            return $"[dbo].[{parts[0]}]";
        }

        return string.Join(".", parts.Select(p => $"[{p}]"));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        base.OnFormClosed(e);
    }
}
