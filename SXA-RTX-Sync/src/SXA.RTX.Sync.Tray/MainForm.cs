using System.Drawing.Drawing2D;

namespace SXA.RTX.Sync.Tray;

public sealed class MainForm : Form
{
    private readonly SyncManager _manager;
    private NotifyIcon _icon;
    private Label _lblStatus;
    private Button _btnPause;
    private Label _lblMachine;
    private StatusDot _dot;
    private ErrorBanner _errorBanner;
    private FlatButton _btnLog;
    private int _errorCount;
    private DateTime _lastErrorBalloon = DateTime.MinValue;

    public MainForm(SyncManager manager)
    {
        _manager = manager;
        Text = "SXA RTX Sync";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(470, 340);
        BackColor = UiTheme.Bg;
        ForeColor = UiTheme.Text;
        Font = UiTheme.BodyFont;
        Icon = IconLoader.AppIcon;

        BuildUi();
        CreateTray();

        Load += OnLoad;
        Shown += OnShown;
        FormClosing += OnFormClosing;
    }

    private void BuildUi()
    {
        var header = new HeaderPanel
        {
            Dock = DockStyle.Top,
            Title = "SXA RTX Sync",
            Subtitle = "Sincronización de pruebas RTX · VTi / VTech"
        };

        var statusCard = new Panel
        {
            Location = new Point(14, 108),
            Size = new Size(442, 112),
            BackColor = UiTheme.BgPanel
        };
        statusCard.Paint += (_, e) => UiTheme.DrawBorderRounded(e.Graphics,
            statusCard.ClientRectangle, 10, UiTheme.Border);

        _dot = new StatusDot { Location = new Point(18, 14), Color = UiTheme.Warning };
        var lblState = new Label
        {
            Location = new Point(38, 10),
            Size = new Size(200, 20),
            Text = "Iniciando...",
            ForeColor = UiTheme.Text,
            Font = new Font(UiTheme.BodyFont, FontStyle.Bold),
            BackColor = Color.Transparent
        };
        _lblStatus = new Label
        {
            Location = new Point(18, 36),
            Size = new Size(406, 26),
            ForeColor = UiTheme.TextDim,
            BackColor = Color.Transparent,
            AutoEllipsis = true
        };
        _lblMachine = new Label
        {
            Location = new Point(18, 64),
            Size = new Size(406, 40),
            ForeColor = UiTheme.TextFaint,
            BackColor = Color.Transparent,
            AutoEllipsis = true
        };

        _btnLog = new FlatButton
        {
            Text = "Errores (0)",
            Location = new Point(14, 232),
            Size = new Size(120, 30),
            FillColor = UiTheme.BgPanelAlt,
            FillColorHover = UiTheme.BgHeader,
            BorderColor = UiTheme.Border,
            Font = UiTheme.BodyFont
        };
        _errorBanner = new ErrorBanner
        {
            Location = new Point(14, 268),
            Size = new Size(442, 34)
        };

        var btnConfig = new FlatButton { Text = "Configuración...", Location = new Point(246, 232), Size = new Size(100, 30), Font = UiTheme.BodyFont };
        var btnStatus = new FlatButton { Text = "Estado", Location = new Point(352, 232), Size = new Size(104, 30), Font = UiTheme.BodyFont };
        _btnPause = new FlatButton { Text = "Pausar", Location = new Point(246, 302), Size = new Size(100, 30), FillColor = UiTheme.BgPanelAlt, FillColorHover = UiTheme.BgHeader, BorderColor = UiTheme.Border, Font = UiTheme.BodyFont };
        var btnExit = new FlatButton { Text = "Salir", Location = new Point(352, 302), Size = new Size(104, 30), FillColor = UiTheme.BgPanelAlt, FillColorHover = UiTheme.BgHeader, BorderColor = UiTheme.Border, Font = UiTheme.BodyFont };

        btnConfig.Click += (_, _) => ShowConfig();
        btnStatus.Click += (_, _) => ShowStatus();
        _btnPause.Click += (_, _) => TogglePause();
        btnExit.Click += (_, _) => ExitApp();
        _btnLog.Click += (_, _) => ShowErrors();
        _errorBanner.Click += (_, _) => ShowErrors();

        statusCard.Controls.Add(_dot);
        statusCard.Controls.Add(lblState);
        statusCard.Controls.Add(_lblStatus);
        statusCard.Controls.Add(_lblMachine);

        Controls.Add(header);
        Controls.Add(statusCard);
        Controls.Add(_btnLog);
        Controls.Add(_errorBanner);
        Controls.Add(btnConfig);
        Controls.Add(btnStatus);
        Controls.Add(_btnPause);
        Controls.Add(btnExit);
    }

