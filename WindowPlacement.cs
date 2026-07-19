using System.Windows;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace ClaudeUsageWidget;

internal static class WindowPlacement
{
    internal const double MinimumVisibleWidth = 48;
    internal const double MinimumVisibleHeight = 32;

    internal static bool NeedsRecovery(Rect windowBounds, Rect virtualScreenBounds)
    {
        if (windowBounds.IsEmpty || virtualScreenBounds.IsEmpty)
            return true;

        var visible = Rect.Intersect(windowBounds, virtualScreenBounds);
        if (visible.IsEmpty)
            return true;

        var requiredWidth = Math.Min(MinimumVisibleWidth, windowBounds.Width);
        var requiredHeight = Math.Min(MinimumVisibleHeight, windowBounds.Height);
        return visible.Width < requiredWidth || visible.Height < requiredHeight;
    }

    internal static Point TopRightOf(Rect workArea, Size windowSize, double margin = 16)
    {
        var left = Math.Max(workArea.Left, workArea.Right - windowSize.Width - margin);
        var top = Math.Min(
            Math.Max(workArea.Top, workArea.Top + margin),
            Math.Max(workArea.Top, workArea.Bottom - windowSize.Height));
        return new Point(left, top);
    }
}
