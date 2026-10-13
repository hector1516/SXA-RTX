using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using SXA.RTX.Sync.Core.Configuration;
using SXA.RTX.Sync.Core.Sync;

namespace SXA.RTX.Sync.Tray;

public sealed class ConfigForm : Form
{
    private readonly SyncManager _manager;
    private TextBox _tbLocal = null!;
    private TextBox _tbRemote = null!;
    private TextBox _tbMachineType = null!;
    private TextBox _tbMachineName = null!;
    private ComboBox _cbLocalInstance = null!;
    private ComboBox _cbLocalDatabase = null!;
    private TextBox _tbRemoteHost = null!;
    private ComboBox _cbRemoteInstance = null!;
    private TextBox _tbRemoteUser = null!;
    private TextBox _tbRemotePassword = null!;
    private ComboBox _cbRemoteDatabase = null!;
    private Button _btnScanLocal = null!;
    private Button _btnScanRemote = null!;
    private DataGridView _dgvLocal = null!;
    private DataGridView _dgvRemote = null!;
    private DataGridView _dgvPairs = null!;
    private Label _lblSavedConfig = null!;
    private List<ScannedTable> _local = new();
    private List<ScannedTable> _remote = new();

    public ConfigForm(SyncManager manager)
    {
        _manager = manager;
        Text = "Configuración de sincronización";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        ClientSize = new Size(1100, 760);
        MinimumSize = new Size(960, 660);
        UiTheme.Apply(this);
        Icon = IconLoader.AppIcon;

        var header = new HeaderPanel
        {
            Dock = DockStyle.Top,
            Height = 118,
            Title = "Configuración",
            Subtitle = "Conexiones, enlace de tablas e identificación de la máquina.",
            LogoSize = 82
        };

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = UiTheme.BodyFont,
            Padding = new Point(28, 12)
        };
        var pageConn = new TabPage("1 · Conexiones") { BackColor = UiTheme.Bg, Padding = new Padding(16) };
        var pageTables = new TabPage("2 · Tablas y enlace") { BackColor = UiTheme.Bg, Padding = new Padding(16) };
        tabs.TabPages.Add(pageConn);
        tabs.TabPages.Add(pageTables);

        BuildConnectionsPage(pageConn);
        BuildTablesPage(pageTables);

