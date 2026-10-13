using System.Drawing.Drawing2D;

namespace SXA.RTX.Sync.Tray;

public sealed class FlatTabControl : TabControl
{
    public FlatTabControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw
                 | ControlStyles.UserPaint, true);
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(240, 46);
        Font = UiTheme.BodyFont;
        Padding = new Point(24, 8);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(UiTheme.Bg);

        // Fondo de la franja de pestañas
        var stripHeight = ItemSize.Height + 6;
        using (var strip = new SolidBrush(UiTheme.BgHeader))
        {
            g.FillRectangle(strip, 0, 0, Width, stripHeight);
        }
        using (var line = new Pen(UiTheme.Border))
        {
            g.DrawLine(line, 0, stripHeight - 1, Width, stripHeight - 1);
        }

        for (var i = 0; i < TabPages.Count; i++)
        {
            var rect = GetTabRect(i);
            var selected = SelectedIndex == i;
            var fill = selected ? UiTheme.BgPanelAlt : UiTheme.BgHeader;
            using (var brush = new SolidBrush(fill))
            {
                g.FillRectangle(brush, rect);
            }
            using (var border = new Pen(selected ? UiTheme.Accent : UiTheme.Border, selected ? 2f : 1f))
            {
                if (selected)
                {
                    g.DrawLine(border, rect.Left, rect.Bottom - 1, rect.Right, rect.Bottom - 1);
                    g.DrawLine(border, rect.Left, rect.Top, rect.Left, rect.Bottom);
                    g.DrawLine(border, rect.Right - 1, rect.Top, rect.Right - 1, rect.Bottom);
                }
                else
                {
                    g.DrawRectangle(border, rect);
                }
            }

            using var textBrush = new SolidBrush(selected ? UiTheme.Accent : UiTheme.TextDim);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(TabPages[i].Text, Font, textBrush, rect, sf);
        }
    }
}
