using System.Data;
using Microsoft.Data.SqlClient;
using SXA.RTX.Sync.Core.Configuration;
using SXA.RTX.Sync.Core.Sync;

namespace SXA.RTX.Sync.Tray;

public sealed class ConfigForm : Form
{
    private readonly SyncManager _manager;
    private readonly TextBox _tbLocal;
    private readonly TextBox _tbRemote;
    private readonly TextBox _tbMachineType;
    private readonly TextBox _tbMachineName;
    private readonly ComboBox _cbLocalInstance;
    private readonly ComboBox _cbLocalDatabase;
    private readonly TextBox _tbRemoteHost;
    private readonly ComboBox _cbRemoteInstance;
    private readonly TextBox _tbRemoteUser;
    private readonly TextBox _tbRemotePassword;
    private readonly ComboBox _cbRemoteDatabase;
    private readonly Button _btnScanLocal;
    private readonly Button _btnScanRemote;
    private readonly DataGridView _dgvLocal;
    private readonly DataGridView _dgvRemote;
    private readonly DataGridView _dgvPairs;
    private readonly Label _lblHint;
    private List<ScannedTable> _local = new();
    private List<ScannedTable> _remote = new();

    public ConfigForm(SyncManager manager)
    {
        _manager = manager;
        Text = "Configuración de sincronización";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(920, 860);
        MinimumSize = new Size(920, 760);
        UiTheme.Apply(this);
        Icon = IconLoader.AppIcon;

        var header = new HeaderPanel
        {
            Dock = DockStyle.Top,
            Title = "Configuración",
            Subtitle = "Conexiones, tablas a sincronizar e identificación de la máquina.",
            LogoSize = 56
        };

        // Local - Instancia
        _cbLocalInstance = CreateCombo(24, 128, 280);
        var btnRefreshLocalInst = CreateButton("Actualizar", 310, 128, 80, UiTheme.BgPanelAlt, UiTheme.BgHeader, UiTheme.Border);
        // Local - Base de datos
        _cbLocalDatabase = CreateCombo(24, 178, 280);
        var btnRefreshLocalDb = CreateButton("Actualizar", 310, 178, 80, UiTheme.BgPanelAlt, UiTheme.BgHeader, UiTheme.Border);
        // Local - Cadena (avanzado, solo lectura)
        _tbLocal = CreateTextBox(24, 224, 428);
        _tbLocal.ReadOnly = true;
        _tbLocal.BackColor = Color.FromArgb(28, 32, 48);
        var lblLocalEdit = new CheckBox
        {
            Text = "Editar cadena",
            Location = new Point(24, 248),
            Size = new Size(120, 18),
            ForeColor = UiTheme.TextFaint,
            Font = UiTheme.SmallFont,
            BackColor = Color.Transparent
        };

        // Remota - Host/IP
        _tbRemoteHost = CreateTextBox(492, 128, 200);
        var btnScanRemoteInst = CreateButton("Escanear instancias", 700, 128, 140, UiTheme.Primary, UiTheme.PrimaryHover);
        // Remota - Instancia
        _cbRemoteInstance = CreateCombo(492, 178, 280);
        _cbRemoteInstance.DropDownStyle = ComboBoxStyle.DropDown;
        // Remota - Usuario/Contraseña
        _tbRemoteUser = CreateTextBox(492, 228, 150);
        _tbRemotePassword = CreateTextBox(650, 228, 140);
        _tbRemotePassword.UseSystemPasswordChar = true;
        // Remota - Base de datos
        _cbRemoteDatabase = CreateCombo(492, 278, 280);
        var btnRefreshRemoteDb = CreateButton("Actualizar", 780, 278, 80, UiTheme.BgPanelAlt, UiTheme.BgHeader, UiTheme.Border);
        // Remota - Cadena
        _tbRemote = CreateTextBox(492, 326, 404);
        _tbRemote.ReadOnly = true;
        _tbRemote.BackColor = Color.FromArgb(28, 32, 48);
        var lblRemoteEdit = new CheckBox
        {
            Text = "Editar cadena",
            Location = new Point(492, 350),
            Size = new Size(120, 18),
            ForeColor = UiTheme.TextFaint,
            Font = UiTheme.SmallFont,
            BackColor = Color.Transparent
        };

        _tbMachineType = CreateTextBox(24, 410, 190);
        _tbMachineName = CreateTextBox(280, 410, 220);

        _btnScanLocal = CreateButton("Escanear tablas local", 24, 268, 150, UiTheme.Primary, UiTheme.PrimaryHover);
        _btnScanRemote = CreateButton("Escanear tablas remoto", 492, 368, 150, UiTheme.Primary, UiTheme.PrimaryHover);
        var btnPairs = CreateButton("Auto-generar pares", 160, 472, 160, UiTheme.Accent, UiTheme.AccentHover);
        var btnRemove = CreateButton("Quitar par", 334, 472, 120, UiTheme.BgPanelAlt, UiTheme.BgHeader, UiTheme.Border);
        var btnSave = CreateButton("Guardar y aplicar", 24, 820, 150, UiTheme.Primary, UiTheme.PrimaryHover);
        var btnCancel = CreateButton("Cancelar", 186, 820, 110, UiTheme.BgPanelAlt, UiTheme.BgHeader, UiTheme.Border);

        _lblHint = new Label
        {
            Location = new Point(24, 502),
            Size = new Size(872, 32),
            Text = "Paso 1: configura instancias y bases arriba. Paso 2: escanea tablas y genera los pares. Las tablas que no existan en remoto se crearán con el prefijo del tipo de máquina (VTi_ o VTech_).",
            ForeColor = UiTheme.TextFaint,
            Font = UiTheme.SmallFont,
            BackColor = Color.Transparent
        };

        _dgvLocal = CreateScanGrid(24, 300);
        _dgvRemote = CreateScanGrid(492, 398);
        _dgvPairs = CreatePairsGrid(24, 540);

        Controls.Add(header);
        // Local
        Controls.Add(CreateFieldLabel("Instancia local (Windows Auth)", 24, 108));
        Controls.Add(_cbLocalInstance);
        Controls.Add(btnRefreshLocalInst);
        Controls.Add(CreateFieldLabel("Base de datos local", 24, 158));
        Controls.Add(_cbLocalDatabase);
        Controls.Add(btnRefreshLocalDb);
        Controls.Add(CreateFieldLabel("Cadena local", 24, 206));
        Controls.Add(_tbLocal);
        Controls.Add(lblLocalEdit);
        // Remote
        Controls.Add(CreateFieldLabel("Servidor remoto (IP / Host)", 492, 108));
        Controls.Add(_tbRemoteHost);
        Controls.Add(btnScanRemoteInst);
        Controls.Add(CreateFieldLabel("Instancia remota", 492, 158));
        Controls.Add(_cbRemoteInstance);
        Controls.Add(CreateFieldLabel("Usuario", 492, 208));
        Controls.Add(CreateFieldLabel("Contraseña", 650, 208));
        Controls.Add(_tbRemoteUser);
        Controls.Add(_tbRemotePassword);
        Controls.Add(CreateFieldLabel("Base de datos remota", 492, 258));
        Controls.Add(_cbRemoteDatabase);
        Controls.Add(btnRefreshRemoteDb);
        Controls.Add(CreateFieldLabel("Cadena remota", 492, 306));
        Controls.Add(_tbRemote);
        Controls.Add(lblRemoteEdit);
        // Common
        Controls.Add(CreateFieldLabel("Tipo de máquina (VTi / VTech)", 24, 390));
        Controls.Add(CreateFieldLabel("Nombre del PC", 280, 390));
        Controls.Add(_btnScanLocal);
        Controls.Add(_btnScanRemote);
        Controls.Add(_tbMachineType);
        Controls.Add(_tbMachineName);
        Controls.Add(btnPairs);
        Controls.Add(btnRemove);
        Controls.Add(btnSave);
        Controls.Add(btnCancel);
        Controls.Add(_lblHint);
        Controls.Add(_dgvLocal);
        Controls.Add(_dgvRemote);
        Controls.Add(_dgvPairs);

        // Eventos
        lblLocalEdit.CheckedChanged += (_, _) => { _tbLocal.ReadOnly = !lblLocalEdit.Checked; _tbLocal.BackColor = lblLocalEdit.Checked ? UiTheme.BgPanel : Color.FromArgb(28, 32, 48); };
        lblRemoteEdit.CheckedChanged += (_, _) => { _tbRemote.ReadOnly = !lblRemoteEdit.Checked; _tbRemote.BackColor = lblRemoteEdit.Checked ? UiTheme.BgPanel : Color.FromArgb(28, 32, 48); };

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
        btnPairs.Click += async (_, _) => await AutoGenerateAsync();
        btnRemove.Click += (_, _) => RemovePair();
        btnSave.Click += async (_, _) => await SaveAsync();
        btnCancel.Click += (_, _) => Close();

        Load += async (_, _) => await OnLoadAsync();
    }

