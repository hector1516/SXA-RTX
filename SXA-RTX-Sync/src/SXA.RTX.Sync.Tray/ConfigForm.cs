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
        ClientSize = new Size(920, 736);
        MinimumSize = new Size(860, 680);
        UiTheme.Apply(this);
        Icon = IconLoader.AppIcon;

        var header = new HeaderPanel
        {
            Dock = DockStyle.Top,
            Title = "Configuración",
            Subtitle = "Conexiones, tablas a sincronizar e identificación de la máquina.",
            LogoSize = 56
        };

        _tbLocal = CreateTextBox(24, 130, 428);
        _tbRemote = CreateTextBox(492, 130, 404);

        _btnScanLocal = CreateButton("Escanear local", 24, 162, 130, UiTheme.Primary, UiTheme.PrimaryHover);
        _btnScanRemote = CreateButton("Escanear remoto", 492, 162, 130, UiTheme.Primary, UiTheme.PrimaryHover);

        _tbMachineType = CreateTextBox(24, 368, 190);
        _tbMachineName = CreateTextBox(280, 368, 220);

        var btnPairs = CreateButton("Auto-generar pares", 160, 402, 160, UiTheme.Accent, UiTheme.AccentHover);
        var btnRemove = CreateButton("Quitar par", 334, 402, 120, UiTheme.BgPanelAlt, UiTheme.BgHeader, UiTheme.Border);
        var btnSave = CreateButton("Guardar y aplicar", 24, 696, 150, UiTheme.Primary, UiTheme.PrimaryHover);
        var btnCancel = CreateButton("Cancelar", 186, 696, 110, UiTheme.BgPanelAlt, UiTheme.BgHeader, UiTheme.Border);

        _lblHint = new Label
        {
            Location = new Point(24, 432),
            Size = new Size(872, 18),
            Text = "Paso 1: escanee local y remoto. Paso 2: genere los pares. Paso 3: revise la clave y guarde.\r\nLas tablas que no existan en remoto se crearán con el prefijo del tipo de máquina (VTi_ o VTech_).",
            ForeColor = UiTheme.TextFaint,
            Font = UiTheme.SmallFont,
            BackColor = Color.Transparent
        };

        _dgvLocal = CreateScanGrid(24, 194);
        _dgvRemote = CreateScanGrid(492, 194);
        _dgvPairs = CreatePairsGrid(24, 450);

        Controls.Add(header);
        Controls.Add(_tbLocal);
        Controls.Add(_tbRemote);
        Controls.Add(CreateFieldLabel("Local (SQL Express)", 24, 110));
        Controls.Add(CreateFieldLabel("Remota (SQL Server)", 492, 110));
        Controls.Add(CreateFieldLabel("Tipo de máquina (VTi / VTech)", 24, 348));
        Controls.Add(CreateFieldLabel("Nombre del PC", 280, 348));
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

        _btnScanLocal.Click += async (_, _) => await ScanLocalAsync();
        _btnScanRemote.Click += async (_, _) => await ScanRemoteAsync();
        btnPairs.Click += async (_, _) => await AutoGenerateAsync();
        btnRemove.Click += (_, _) => RemovePair();
        btnSave.Click += async (_, _) => await SaveAsync();
        btnCancel.Click += (_, _) => Close();
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
            Size = new Size(404, 140),
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
            Size = new Size(872, 230),
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
            btn.Text = isLocal ? "Escanear local" : "Escanear remoto";
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
