using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Threading;
using MyMelody.App.Services;

namespace MyMelody.App;

// Tests only this isolated fixture's hidden HWND. It never shows a window, captures the mouse, or sends input.
internal static class PetPlacementUiSmoke
{
    private const uint MoveFlags = 0x0001 | 0x0004 | 0x0010; // NOSIZE | NOZORDER | NOACTIVATE, deliberately no SHOWWINDOW

    public static void Run(AppHost host, string output, List<string> checks)
    {
        var manager = host.Manager;
        var pet = host.Pet;
        var settings = manager.Settings;
        string stateBefore = PracticeState(host);
        string settingsBefore = JsonSerializer.Serialize(settings);
        var original = (settings.CharacterVisible, settings.CharacterSize, settings.CharacterLeft, settings.CharacterTop);
        var results = new List<object>();
        var areas = WorkingAreas();
        Require(areas.Count > 0, "The native placement fixture needs a monitor work area.");
        try
        {
            settings.CharacterVisible = false;
            pet.Refresh();
            var hwnd = new WindowInteropHelper(pet).Handle;
            Require(hwnd != IntPtr.Zero, "Hidden popup did not create a native window handle.");
            DrainLayout(); RequireHidden(pet, hwnd);
            int farLeft = areas.Min(area => area.Left) - 10000;
            int farRight = areas.Max(area => area.Right) + 10000;
            int farTop = areas.Min(area => area.Top) - 10000;
            int farBottom = areas.Max(area => area.Bottom) + 10000;

            foreach (int size in new[] { 48, 96, 384 })
            {
                settings.CharacterSize = size;
                pet.Refresh(); DrainLayout();
                RequireSize(pet, hwnd, size);
                var current = Bounds(hwnd);
                var area = areas.FirstOrDefault(candidate => Contains(candidate, current));
                if (area.Width <= 0) area = areas[0];
                int x = area.Left + area.Width / 2, y = area.Top + area.Height / 2;
                var cases = new[]
                {
                    (Name: "left", X: farLeft, Y: y), (Name: "right", X: farRight, Y: y),
                    (Name: "top", X: x, Y: farTop), (Name: "bottom", X: x, Y: farBottom),
                    (Name: "far-corner", X: farRight, Y: farTop)
                };
                foreach (var test in cases)
                {
                    MoveHidden(hwnd, test.X, test.Y);
                    Require(pet.ClampPosition(), $"The {test.Name} off-screen position was not corrected.");
                    DrainLayout();
                    RequirePlaced(pet, hwnd, areas, size);
                    results.Add(new { sizeDip = size, test = test.Name, result = Bounds(hwnd), dpi = GetDpiForWindow(hwnd) });
                }

                // Positioning is allowed on every actual monitor, including negative-coordinate monitors.
                for (int index = 0; index < areas.Count; index++)
                {
                    area = areas[index];
                    MoveHidden(hwnd, area.Left + 16, area.Top + 16);
                    DrainLayout(); pet.ClampPosition(); DrainLayout();
                    RequirePlaced(pet, hwnd, areas, size);
                    current = Bounds(hwnd);
                    if (current.Width <= area.Width && current.Height <= area.Height)
                        Require(Contains(area, current), "Popup was trapped on a different monitor.");
                    results.Add(new { sizeDip = size, test = "monitor-" + index, result = current, dpi = GetDpiForWindow(hwnd) });
                }
            }

            // Restoring an obsolete saved position and then resizing must both use native work-area limits.
            settings.CharacterLeft = farRight; settings.CharacterTop = farBottom;
            pet.ApplySavedPosition(); DrainLayout();
            RequirePlaced(pet, hwnd, areas, 384);
            results.Add(new { sizeDip = 384, test = "saved-position-outside-desktop", result = Bounds(hwnd), dpi = GetDpiForWindow(hwnd) });
            settings.CharacterSize = 48; pet.Refresh(); DrainLayout();
            var resizeArea = areas.First(candidate => Contains(candidate, Bounds(hwnd)));
            var resizeBounds = Bounds(hwnd);
            MoveHidden(hwnd, resizeArea.Right - resizeBounds.Width, resizeArea.Bottom - resizeBounds.Height);
            settings.CharacterSize = 384; pet.Refresh(); DrainLayout();
            RequirePlaced(pet, hwnd, areas, 384);
            results.Add(new { sizeDip = 384, test = "resize-at-bottom-right", result = Bounds(hwnd), dpi = GetDpiForWindow(hwnd) });
        }
        finally
        {
            settings.CharacterVisible = false;
            settings.CharacterSize = original.CharacterSize;
            settings.CharacterLeft = original.CharacterLeft;
            settings.CharacterTop = original.CharacterTop;
            pet.ApplySavedPosition(); DrainLayout();
            // Restore preferences without showing the popup; the UI fixture intentionally keeps it hidden.
            settings.CharacterVisible = original.CharacterVisible;
            settings.CharacterSize = original.CharacterSize;
            settings.CharacterLeft = original.CharacterLeft;
            settings.CharacterTop = original.CharacterTop;
            manager.Save(); host.Main.LoadSettings();
        }
        Require(PracticeState(host) == stateBefore, "Native placement changed characters, experience, or original practice records.");
        Require(JsonSerializer.Serialize(settings) == settingsBefore, "Native placement did not restore fixture settings.");
        File.WriteAllText(Path.Combine(output, "pet-placement-checks.json"), JsonSerializer.Serialize(new
        {
            success = true, workingAreas = areas, results, popupStayedHidden = true, popupNeverActivated = true,
            settingsRestored = true, practiceStateUnchanged = true,
            realMouseCaptureTested = false, mixedDpiTransitionsRequireDifferentDpiMonitors = true
        }, new JsonSerializerOptions { WriteIndented = true }));
        checks.Add("The real hidden popup HWND returns from all four desktop edges and distant saved coordinates into actual monitor work areas; it can move across available monitors without activation.");
        checks.Add("Hidden native placement preserves 48/96/384 DIP sizes, corrects bottom-right resizing, and restores fixture settings without changing characters, experience, or practice sessions.");
    }

