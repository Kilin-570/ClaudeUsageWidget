using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ClaudeUsageWidget;

/// <summary>Draws the session percentage into a tray icon at runtime.</summary>
public static class TrayIconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon Render(double? utilization)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var color = utilization switch
        {
            null => Color.FromArgb(0x8A, 0x8A, 0x96),
            >= 90 => Color.FromArgb(0xF4, 0x51, 0x4E),
            >= 70 => Color.FromArgb(0xF5, 0xA9, 0x3B),
            _ => Color.FromArgb(0x4C, 0x9F, 0xF0),
        };

        // ring showing utilization
        using (var track = new Pen(Color.FromArgb(70, 255, 255, 255), 4f))
            g.DrawEllipse(track, 2, 2, size - 5, size - 5);
        if (utilization is double u && u > 0)
        {
            using var arc = new Pen(color, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(arc, 2, 2, size - 5, size - 5, -90, (float)(Math.Clamp(u, 0, 100) / 100.0 * 360));
        }

        // percentage number in the middle
        var text = utilization is double v ? Math.Round(v).ToString() : "–";
        var fontSize = text.Length >= 3 ? 10f : 13f;
        using var font = new Font("Segoe UI", fontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.White);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, new RectangleF(0, 1, size, size), sf);

        var hIcon = bmp.GetHicon();
        try
        {
            // Clone so we can release the GDI handle immediately.
            using var tmp = Icon.FromHandle(hIcon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }
}