        // Barra inferior
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = UiTheme.BgPanel, Padding = new Padding(20, 14, 20, 14) };
        var btnSave = CreateButton("Guardar y aplicar", 0, 0, 230, UiTheme.Primary, UiTheme.PrimaryHover);
        var btnCancel = CreateButton("Cancelar", 0, 0, 160, UiTheme.BgPanelAlt, UiTheme.BgHeader, UiTheme.Border);
        var btnExport = CreateButton("Exportar config", 0, 0, 220, UiTheme.BgPanelAlt, UiTheme.BgHeader, UiTheme.Border);
        var btnImport = CreateButton("Importar config", 0, 0, 220, UiTheme.BgPanelAlt, UiTheme.BgHeader, UiTheme.Border);
        btnExport.Anchor = btnImport.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnSave.Location = new Point(20, 16);
        btnCancel.Location = new Point(262, 16);
        btnExport.Location = new Point(Width, 16);   // se recoloca en Resize
        btnImport.Location = new Point(Width, 16);
        bottom.Controls.Add(btnSave);
        bottom.Controls.Add(btnCancel);
        bottom.Controls.Add(btnExport);
        bottom.Controls.Add(btnImport);
        void LayoutBottom()
        {
            var w = bottom.ClientSize.Width;
            btnImport.Location = new Point(w - btnImport.Width - 20, 16);
            btnExport.Location = new Point(btnImport.Left - btnExport.Width - 12, 16);
        }
        bottom.Resize += (_, _) => LayoutBottom();
        bottom.HandleCreated += (_, _) => LayoutBottom();
        LayoutBottom();

        btnSave.Click += async (_, _) => await SaveAsync();
        btnCancel.Click += (_, _) => Close();
        btnExport.Click += (_, _) => ExportConfig();
        btnImport.Click += async (_, _) => await ImportConfigAsync();

        Controls.Add(tabs);
        Controls.Add(bottom);
        Controls.Add(header);

        Load += async (_, _) => await OnLoadAsync();
    }

    private void BuildConnectionsPage(TabPage page)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UiTheme.Bg
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var left = NewColumnPanel();
        // Local
        _cbLocalInstance = CreateCombo(0, 0, 360);
        var btnRefreshLocalInst = CreateButton("Actualizar", 0, 0, 140, UiTheme.BgPanelAlt, UiTheme.BgHeader, UiTheme.Border);
        _cbLocalDatabase = CreateCombo(0, 0, 360);
        var btnRefreshLocalDb = CreateButton("Actualizar", 0, 0, 140, UiTheme.BgPanelAlt, UiTheme.BgHeader, UiTheme.Border);
        _tbLocal = CreateTextBox(0, 0, 512);
        _tbLocal.ReadOnly = true;
        _tbLocal.BackColor = Color.FromArgb(28, 32, 48);
        var chkLocalEdit = CreateCheck("Editar cadena manualmente", 0, 0);
        _btnScanLocal = CreateButton("Escanear tablas local", 0, 0, 260, UiTheme.Primary, UiTheme.PrimaryHover);

        left.Controls.Add(FieldLabel("Instancia local (Windows Auth)"));
        left.Controls.Add(HBox(46, _cbLocalInstance, btnRefreshLocalInst));
        left.Controls.Add(FieldLabel("Base de datos local"));
        left.Controls.Add(HBox(46, _cbLocalDatabase, btnRefreshLocalDb));
        left.Controls.Add(FieldLabel("Cadena de conexión local"));
        left.Controls.Add(HBox(40, _tbLocal));
        left.Controls.Add(HBox(34, chkLocalEdit));
        left.Controls.Add(HBox(56, _btnScanLocal));
        left.Controls.Add(FieldLabel("Tipo de máquina (VTi / VTech)"));
        _tbMachineType = CreateTextBox(0, 0, 240);
        left.Controls.Add(HBox(40, _tbMachineType));
        left.Controls.Add(FieldLabel("Nombre del PC"));
        _tbMachineName = CreateTextBox(0, 0, 320);
        left.Controls.Add(HBox(40, _tbMachineName));

        var right = NewColumnPanel();
        // Remota
        _tbRemoteHost = CreateTextBox(0, 0, 340);
        var btnScanRemoteInst = CreateButton("Escanear instancias", 0, 0, 220, UiTheme.Primary, UiTheme.PrimaryHover);
        _cbRemoteInstance = CreateCombo(0, 0, 570);
        _cbRemoteInstance.DropDownStyle = ComboBoxStyle.DropDown;
        _tbRemoteUser = CreateTextBox(0, 0, 260);
        _tbRemotePassword = CreateTextBox(0, 0, 260);
        _tbRemotePassword.UseSystemPasswordChar = true;
        _cbRemoteDatabase = CreateCombo(0, 0, 360);
        var btnRefreshRemoteDb = CreateButton("Actualizar", 0, 0, 140, UiTheme.BgPanelAlt, UiTheme.BgHeader, UiTheme.Border);
        _tbRemote = CreateTextBox(0, 0, 570);
        _tbRemote.ReadOnly = true;
        _tbRemote.BackColor = Color.FromArgb(28, 32, 48);
        var chkRemoteEdit = CreateCheck("Editar cadena manualmente", 0, 0);
        _btnScanRemote = CreateButton("Escanear tablas remoto", 0, 0, 260, UiTheme.Primary, UiTheme.PrimaryHover);

        right.Controls.Add(FieldLabel("Servidor remoto (IP / Host)"));
        right.Controls.Add(HBox(46, _tbRemoteHost, btnScanRemoteInst));
        right.Controls.Add(FieldLabel("Instancia remota"));
        right.Controls.Add(HBox(46, _cbRemoteInstance));
        right.Controls.Add(FieldLabel("Usuario  ·  Contraseña"));
        right.Controls.Add(HBox(40, _tbRemoteUser, _tbRemotePassword));
        right.Controls.Add(FieldLabel("Base de datos remota"));
        right.Controls.Add(HBox(46, _cbRemoteDatabase, btnRefreshRemoteDb));
        right.Controls.Add(FieldLabel("Cadena de conexión remota"));
        right.Controls.Add(HBox(40, _tbRemote));
        right.Controls.Add(HBox(34, chkRemoteEdit));
        right.Controls.Add(HBox(56, _btnScanRemote));

        root.Controls.Add(left, 0, 0);
        root.Controls.Add(right, 1, 0);
        page.Controls.Add(root);

        chkLocalEdit.CheckedChanged += (_, _) => _tbLocal.ReadOnly = !chkLocalEdit.Checked;
        chkRemoteEdit.CheckedChanged += (_, _) => _tbRemote.ReadOnly = !chkRemoteEdit.Checked;

        _cbLocalInstance.SelectedIndexChanged += (_, _) => UpdateLocalConnectionString();
        _cbLocalDatabase.SelectedIndexChanged += (_, _) => UpdateLocalConnectionString();
        _cbLocalDatabase.DropDown += async (_, _) => await RefreshLocalDatabasesAsync();

        _tbRemoteHost.TextChanged += (_, _) => UpdateRemoteConnectionString();
        _cbRemoteInstance.SelectedIndexChanged += (_, _) => UpdateRemoteConnectionString();
        _cbRemoteDatabase.SelectedIndexChanged += (_, _) => UpdateRemoteConnectionString();
        _tbRemoteUser.TextChanged += (_, _) => UpdateRemoteConnectionString();
        _tbRemotePassword.TextChanged += (_, _) => UpdateRemoteConnectionString();
        _cbRemoteDatabase.DropDown += async (_, _) => await RefreshRemoteDatabasesAsync();

        btnRefreshLocalInst.Click += async (_, _) => await RefreshLocalInstancesAsync();
        btnRefreshLocalDb.Click += async (_, _) => await RefreshLocalDatabasesAsync();
        btnScanRemoteInst.Click += async (_, _) => await RefreshRemoteInstancesAsync();
        btnRefreshRemoteDb.Click += async (_, _) => await RefreshRemoteDatabasesAsync();

        _btnScanLocal.Click += async (_, _) => await ScanLocalAsync();
        _btnScanRemote.Click += async (_, _) => await ScanRemoteAsync();
    }

    private void BuildTablesPage(TabPage page)
    {
        _lblSavedConfig = new Label
        {
            Dock = DockStyle.Top,
            Height = 84,
            Padding = new Padding(16, 10, 16, 10),
            BackColor = UiTheme.BgPanel,
            ForeColor = UiTheme.Text,
            Font = UiTheme.SmallFont,
            Text = "Configuración guardada: —"
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 60,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(4, 6, 4, 6),
            BackColor = UiTheme.Bg
        };
        var btnPairs = CreateButton("Auto-generar pares", 0, 0, 240, UiTheme.Accent, UiTheme.AccentHover);
        var btnPreselect = CreateButton("Preseleccionar existentes", 0, 0, 260, UiTheme.Primary, UiTheme.PrimaryHover);
        var btnRemove = CreateButton("Quitar par", 0, 0, 170, UiTheme.BgPanelAlt, UiTheme.BgHeader, UiTheme.Border);
        actions.Controls.Add(btnPairs);
        actions.Controls.Add(btnPreselect);
        actions.Controls.Add(btnRemove);

        var grids = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 300,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UiTheme.Bg
        };
        grids.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grids.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _dgvLocal = CreateScanGrid(0, 0, 520, 280);
        _dgvLocal.Dock = DockStyle.Fill;
        _dgvRemote = CreateScanGrid(0, 0, 520, 280);
        _dgvRemote.Dock = DockStyle.Fill;
        var colL = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 8, 0), BackColor = UiTheme.Bg };
        var colR = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0), BackColor = UiTheme.Bg };
        colL.Controls.Add(_dgvLocal);
        colR.Controls.Add(_dgvRemote);
        grids.Controls.Add(colL, 0, 0);
        grids.Controls.Add(colR, 1, 0);

        var pairsTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 32,
            Text = "Enlace de tablas (Local → Remota)",
            ForeColor = UiTheme.Accent,
            Font = new Font(UiTheme.BodyFont, FontStyle.Bold),
            Padding = new Padding(4, 6, 0, 0),
            BackColor = UiTheme.Bg
        };

        _dgvPairs = CreatePairsGrid(0, 0, 900, 220);
        _dgvPairs.Dock = DockStyle.Fill;

        // Orden de docking (se agrega de abajo hacia arriba)
        page.Controls.Add(_dgvPairs);
        page.Controls.Add(pairsTitle);
        page.Controls.Add(grids);
        page.Controls.Add(actions);
        page.Controls.Add(_lblSavedConfig);

        btnPairs.Click += async (_, _) => await AutoGenerateAsync();
        btnPreselect.Click += (_, _) => PreselectExisting();
        btnRemove.Click += (_, _) => RemovePair();
    }

    private static FlowLayoutPanel NewColumnPanel()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(12),
            BackColor = UiTheme.Bg
        };
    }

    private static Label FieldLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Text = text,
            ForeColor = UiTheme.TextDim,
            Font = UiTheme.SmallFont,
            BackColor = Color.Transparent,
            Margin = new Padding(2, 8, 2, 4)
        };
    }

    private static Panel HBox(int height, params Control[] controls)
    {
        var panel = new Panel { AutoSize = false, Height = height, Width = 8 };
        var x = 0;
        foreach (var c in controls)
        {
            if (c.Height > height)
            {
                c.Height = height;
            }
            c.Location = new Point(x, 0);
            panel.Controls.Add(c);
            x += c.Width + 8;
        }
        panel.Width = x;
        return panel;
    }

    private async Task OnLoadAsync()
    {
        var opts = _manager.CurrentOptions;
        ParseLocalConnectionString(opts.LocalConnectionString);
        ParseRemoteConnectionString(opts.RemoteConnectionString);
        _tbMachineType.Text = opts.MachineType;
        _tbMachineName.Text = opts.MachineName;
        _tbLocal.Text = opts.LocalConnectionString;
        _tbRemote.Text = opts.RemoteConnectionString;

        // Visualización de la configuración guardada actual
        _dgvPairs.Rows.Clear();
        foreach (var t in opts.Tables)
        {
            _dgvPairs.Rows.Add(t.LocalTable, t.RemoteTable, t.KeyColumn, t.Enabled ? "Guardada" : "Deshabilitada");
        }
        UpdateSavedConfigSummary();

        await RefreshLocalInstancesAsync();
        if (!string.IsNullOrWhiteSpace(_cbLocalInstance.Text))
        {
            await RefreshLocalDatabasesAsync();
        }
    }

    private void UpdateSavedConfigSummary()
    {
        try
        {
            var opts = _manager.CurrentOptions;
            var localB = SafeParse(opts.LocalConnectionString);
            var remoteB = SafeParse(opts.RemoteConnectionString);
            _lblSavedConfig.Text =
                $"Configuración guardada actual\r\n" +
                $"Tablas enlazadas: {opts.Tables.Count}    ·    Tipo de máquina: {(string.IsNullOrWhiteSpace(opts.MachineType) ? "—" : opts.MachineType)}\r\n" +
                $"Local:  {localB.DataSource}  /  {localB.InitialCatalog}      Remoto:  {remoteB.DataSource}  /  {remoteB.InitialCatalog}";
        }
        catch
        {
            _lblSavedConfig.Text = "Configuración guardada actual: —";
        }
    }

    private static SqlConnectionStringBuilder SafeParse(string cs)
    {
        try { return new SqlConnectionStringBuilder(cs); }
        catch { return new SqlConnectionStringBuilder(); }
    }

    private void ParseLocalConnectionString(string cs)
    {
        try
        {
            var b = new SqlConnectionStringBuilder(cs);
            _cbLocalInstance.Text = b.DataSource;
            _cbLocalDatabase.Text = b.InitialCatalog;
        }
        catch { }
    }

    private void ParseRemoteConnectionString(string cs)
    {
        try
        {
            var b = new SqlConnectionStringBuilder(cs);
            var ds = b.DataSource;
            // Separar host\instancia
            var slash = ds.IndexOf('\\');
            if (slash >= 0)
            {
                _tbRemoteHost.Text = ds.Substring(0, slash);
                var rest = ds.Substring(slash + 1);
                var comma = rest.IndexOf(',');
                _cbRemoteInstance.Text = comma >= 0 ? rest.Substring(0, comma) : rest;
            }
            else
            {
                var comma = ds.IndexOf(',');
                if (comma >= 0)
                {
                    _tbRemoteHost.Text = ds.Substring(0, comma);
                }
                else
                {
                    _tbRemoteHost.Text = ds;
                }
                _cbRemoteInstance.Text = "";
            }
            _tbRemoteUser.Text = b.UserID;
            _tbRemotePassword.Text = b.Password;
            _cbRemoteDatabase.Text = b.InitialCatalog;
        }
        catch { }
    }

    private void UpdateLocalConnectionString()
    {
        var cs = SqlDiscoveryService.BuildLocalConnectionString(_cbLocalInstance.Text, _cbLocalDatabase.Text);
        _tbLocal.Text = cs;
    }

    private void UpdateRemoteConnectionString()
    {
        var cs = SqlDiscoveryService.BuildRemoteConnectionString(_tbRemoteHost.Text, _cbRemoteInstance.Text, _cbRemoteDatabase.Text, _tbRemoteUser.Text, _tbRemotePassword.Text);
        _tbRemote.Text = cs;
    }

    private async Task RefreshLocalInstancesAsync()
    {
        try
        {
            var instances = await SqlDiscoveryService.GetLocalInstancesAsync(CancellationToken.None);
            var current = _cbLocalInstance.Text;
            _cbLocalInstance.Items.Clear();
            foreach (var inst in instances) _cbLocalInstance.Items.Add(inst);
            if (!string.IsNullOrWhiteSpace(current) && !_cbLocalInstance.Items.Contains(current))
            {
                _cbLocalInstance.Items.Add(current);
            }
            if (!string.IsNullOrWhiteSpace(current)) _cbLocalInstance.Text = current;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudieron listar instancias locales: {ex.Message}", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task RefreshLocalDatabasesAsync()
    {
        var instance = _cbLocalInstance.Text.Trim();
        if (string.IsNullOrWhiteSpace(instance))
        {
            MessageBox.Show(this, "Selecciona primero la instancia local.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            var cs = SqlDiscoveryService.BuildLocalConnectionString(instance, "master");
            var dbs = await SqlDiscoveryService.GetDatabasesAsync(cs, CancellationToken.None);
            var current = _cbLocalDatabase.Text;
            _cbLocalDatabase.Items.Clear();
            foreach (var db in dbs) _cbLocalDatabase.Items.Add(db);
            if (!string.IsNullOrWhiteSpace(current) && !_cbLocalDatabase.Items.Contains(current))
            {
                _cbLocalDatabase.Items.Add(current);
            }
            if (!string.IsNullOrWhiteSpace(current)) _cbLocalDatabase.Text = current;
            UpdateLocalConnectionString();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudieron listar bases locales: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RefreshRemoteInstancesAsync()
    {
        var host = _tbRemoteHost.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            MessageBox.Show(this, "Escribe primero la IP o nombre del servidor remoto.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            var instances = await SqlDiscoveryService.GetRemoteInstancesAsync(host, CancellationToken.None);
            var current = _cbRemoteInstance.Text;
            _cbRemoteInstance.Items.Clear();
            foreach (var inst in instances)
            {
                // Quitar prefijo host\ para mostrar solo instancia
                var name = inst.Contains("\\") ? inst.Substring(inst.IndexOf("\\") + 1) : inst;
                if (name.Equals(host, StringComparison.OrdinalIgnoreCase)) name = "";
                if (!string.IsNullOrWhiteSpace(name) && !_cbRemoteInstance.Items.Contains(name))
                {
                    _cbRemoteInstance.Items.Add(name);
                }
                else if (string.IsNullOrWhiteSpace(name) && !_cbRemoteInstance.Items.Contains("(default)"))
                {
                    _cbRemoteInstance.Items.Add("(default)");
                }
            }
            if (!string.IsNullOrWhiteSpace(current) && !_cbRemoteInstance.Items.Contains(current))
            {
                _cbRemoteInstance.Items.Add(current);
            }
            if (instances.Count == 0)
            {
                MessageBox.Show(this, "No se detectaron instancias (SQL Browser apagado o firewall). Escribe la instancia manualmente si la conoces.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudieron listar instancias remotas: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RefreshRemoteDatabasesAsync()
    {
        var host = _tbRemoteHost.Text.Trim();
        var user = _tbRemoteUser.Text.Trim();
        var pass = _tbRemotePassword.Text;
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user))
        {
            MessageBox.Show(this, "Completa IP, instancia (si aplica), usuario y contraseña antes de listar bases.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            var inst = _cbRemoteInstance.Text.Trim();
            if (inst == "(default)") inst = "";
            var cs = SqlDiscoveryService.BuildRemoteConnectionString(host, inst, "master", user, pass);
            var dbs = await SqlDiscoveryService.GetDatabasesAsync(cs, CancellationToken.None);
            var current = _cbRemoteDatabase.Text;
            _cbRemoteDatabase.Items.Clear();
            foreach (var db in dbs) _cbRemoteDatabase.Items.Add(db);
            if (!string.IsNullOrWhiteSpace(current) && !_cbRemoteDatabase.Items.Contains(current))
            {
                _cbRemoteDatabase.Items.Add(current);
            }
            if (!string.IsNullOrWhiteSpace(current)) _cbRemoteDatabase.Text = current;
            UpdateRemoteConnectionString();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudieron listar bases remotas: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string BuildMachinePrefix()
    {
        var type = _tbMachineType.Text.Trim();
        if (string.IsNullOrWhiteSpace(type))
        {
            return "";
        }

        if (type.Equals("VTech", StringComparison.OrdinalIgnoreCase))
        {
            return "VTech";
        }

        return type.Equals("VTi", StringComparison.OrdinalIgnoreCase) ? "VTi" : type;
    }

    private static TextBox CreateTextBox(int x, int y, int width)
    {
        return new TextBox
        {
            Location = new Point(x, y),
            Size = new Size(width, 36),
            BackColor = UiTheme.BgPanel,
            ForeColor = UiTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = UiTheme.BodyFont
        };
    }

    private static ComboBox CreateCombo(int x, int y, int width)
    {
        return new ComboBox
        {
            Location = new Point(x, y),
            Size = new Size(width, 38),
            BackColor = UiTheme.BgPanel,
            ForeColor = UiTheme.Text,
            FlatStyle = FlatStyle.Flat,
            Font = UiTheme.BodyFont,
            DropDownStyle = ComboBoxStyle.DropDown
        };
    }

    private static CheckBox CreateCheck(string text, int x, int y)
    {
        return new CheckBox
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(320, 30),
            ForeColor = UiTheme.TextDim,
            Font = UiTheme.SmallFont,
            BackColor = Color.Transparent
        };
    }

    private static FlatButton CreateButton(string text, int x, int y, int width,
        Color fill, Color hover, Color? border = null)
    {
        return new FlatButton
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, 44),
            FillColor = fill,
            FillColorHover = hover,
            BorderColor = border ?? Color.Transparent,
            Font = UiTheme.BodyFont
        };
    }

    private static Label CreateFieldLabel(string text, int x, int y)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = false,
            Size = new Size(440, 24),
            ForeColor = UiTheme.TextDim,
            Font = UiTheme.SmallFont,
            BackColor = Color.Transparent
        };
        return label;
    }

    private static DataGridView CreateScanGrid(int x, int y, int width, int height)
    {
        var grid = new DataGridView
        {
            Location = new Point(x, y),
            Size = new Size(width, height),
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            ReadOnly = false,
            BorderStyle = BorderStyle.FixedSingle,
            RowTemplate = { Height = 38 }
        };
        UiTheme.StyleGrid(grid);
        grid.ColumnHeadersHeight = 44;
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Usar", Width = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tabla", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Col", Width = 70, ReadOnly = true });
        return grid;
    }

    private static DataGridView CreatePairsGrid(int x, int y, int width, int height)
    {
        var grid = new DataGridView
        {
            Location = new Point(x, y),
            Size = new Size(width, height),
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BorderStyle = BorderStyle.FixedSingle,
            RowTemplate = { Height = 38 }
        };
        UiTheme.StyleGrid(grid);
        grid.ColumnHeadersHeight = 44;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Local", Width = 300 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Remota", Width = 300 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Clave", Width = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Estado", ReadOnly = true });
        return grid;
    }

    private async Task ScanLocalAsync() => await ScanAsync(_tbLocal.Text, isLocal: true);

    private async Task ScanRemoteAsync() => await ScanAsync(_tbRemote.Text, isLocal: false);

    private async Task ScanAsync(string cs, bool isLocal)
    {
        var btn = isLocal ? _btnScanLocal : _btnScanRemote;
        btn.Enabled = false;
        btn.Text = "Escaneando...";
        try
        {
            var tables = await DatabaseScanner.ScanTablesAsync(cs, CancellationToken.None);
            if (isLocal)
            {
                _local = tables;
                FillScanGrid(_dgvLocal, tables);
            }
            else
            {
                _remote = tables;
                FillScanGrid(_dgvRemote, tables);
                MarkExistingTables();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudo escanear: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btn.Enabled = true;
            btn.Text = isLocal ? "Escanear tablas local" : "Escanear tablas remoto";
        }
    }

    private void FillScanGrid(DataGridView grid, List<ScannedTable> tables)
    {
        grid.Rows.Clear();
        foreach (var t in tables)
        {
            var index = grid.Rows.Add(true, t.FullName, t.Columns.Count);
            grid.Rows[index].Tag = t;
        }
    }

    private IReadOnlyList<ScannedTable> SelectedLocalTables()
    {
        var result = new List<ScannedTable>();
        foreach (DataGridViewRow row in _dgvLocal.Rows)
        {
            var used = row.Cells[0].Value is bool b && b;
            if (used && row.Tag is ScannedTable t)
            {
                result.Add(t);
            }
        }
        return result;
    }

    private async Task AutoGenerateAsync()
    {
        var selected = SelectedLocalTables();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Marque al menos una tabla local en 'Usar'.", "Aviso",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_remote.Count == 0)
        {
            MessageBox.Show(this, "Escanee primero la base remota.", "Aviso",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _btnScanLocal.Enabled = false;
        _btnScanRemote.Enabled = false;
        try
        {
            var prefix = BuildMachinePrefix();
            foreach (var local in selected)
            {
                var remoteTable = _remote.FirstOrDefault(r =>
                    string.Equals(r.Name, local.Name, StringComparison.OrdinalIgnoreCase));

                var key = await DatabaseScanner.DetectKeyColumnAsync(
                    _tbLocal.Text, local.Schema, local.Name, CancellationToken.None);

                string status;
                string remoteFull;
                if (remoteTable is null)
                {
                    if (string.IsNullOrWhiteSpace(prefix))
                    {
                        remoteFull = local.FullName;
                        status = "Se creará en remoto";
                    }
                    else
                    {
                        var prefixedName = $"{prefix}_{local.Name}";
                        var existing = _remote.FirstOrDefault(r =>
                            string.Equals(r.Name, prefixedName, StringComparison.OrdinalIgnoreCase));
                        if (existing is not null)
                        {
                            remoteFull = existing.FullName;
                            status = "Listo (ya existe con prefijo)";
                        }
                        else
                        {
                            remoteFull = $"{local.Schema}.{prefixedName}";
                            status = $"Se creará en remoto como {prefix}_";
                        }
                    }
                }
                else
                {
                    remoteFull = remoteTable.FullName;
                    var comparison = ColumnComparison.Evaluate(
                        local.Columns,
                        remoteTable.Columns,
                        new HashSet<string> { _manager.CurrentOptions.OriginColumn });
                    status = comparison.Compatible
                        ? "Listo"
                        : $"Incompatible: {string.Join("; ", comparison.MissingOnRemote.Select(m => $"falta {m}"))}"
                          + (comparison.Incompatible.Count > 0 ? $"; {string.Join("; ", comparison.Incompatible)}" : "");
                }

                UpsertPair(local.FullName, remoteFull, key, status);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Error generando pares: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnScanLocal.Enabled = true;
            _btnScanRemote.Enabled = true;
        }
    }

    private void PreselectExisting()
    {
        if (_local.Count == 0)
        {
            MessageBox.Show(this, "Escanea primero las tablas locales.", "Aviso",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_remote.Count == 0)
        {
            MessageBox.Show(this, "Escanea primero las tablas remotas.", "Aviso",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var marked = MarkExistingTables();
        MessageBox.Show(this,
            $"Se marcaron {marked} tabla(s) que ya existen en remoto (o con prefijo).\r\n" +
            "Puedes revisar y pulsar 'Auto-generar pares'.",
            "Preselección", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private int MarkExistingTables()
    {
        if (_local.Count == 0 || _remote.Count == 0)
        {
            return 0;
        }

        var prefix = BuildMachinePrefix();
        var marked = 0;
        foreach (DataGridViewRow row in _dgvLocal.Rows)
        {
            if (row.Tag is not ScannedTable t)
            {
                continue;
            }

            var match = _remote.Any(r =>
                string.Equals(r.Name, t.Name, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(prefix) &&
                 string.Equals(r.Name, $"{prefix}_{t.Name}", StringComparison.OrdinalIgnoreCase)));

            row.Cells[0].Value = match;
            if (match)
            {
                marked++;
            }
        }

        return marked;
    }

    private void UpsertPair(string local, string remote, string key, string status)
    {
        foreach (DataGridViewRow row in _dgvPairs.Rows)
        {
            if (string.Equals(Convert.ToString(row.Cells["Local"].Value), local, StringComparison.OrdinalIgnoreCase))
            {
                row.Cells["Remota"].Value = remote;
                row.Cells["Clave"].Value = key;
                row.Cells["Estado"].Value = status;
                return;
            }
        }

        _dgvPairs.Rows.Add(local, remote, key, status);
    }

    private void RemovePair()
    {
        if (_dgvPairs.CurrentRow is not null)
        {
            _dgvPairs.Rows.Remove(_dgvPairs.CurrentRow);
        }
    }

    private void ExportConfig()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Exportar configuración",
            Filter = "Configuración SXA (*.json)|*.json",
            FileName = $"sxa-rtx-config-{Environment.MachineName}.json"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(_manager.CurrentOptions, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json);
            MessageBox.Show(this, $"Configuración exportada:\r\n{dlg.FileName}", "Exportar",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudo exportar: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ImportConfigAsync()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Importar configuración",
            Filter = "Configuración SXA (*.json)|*.json|Todos los archivos (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var options = JsonSerializer.Deserialize<SyncOptions>(File.ReadAllText(dlg.FileName));
            if (options is null || string.IsNullOrWhiteSpace(options.LocalConnectionString) ||
                string.IsNullOrWhiteSpace(options.RemoteConnectionString))
            {
                MessageBox.Show(this, "El archivo no contiene una configuración válida.", "Importar",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(this,
                $"Se importará la configuración de {options.Tables.Count} tabla(s) y se aplicará ahora.\r\n¿Continuar?",
                "Importar configuración", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            await _manager.ReconfigureAsync(options, CancellationToken.None);
            PopulateFromOptions(options);
            MessageBox.Show(this, "Configuración importada y aplicada.", "Importar",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudo importar: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PopulateFromOptions(SyncOptions options)
    {
        _tbMachineType.Text = options.MachineType;
        _tbMachineName.Text = options.MachineName;
        ParseLocalConnectionString(options.LocalConnectionString);
        ParseRemoteConnectionString(options.RemoteConnectionString);
        _tbLocal.Text = options.LocalConnectionString;
        _tbRemote.Text = options.RemoteConnectionString;
        _dgvPairs.Rows.Clear();
        foreach (var t in options.Tables)
        {
            _dgvPairs.Rows.Add(t.LocalTable, t.RemoteTable, t.KeyColumn, t.Enabled ? "Listo" : "Deshabilitada");
        }
        UpdateSavedConfigSummary();
    }

    private async Task SaveAsync()
    {
        try
        {
            var tables = new List<SyncTableConfig>();
            foreach (DataGridViewRow row in _dgvPairs.Rows)
            {
                var local = Convert.ToString(row.Cells["Local"].Value);
                var remote = Convert.ToString(row.Cells["Remota"].Value);
                var key = Convert.ToString(row.Cells["Clave"].Value);
                if (string.IsNullOrWhiteSpace(local) || string.IsNullOrWhiteSpace(remote) || string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                tables.Add(new SyncTableConfig
                {
                    LocalTable = local,
                    RemoteTable = remote,
                    KeyColumn = key,
                    Enabled = true,
                    AutoCreateRemote = true
                });
            }

            var opts = new SyncOptions
            {
                LocalConnectionString = _tbLocal.Text,
                RemoteConnectionString = _tbRemote.Text,
                OriginColumn = _manager.CurrentOptions.OriginColumn,
                PollIntervalSeconds = _manager.CurrentOptions.PollIntervalSeconds,
                BatchSize = _manager.CurrentOptions.BatchSize,
                ReclaimAfterMinutes = _manager.CurrentOptions.ReclaimAfterMinutes,
                MaxRetries = _manager.CurrentOptions.MaxRetries,
                SyncLogTable = _manager.CurrentOptions.SyncLogTable,
                HeartbeatTable = _manager.CurrentOptions.HeartbeatTable,
                DeviceCatalogTable = _manager.CurrentOptions.DeviceCatalogTable,
                DeviceConfigFile = _manager.CurrentOptions.DeviceConfigFile,
                MachineType = _tbMachineType.Text.Trim(),
                MachineName = _tbMachineName.Text.Trim(),
                AutoCheckUpdates = _manager.CurrentOptions.AutoCheckUpdates,
                AutoInstallUpdates = _manager.CurrentOptions.AutoInstallUpdates,
                UpdateCheckIntervalMinutes = _manager.CurrentOptions.UpdateCheckIntervalMinutes,
                UpdateRepo = _manager.CurrentOptions.UpdateRepo,
                Tables = tables
            };

            var localTest = await SqlDiscoveryService.TestConnectionAsync(opts.LocalConnectionString, CancellationToken.None);
            var remoteTest = await SqlDiscoveryService.TestConnectionAsync(opts.RemoteConnectionString, CancellationToken.None);
            if (!localTest.Ok || !remoteTest.Ok)
            {
                var detail = $"Local: {(localTest.Ok ? "OK" : localTest.Message)}\r\nRemota: {(remoteTest.Ok ? "OK" : remoteTest.Message)}";
                var authWarning = remoteTest.IsAuthError
                    ? "\r\n\r\nLa contraseña remota fue rechazada. Si guardas así, la sincronización se pausará automáticamente para no saturar el servidor central."
                    : "";
                var choice = MessageBox.Show(this,
                    $"No se pudo validar la conexión:\r\n\r\n{detail}{authWarning}\r\n\r\n¿Guardar de todos modos?",
                    "Validar conexión",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (choice != DialogResult.Yes)
                {
                    return;
                }
            }

            await _manager.ReconfigureAsync(opts, CancellationToken.None);
            MessageBox.Show(this,
                $"Configuración guardada. {tables.Count} tabla(s) en sincronización.",
                "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Error al guardar: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