    private async Task OnLoadAsync()
    {
        // Parsear cadenas actuales para precargar combos
        ParseLocalConnectionString(_manager.CurrentOptions.LocalConnectionString);
        ParseRemoteConnectionString(_manager.CurrentOptions.RemoteConnectionString);
        _tbMachineType.Text = _manager.CurrentOptions.MachineType;
        _tbMachineName.Text = _manager.CurrentOptions.MachineName;
        _tbLocal.Text = _manager.CurrentOptions.LocalConnectionString;
        _tbRemote.Text = _manager.CurrentOptions.RemoteConnectionString;

        await RefreshLocalInstancesAsync();
        // Intentar precargar BD locales si hay instancia
        if (!string.IsNullOrWhiteSpace(_cbLocalInstance.Text))
        {
            await RefreshLocalDatabasesAsync();
        }
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
            Size = new Size(width, 24),
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
            Size = new Size(width, 24),
            BackColor = UiTheme.BgPanel,
            ForeColor = UiTheme.Text,
            FlatStyle = FlatStyle.Flat,
            Font = UiTheme.BodyFont,
            DropDownStyle = ComboBoxStyle.DropDown
        };
    }

    private static FlatButton CreateButton(string text, int x, int y, int width,
        Color fill, Color hover, Color? border = null)
    {
        return new FlatButton
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, 30),
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
            Size = new Size(300, 18),
            ForeColor = UiTheme.TextDim,
            Font = UiTheme.SmallFont,
            BackColor = Color.Transparent
        };
        return label;
    }

    private static DataGridView CreateScanGrid(int x, int y)
    {
        var grid = new DataGridView
        {
            Location = new Point(x, y),
            Size = new Size(404, 120),
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            ReadOnly = false,
            BorderStyle = BorderStyle.FixedSingle
        };
        UiTheme.StyleGrid(grid);
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Usar", Width = 42 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tabla", Width = 230, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Col", Width = 50, ReadOnly = true });
        return grid;
    }

    private static DataGridView CreatePairsGrid(int x, int y)
    {
        var grid = new DataGridView
        {
            Location = new Point(x, y),
            Size = new Size(872, 200),
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BorderStyle = BorderStyle.FixedSingle
        };
        UiTheme.StyleGrid(grid);
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
