namespace MyMelody.App.Services;

/// <summary>Screen coordinates and dimensions in physical pixels, including negative monitor origins.</summary>
internal readonly record struct PixelBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

internal static class DesktopPlacement
{
    /// <summary>Choose one real work area, never the enclosing virtual-desktop rectangle.</summary>
    public static PixelBounds Clamp(PixelBounds window, IReadOnlyList<PixelBounds> workingAreas)
    {
        if (window.Width <= 0 || window.Height <= 0) throw new ArgumentException("Window dimensions must be positive.", nameof(window));
        if (workingAreas.Count == 0) throw new ArgumentException("A monitor work area is required.", nameof(workingAreas));
        PixelBounds? best = null;
        long bestOverlap = -1;
        double bestDistance = double.PositiveInfinity;
        foreach (var area in workingAreas)
        {
            if (area.Width <= 0 || area.Height <= 0) continue;
            long overlap = (long)Math.Max(0, Math.Min(window.Right, area.Right) - Math.Max(window.Left, area.Left)) *
                Math.Max(0, Math.Min(window.Bottom, area.Bottom) - Math.Max(window.Top, area.Top));
            double dx = Math.Max(0d, Math.Max((double)area.Left - window.Right, (double)window.Left - area.Right));
            double dy = Math.Max(0d, Math.Max((double)area.Top - window.Bottom, (double)window.Top - area.Bottom));
            double distance = dx * dx + dy * dy;
            if (overlap > bestOverlap || overlap == bestOverlap && distance < bestDistance)
            {
                best = area; bestOverlap = overlap; bestDistance = distance;
            }
        }
        if (best is not { } target) throw new ArgumentException("A valid monitor work area is required.", nameof(workingAreas));
        // If a window is larger than a work area, keep its top-left reachable without changing the user's size.
        int left = Math.Clamp(window.Left, target.Left, Math.Max(target.Left, target.Right - window.Width));
        int top = Math.Clamp(window.Top, target.Top, Math.Max(target.Top, target.Bottom - window.Height));
        return new PixelBounds(left, top, left + window.Width, top + window.Height);
    }
}
