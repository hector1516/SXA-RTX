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

        var logoLeft = 20;
        var size = Math.Min(LogoSize, Height - 16);
        var textLeft = 28;

        if (_logo is not null)
        {
            var rect = new Rectangle(logoLeft, (Height - size) / 2, size, size);
            // Halo sutil para dar protagonismo al logo
            using (var glow = new SolidBrush(Color.FromArgb(26, UiTheme.Accent)))
            {
                g.FillEllipse(glow, rect.X - 8, rect.Y - 8, rect.Width + 16, rect.Height + 16);
            }
            g.DrawImage(_logo, rect);
            textLeft = rect.Right + 22;
        }

        var sf = new StringFormat { FormatFlags = StringFormatFlags.NoWrap };
        var textWidth = Width - textLeft - 24;

        var titleHeight = 40f;
        var subHeight = 26f;
        var totalText = titleHeight + subHeight + 6;
        var top = (Height - totalText) / 2f;

        var titleRect = new RectangleF(textLeft, top, textWidth, titleHeight);
        using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
        {
            g.DrawString(Title, UiTheme.TitleFont, shadow, titleRect.X + 2, titleRect.Y + 2, sf);
        }
        using (var titleBrush = new SolidBrush(UiTheme.Text))
        {
            g.DrawString(Title, UiTheme.TitleFont, titleBrush, titleRect, sf);
        }

        var subRect = new RectangleF(textLeft, top + titleHeight + 2, textWidth, subHeight);
        using var subBrush = new SolidBrush(UiTheme.TextDim);
        g.DrawString(Subtitle, UiTheme.SubtitleFont, subBrush, subRect, sf);
    }
}
