using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SXA.RTX.Sync.Tray;

public sealed class FlatButton : Button
{
    private bool _hovered;
    private bool _pressed;
    private Color _fill = UiTheme.Primary;
    private Color _fillHover = UiTheme.PrimaryHover;
    private Color _border = Color.Transparent;

    [DefaultValue(typeof(Color), "72, 136, 184")]
    public Color FillColor
    {
        get => _fill;
        set { _fill = value; Invalidate(); }
    }

    [DefaultValue(typeof(Color), "92, 156, 204")]
    public Color FillColorHover
    {
        get => _fillHover;
        set { _fillHover = value; Invalidate(); }
    }

    [DefaultValue(typeof(Color), "0, 0, 0, 0")]
    public Color BorderColor
    {
        get => _border;
        set { _border = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 8;

    public FlatButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = UiTheme.Text;
        Font = new Font(UiTheme.BodyFont, FontStyle.Bold);
        Cursor = Cursors.Hand;
        Height = 36;
        TextAlign = ContentAlignment.MiddleCenter;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        var color = Enabled ? (_pressed ? ControlPaint.Dark(_fillHover, 0.15f)
            : _hovered ? _fillHover : _fill) : UiTheme.BgPanelAlt;

        UiTheme.FillRounded(g, rect, CornerRadius, color);
        if (_border != Color.Transparent)
        {
            UiTheme.DrawBorderRounded(g, rect, CornerRadius, _border);
        }

        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        using var brush = new SolidBrush(Enabled ? ForeColor : UiTheme.TextFaint);
        g.DrawString(Text, Font, brush, rect, sf);
    }
}
