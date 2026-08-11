using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Reflection;

namespace SXA.RTX.Sync.Tray;

public sealed class HeaderPanel : Panel
{
    private readonly Image? _logo;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Subtitle { get; set; } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LogoSize { get; set; } = 84;

    public HeaderPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw
                 | ControlStyles.SupportsTransparentBackColor, true);
        Height = 96;
        _logo = TryLoadLogo();
    }

    private static Image? TryLoadLogo()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("logo.png", StringComparison.OrdinalIgnoreCase));
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

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var brush = new LinearGradientBrush(ClientRectangle,
            UiTheme.BgHeader, UiTheme.BgPanelAlt, LinearGradientMode.Horizontal);
        g.FillRectangle(brush, ClientRectangle);

        using var pen = new Pen(Color.FromArgb(70, UiTheme.Accent), 1f);
        g.DrawLine(pen, 0, Height - 1, Width, Height - 1);

        if (_logo is not null)
        {
            var size = Math.Min(LogoSize, Height - 16);
            var rect = new Rectangle(Width - size - 14, (Height - size) / 2, size, size);
            g.DrawImage(_logo, rect);
        }

        var sf = new StringFormat { FormatFlags = StringFormatFlags.NoWrap };
        var textWidth = _logo is null ? Width - 28 : Width - LogoSize - 50;

        var titleRect = new RectangleF(18, 14, textWidth, 34);
        using (var shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
        {
            g.DrawString(Title, UiTheme.TitleFont, shadow, titleRect.X + 1, titleRect.Y + 1, sf);
        }
        using (var titleBrush = new SolidBrush(UiTheme.Text))
        {
            g.DrawString(Title, UiTheme.TitleFont, titleBrush, titleRect, sf);
        }

        var subRect = new RectangleF(18, 48, textWidth, 22);
        using var subBrush = new SolidBrush(UiTheme.TextDim);
        g.DrawString(Subtitle, UiTheme.SubtitleFont, subBrush, subRect, sf);
    }
}
