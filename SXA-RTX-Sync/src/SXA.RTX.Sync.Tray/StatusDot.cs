using System.ComponentModel;

namespace SXA.RTX.Sync.Tray;

public sealed class StatusDot : Panel
{
    private Color _color = UiTheme.Success;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Color
    {
        get => _color;
        set { _color = value; Invalidate(); }
    }

    public StatusDot()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        Size = new Size(14, 14);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var rect = new Rectangle(1, 1, Width - 3, Height - 3);
        using var glow = new System.Drawing.Drawing2D.GraphicsPath();
        using (var fill = new SolidBrush(_color))
        {
            g.FillEllipse(fill, rect);
        }
        using (var border = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f))
        {
            g.DrawEllipse(border, rect);
        }
    }
}
