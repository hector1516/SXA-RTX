using System.Reflection;

namespace SXA.RTX.Sync.Tray;

internal static class IconLoader
{
    public static Icon AppIcon { get; } = LoadAppIcon();

    private static Icon LoadAppIcon()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var name = assembly.GetManifestResourceNames().FirstOrDefault(n =>
                n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
            if (name is null)
            {
                return SystemIcons.Application;
            }

            using var stream = assembly.GetManifestResourceStream(name);
            return stream is null ? SystemIcons.Application : new Icon(stream);
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    public static Icon CreateStatusIcon(bool error)
    {
        try
        {
            var bmp = new Bitmap(64, 64);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.DrawIcon(AppIcon, new Rectangle(0, 0, 64, 64));
                if (error)
                {
                    using var brush = new SolidBrush(Color.FromArgb(235, 64, 64));
                    g.FillEllipse(brush, 42, 42, 20, 20);
                    using var border = new Pen(Color.White, 3f);
                    g.DrawEllipse(border, 42, 42, 20, 20);
                }
            }

            return Icon.FromHandle(bmp.GetHicon());
        }
        catch
        {
            return AppIcon;
        }
    }
}