    private static string PracticeState(AppHost host) => JsonSerializer.Serialize(new
    {
        host.Manager.State, host.Manager.Sessions, host.Manager.TotalSeconds
    });

    private static void RequirePlaced(PetWindow pet, IntPtr hwnd, List<PixelBounds> areas, int size)
    {
        RequireHidden(pet, hwnd); RequireSize(pet, hwnd, size);
        var bounds = Bounds(hwnd);
        Require(areas.Any(area => Contains(area, bounds) ||
            (bounds.Width > area.Width || bounds.Height > area.Height) && bounds.Left == area.Left && bounds.Top == area.Top),
            "The hidden popup was left outside every monitor work area.");
    }

    private static void RequireHidden(PetWindow pet, IntPtr hwnd)
    {
        Require(!pet.IsVisible && !IsWindowVisible(hwnd), "The native placement test displayed the popup.");
        Require(!pet.IsActive && GetForegroundWindow() != hwnd, "The native placement test activated the popup.");
    }

    private static void RequireSize(PetWindow pet, IntPtr hwnd, int size)
    {
        Require(pet.Width == size && pet.Height == size, "Placement changed the requested character DIP size.");
        uint dpi = GetDpiForWindow(hwnd);
        Require(dpi > 0, "The popup DPI could not be read.");
        var bounds = Bounds(hwnd);
        double expected = size * dpi / 96d;
        Require(Math.Abs(bounds.Width - expected) <= 1 && Math.Abs(bounds.Height - expected) <= 1,
            "Native popup dimensions no longer match the requested character size and DPI.");
    }

    private static bool Contains(PixelBounds area, PixelBounds window) =>
        window.Left >= area.Left && window.Top >= area.Top && window.Right <= area.Right && window.Bottom <= area.Bottom;

    private static void MoveHidden(IntPtr hwnd, int x, int y) =>
        Require(SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, MoveFlags), "Could not position the hidden fixture HWND.");

    private static PixelBounds Bounds(IntPtr hwnd)
    {
        Require(GetWindowRect(hwnd, out var rect), "Could not read hidden popup bounds.");
        return new PixelBounds(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    private static void DrainLayout()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static List<PixelBounds> WorkingAreas()
    {
        var result = new List<PixelBounds>();
        MonitorEnum callback = (IntPtr monitor, IntPtr hdc, ref NativeRect rect, IntPtr data) =>
        {
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
                result.Add(new PixelBounds(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom));
            return true;
        };
        Require(EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero), "Could not enumerate native monitor work areas.");
        return result.Where(area => area.Width > 0 && area.Height > 0).ToList();
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public uint Size; public NativeRect Bounds, Work; public uint Flags; }
    private delegate bool MonitorEnum(IntPtr monitor, IntPtr hdc, ref NativeRect rect, IntPtr data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnum callback, IntPtr data);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
