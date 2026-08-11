using System.Reflection;
using System.Drawing.Drawing2D;

namespace SXA.RTX.Sync.Tray;

public sealed class SplashScreen : Form
{
    private readonly Image? _logo;
    private readonly System.Windows.Forms.Timer _timer;
    private DateTime _started;
    private bool _closing;

    public SplashScreen()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        Size = new Size(460, 460);
        DoubleBuffered = true;

        _logo = TryLoadLogo();
        _timer = new System.Windows.Forms.Timer { Interval = 16 };
        _timer.Tick += OnTick;
    }

    private static Image? TryLoadLogo()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var name = assembly.GetManifestResourceNames().FirstOrDefault(n =>
                n.EndsWith("logo.png", StringComparison.OrdinalIgnoreCase));
            if (name is null)
            {
                return null;
            }

            using var stream = assembly.GetManifestResourceStream(name);
            return stream is null ? null : Image.FromStream(stream);
        }
        catch
        {
            return null;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _started = DateTime.UtcNow;
        Opacity = 0;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var elapsed = (DateTime.UtcNow - _started).TotalMilliseconds;

        if (elapsed < 450)
        {
            Opacity = Math.Min(1, elapsed / 450);
        }
        else if (elapsed >= 2600)
        {
            _timer.Stop();
            if (_closing)
            {
                return;
            }

            _closing = true;
            var fade = new System.Windows.Forms.Timer { Interval = 16 };
            fade.Tick += (_, _) =>
            {
                Opacity = Math.Max(0, Opacity - 0.06);
                if (Opacity <= 0)
                {
                    fade.Stop();
                    fade.Dispose();
                    Close();
                }
            };
            fade.Start();
        }
    }

    protected override void OnClick(EventArgs e)
    {
        _started = DateTime.UtcNow.AddMilliseconds(-2600);
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
        {
            _started = DateTime.UtcNow.AddMilliseconds(-2600);
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_logo is not null)
        {
            var side = Math.Min(Width, Height) - 70;
            var rect = new Rectangle((Width - side) / 2, 34, side, side);
            g.DrawImage(_logo, rect);
        }

        var pill = new Rectangle(0, Height - 62, Width, 52);
        var text = $"SXA RTX Sync   ·   v{VersionLabel()}";

        using (var pillPath = UiTheme.RoundedPath(new Rectangle(0, Height - 66, Width, 56), 14))
        using (var pillBrush = new SolidBrush(Color.FromArgb(215, 24, 27, 41)))
        {
            g.FillPath(pillBrush, pillPath);
        }

        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var textBrush = new SolidBrush(Color.FromArgb(240, 192, 96));
        g.DrawString(text, new Font(UiTheme.BodyFont, FontStyle.Bold), textBrush, pill, sf);
    }

    private static string VersionLabel()
    {
        try
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        }
        catch
        {
            return "0.0.0";
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        _logo?.Dispose();
        base.OnFormClosed(e);
    }
}
