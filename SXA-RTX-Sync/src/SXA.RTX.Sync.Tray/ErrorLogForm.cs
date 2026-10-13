using System.Diagnostics;

namespace SXA.RTX.Sync.Tray;

public sealed class ErrorLogForm : Form
{
    private readonly DataGridView _dgv;
    private readonly TextBox _tbDetail;
    private readonly Label _lblDetailTitle;
    private ErrorRecord? _selected;

    public ErrorLogForm()
    {
        Text = "Registro de errores - SXA RTX Sync";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        WindowState = FormWindowState.Maximized;
        ClientSize = new Size(1000, 720);
        MinimumSize = new Size(880, 600);
        UiTheme.Apply(this);
        Icon = IconLoader.AppIcon;

        var header = new HeaderPanel
        {
            Dock = DockStyle.Top,
            Height = 136,
            Title = "Registro de errores",
            Subtitle = "Los errores no detienen la sincronización. Esto es solo para diagnóstico.",
            LogoSize = 116
        };

        var content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.Bg };

        _dgv = new DataGridView
        {
            Location = new Point(20, 24),
            Size = new Size(940, 300),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowTemplate = { Height = 38 }
        };
        UiTheme.StyleGrid(_dgv);
        _dgv.ColumnHeadersHeight = 44;
        _dgv.Columns.Add("Time", "Hora");
        _dgv.Columns.Add("Source", "Origen");
        _dgv.Columns.Add("Message", "Mensaje");

        _lblDetailTitle = new Label
        {
            Location = new Point(20, 340),
            Size = new Size(940, 26),
            Text = "Detalle",
            Font = new Font(UiTheme.BodyFont, FontStyle.Bold),
            ForeColor = UiTheme.Accent
        };

        _tbDetail = new TextBox
        {
            Location = new Point(20, 372),
            Size = new Size(940, 240),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            BackColor = UiTheme.BgPanel,
            ForeColor = UiTheme.Text,
            Font = UiTheme.MonoFont,
            BorderStyle = BorderStyle.FixedSingle
        };

        var btnCopy = new FlatButton
        {
            Text = "Copiar seleccionado",
            Location = new Point(20, 630),
            Size = new Size(240, 46),
            FillColor = UiTheme.Primary,
            FillColorHover = UiTheme.PrimaryHover,
            Font = UiTheme.BodyFont
        };
        var btnOpenLog = new FlatButton
        {
            Text = "Abrir archivo de log",
            Location = new Point(274, 630),
            Size = new Size(240, 46),
            FillColor = UiTheme.BgPanelAlt,
            FillColorHover = UiTheme.BgHeader,
            BorderColor = UiTheme.Border,
            Font = UiTheme.BodyFont
        };
        var btnClose = new FlatButton
        {
            Text = "Cerrar",
            Location = new Point(840, 630),
            Size = new Size(120, 46),
            FillColor = UiTheme.BgPanelAlt,
            FillColorHover = UiTheme.BgHeader,
            BorderColor = UiTheme.Border,
            Font = UiTheme.BodyFont
        };

        content.Controls.Add(_dgv);
        content.Controls.Add(_lblDetailTitle);
        content.Controls.Add(_tbDetail);
        content.Controls.Add(btnCopy);
        content.Controls.Add(btnOpenLog);
        content.Controls.Add(btnClose);

        Controls.Add(content);
        Controls.Add(header);

        btnCopy.Click += (_, _) => CopySelected();
        btnOpenLog.Click += (_, _) => OpenLogFile();
        btnClose.Click += (_, _) => Close();
        _dgv.SelectionChanged += (_, _) => ShowDetail();

        Load += (_, _) => Reload();
    }

    private void Reload()
    {
        _dgv.Rows.Clear();
        var errors = Diagnostics.RecentErrors;
        foreach (var err in errors)
        {
            var index = _dgv.Rows.Add(
                err.Time.ToString("yyyy-MM-dd HH:mm:ss"),
                err.Source,
                err.Message.ReplaceLineEndings(" "));
            _dgv.Rows[index].Tag = err;
        }

        if (_dgv.Rows.Count > 0)
        {
            _dgv.Rows[^1].Selected = true;
        }

        ShowDetail();
    }

    private void ShowDetail()
    {
        if (_dgv.CurrentRow?.Tag is ErrorRecord err)
        {
            _selected = err;
            _tbDetail.Text = $"[{err.Time:yyyy-MM-dd HH:mm:ss}] ({err.Source})\r\n{err.Message}\r\n\r\n{err.Stack ?? "(sin detalle)"}";
        }
        else
        {
            _tbDetail.Text = "";
        }
    }

    private void CopySelected()
    {
        if (_selected is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(_tbDetail.Text);
        }
        catch
        {
            // Sin clipboard disponible.
        }
    }

    private void OpenLogFile()
    {
        try
        {
            if (File.Exists(Diagnostics.LogPath))
            {
                Process.Start(new ProcessStartInfo(Diagnostics.LogPath) { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show(this, "Todavía no hay archivo de log.", "SXA RTX Sync",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudo abrir el log: {ex.Message}", "SXA RTX Sync",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
