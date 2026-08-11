using System.Drawing.Drawing2D;

namespace SXA.RTX.Sync.Tray;

internal static class UiTheme
{
    public static readonly Color Bg = Color.FromArgb(24, 27, 41);
    public static readonly Color BgPanel = Color.FromArgb(32, 36, 54);
    public static readonly Color BgPanelAlt = Color.FromArgb(38, 43, 64);
    public static readonly Color BgHeader = Color.FromArgb(20, 22, 34);
    public static readonly Color Border = Color.FromArgb(58, 64, 92);
    public static readonly Color Accent = Color.FromArgb(240, 192, 96);
    public static readonly Color AccentHover = Color.FromArgb(255, 210, 120);
    public static readonly Color Primary = Color.FromArgb(72, 136, 184);
    public static readonly Color PrimaryHover = Color.FromArgb(92, 156, 204);
    public static readonly Color Success = Color.FromArgb(82, 180, 120);
    public static readonly Color Warning = Color.FromArgb(240, 192, 96);
    public static readonly Color Error = Color.FromArgb(226, 82, 82);
    public static readonly Color Text = Color.FromArgb(232, 234, 242);
    public static readonly Color TextDim = Color.FromArgb(158, 164, 186);
    public static readonly Color TextFaint = Color.FromArgb(110, 116, 142);

    public static readonly Font TitleFont = new("Segoe UI Semibold", 15f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font SubtitleFont = new("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font BodyFont = new("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font SmallFont = new("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font MonoFont = new("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Point);

    public static void Apply(Control control)
    {
        control.BackColor = Bg;
        control.ForeColor = Text;
        control.Font = BodyFont;
    }

    public static void StyleLabel(Label label, bool bold = false, bool dim = false)
    {
        label.AutoSize = false;
        label.BackColor = Color.Transparent;
        label.ForeColor = dim ? TextDim : Text;
        label.Font = bold ? new Font(BodyFont, FontStyle.Bold) : BodyFont;
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = BgPanel;
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = Border;
        grid.EnableHeadersVisualStyles = false;
        grid.RowHeadersVisible = false;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = BgPanelAlt,
            ForeColor = Accent,
            Font = new Font(BodyFont, FontStyle.Bold),
            SelectionBackColor = BgPanelAlt,
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 4, 0)
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = BgPanel,
            ForeColor = Text,
            SelectionBackColor = Color.FromArgb(52, 60, 92),
            SelectionForeColor = Text,
            Padding = new Padding(2, 0, 2, 0)
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(36, 40, 60),
            ForeColor = Text,
            SelectionBackColor = Color.FromArgb(52, 60, 92),
            SelectionForeColor = Text
        };
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
    }

    public static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            path.CloseFigure();
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, Rectangle bounds, int radius, Color color)
    {
        using var path = RoundedPath(bounds, radius);
        using var brush = new SolidBrush(color);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillPath(brush, path);
    }

    public static void DrawBorderRounded(Graphics g, Rectangle bounds, int radius, Color color, int width = 1)
    {
        using var path = RoundedPath(bounds, radius);
        using var pen = new Pen(color, width);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawPath(pen, path);
    }

    public static void FillGradient(Graphics g, Rectangle bounds, Color top, Color bottom)
    {
        using var brush = new LinearGradientBrush(bounds, top, bottom, LinearGradientMode.Vertical);
        g.FillRectangle(brush, bounds);
    }

    public static void DrawTextShadow(Graphics g, string text, Font font, Color shadow, Rectangle bounds, StringFormat sf)
    {
        var rect = new Rectangle(bounds.X + 1, bounds.Y + 1, bounds.Width, bounds.Height);
        using var brush = new SolidBrush(shadow);
        g.DrawString(text, font, brush, rect, sf);
    }
}