    private void CreateTray()
    {
        var menu = new ContextMenuStrip { ForeColor = UiTheme.Text, BackColor = UiTheme.BgPanel };
        menu.Renderer = new ToolStripProfessionalRenderer(new TrayColorTable());
        menu.Items.Add("Abrir panel", null, (_, _) => ShowPanel());
        menu.Items.Add("Configuración...", null, (_, _) => ShowConfig());
        menu.Items.Add("Estado...", null, (_, _) => ShowStatus());
        menu.Items.Add("Errores...", null, (_, _) => ShowErrors());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Pausar / Reanudar", null, (_, _) => TogglePause());
        menu.Items.Add("Salir", null, (_, _) => ExitApp());

        _icon = new NotifyIcon
        {
            Icon = IconLoader.AppIcon,
            Text = "SXA RTX Sync",
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => ShowPanel();
    }

    private void OnLoad(object? sender, EventArgs e)
    {
        _lblStatus.Text = "Cargando configuración...";
        _lblMachine.Text = "";
    }

    private async void OnShown(object? sender, EventArgs e)
    {
        var ok = await _manager.InitializeAsync();
        _manager.Start();

        _manager.LogUpdated += msg => SafeInvoke(() =>
        {
            _lblStatus.Text = msg;
            _dot.Color = UiTheme.Success;
        });
        _manager.StateChanged += () => SafeInvoke(RefreshPauseButton);
        Diagnostics.ErrorRaised += record => SafeInvoke(() => OnError(record.Source, record.Message));

        _lblMachine.Text = $"{_manager.Identity.DeviceId}  ·  {TrayMachineLabel()}";

        Hide();
        _icon.BalloonTipTitle = "SXA RTX Sync";
        _icon.BalloonTipText = ok
            ? $"En ejecución. Dispositivo {_manager.Identity.DeviceId}."
            : "No se pudo determinar el dispositivo. Abra la configuración.";
        _icon.ShowBalloonTip(3000);

        if (!ok || !TrayComposition.IsConfigValid(_manager.CurrentOptions))
        {
            ShowConfig();
        }
    }

    private string TrayMachineLabel()
    {
        var opts = _manager.CurrentOptions;
        var name = string.IsNullOrWhiteSpace(opts.MachineName) ? _manager.Identity.MachineName : opts.MachineName;
        var type = string.IsNullOrWhiteSpace(opts.MachineType) ? "—" : opts.MachineType;
        return $"Máquina {name} · Tipo {type}";
    }

    private void OnError(string source, string message)
    {
        _errorCount++;
        _errorBanner.SetError($"{source}: {message}");
        _errorBanner.Visible = true;
        _btnLog.Text = $"Errores ({_errorCount})";
        _dot.Color = UiTheme.Error;

        if (_errorCount == 1 && !_manager.IsPaused)
        {
            _icon.Icon = IconLoader.CreateStatusIcon(error: true);
        }

        var now = DateTime.Now;
        if ((now - _lastErrorBalloon).TotalSeconds > 30)
        {
            _lastErrorBalloon = now;
            _icon.BalloonTipTitle = $"Error en {source}";
            _icon.BalloonTipText = message + " — La sincronización continúa. Abra Errores para ver el detalle.";
            _icon.ShowBalloonTip(5000);
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private bool _allowClose;

    private async void ExitApp()
    {
        var confirm = MessageBox.Show(
            this,
            "¿Desea salir realmente de SXA RTX Sync?\r\n\r\nLa sincronización se detendrá y la máquina dejará de enviar datos al servidor.",
            "Confirmar salida",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        _allowClose = true;
        _icon.Visible = false;
        await _manager.StopAsync();
        Close();
    }

    private void TogglePause()
    {
        if (_manager.IsPaused)
        {
            _manager.Resume();
        }
        else
        {
            _manager.Pause();
        }
        RefreshPauseButton();
    }

    private void RefreshPauseButton()
    {
        if (_btnPause is not null)
        {
            _btnPause.Text = _manager.IsPaused ? "Reanudar" : "Pausar";
        }
        if (_dot is not null && !_dot.IsDisposed)
        {
            _dot.Color = _manager.IsPaused ? UiTheme.Warning : _errorCount > 0 ? UiTheme.Error : UiTheme.Success;
        }
    }

    private void ShowPanel()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    public void ShowConfig()
    {
        using var form = new ConfigForm(_manager);
        form.ShowDialog(this);
    }

    public void ShowStatus()
    {
        using var form = new StatusForm(_manager);
        form.ShowDialog(this);
    }

    public void ShowErrors()
    {
        using var form = new ErrorLogForm();
        form.ShowDialog(this);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _icon.Dispose();
        base.OnFormClosed(e);
    }

    private void SafeInvoke(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }
        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }
}

internal sealed class TrayColorTable : ProfessionalColorTable
{
    public override Color MenuItemSelected => UiTheme.BgPanelAlt;
    public override Color MenuItemBorder => UiTheme.Border;
    public override Color ToolStripDropDownBackground => UiTheme.BgPanel;
    public override Color MenuBorder => UiTheme.Border;
    public override Color MenuItemSelectedGradientBegin => UiTheme.BgPanelAlt;
    public override Color MenuItemSelectedGradientEnd => UiTheme.BgPanelAlt;
    public override Color ImageMarginGradientBegin => UiTheme.BgPanel;
    public override Color ImageMarginGradientMiddle => UiTheme.BgPanel;
    public override Color ImageMarginGradientEnd => UiTheme.BgPanel;
    public override Color SeparatorDark => UiTheme.Border;
    public override Color SeparatorLight => UiTheme.Border;
}

internal sealed class ErrorBanner : Panel
{
    private string _message = "";

    public ErrorBanner()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Visible = false;
    }

    public void SetError(string message)
    {
        _message = message;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        UiTheme.FillRounded(g, rect, 9, Color.FromArgb(60, 226, 82, 82));
        using (var gradientFill = new SolidBrush(Color.FromArgb(28, 255, 255, 255)))
        {
            g.FillRectangle(gradientFill, new Rectangle(3, 3, Width - 6, Height - 6));
        }
        UiTheme.DrawBorderRounded(g, rect, 9, Color.FromArgb(150, 255, 255, 255));

        var iconRect = new Rectangle(10, (Height - 16) / 2, 16, 16);
        using (var warnBrush = new SolidBrush(Color.FromArgb(255, 236, 236)))
        {
            g.FillEllipse(warnBrush, iconRect);
        }
        using (var warnText = new SolidBrush(Color.FromArgb(226, 82, 82)))
        {
            var warnFont = new Font(UiTheme.BodyFont, FontStyle.Bold);
            g.DrawString("!", warnFont, warnText,
                new Rectangle(iconRect.X, iconRect.Y - 1, iconRect.Width, iconRect.Height),
                new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        }

        var textRect = new Rectangle(34, 0, Width - 44, Height);
        using var textBrush = new SolidBrush(Color.FromArgb(255, 240, 240));
        g.DrawString(_message, UiTheme.BodyFont, textBrush, textRect,
            new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter });
    }
}
