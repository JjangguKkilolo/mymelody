using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MyMelody.App.Services;

namespace MyMelody.App;

public sealed class PetWindow : Window
{
    private const uint MoveFlags = 0x0001 | 0x0004 | 0x0010; // NOSIZE | NOZORDER | NOACTIVATE
    private readonly AppHost _host;
    private readonly SpriteView _sprite = new();
    private NativePoint _pressScreen;
    private double _grabX, _grabY, _dragThreshold;
    private IntPtr _hwnd;
    private bool _dragActive, _dragged, _applying, _movingWindow, _placementQueued, _saveAfterPlacement, _closed;

    public PetWindow(AppHost host)
    {
        _host = host;
        Title = "마이멜로디 · 바탕화면 친구";
        WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false;
        ResizeMode = ResizeMode.NoResize; Content = _sprite;
        Cursor = Cursors.Hand;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(_hwnd, -20, new IntPtr(GetWindowLongPtr(_hwnd, -20).ToInt64() | 0x08000000L | 0x00000080L));
            HwndSource.FromHwnd(_hwnd)?.AddHook(WindowHook);
        };
        MouseLeftButtonDown += (_, _) => BeginDrag();
        MouseMove += (_, e) =>
        {
            if (!_dragActive) return;
            if (e.LeftButton != MouseButtonState.Pressed || !IsMouseCaptured) { FinishDrag(false); return; }
            if (!GetCursorPos(out var cursor) || !GetWindowRect(_hwnd, out var bounds)) return;
            if (!_dragged && Math.Abs(cursor.X - _pressScreen.X) + Math.Abs(cursor.Y - _pressScreen.Y) < _dragThreshold) return;
            _dragged = true;
            // Screen pixels do not jump when the cursor crosses a monitor with a different DPI.
            // A fractional grab point stays on the character when WPF resizes it for the new monitor.
            MoveWindow(cursor.X - (int)Math.Round(_grabX * (bounds.Right - bounds.Left)),
                cursor.Y - (int)Math.Round(_grabY * (bounds.Bottom - bounds.Top)));
        };
        MouseLeftButtonUp += (_, _) => FinishDrag(true);
        LostMouseCapture += (_, _) => FinishDrag(false);
        MouseRightButtonUp += (_, _) => _host.ShowMain("Settings");
        SizeChanged += (_, _) => QueuePlacement();
        IsVisibleChanged += (_, _) => { if (IsVisible) QueuePlacement(); };
        Closing += (_, e) => { if (!_host.IsExiting) { e.Cancel = true; _host.SetPetVisibility(false); } };
        Closed += (_, _) => { _closed = true; _hwnd = IntPtr.Zero; };
    }

    private void BeginDrag()
    {
        if (!GetCursorPos(out _pressScreen) || !GetWindowRect(_hwnd, out var bounds)) return;
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0) return;
        _grabX = Math.Clamp((_pressScreen.X - bounds.Left) / (double)width, 0, 1);
        _grabY = Math.Clamp((_pressScreen.Y - bounds.Top) / (double)height, 0, 1);
        _dragThreshold = 5 * WindowScale;
        _dragged = false;
        _dragActive = CaptureMouse();
    }

    private void FinishDrag(bool openOnClick)
    {
        if (!_dragActive) return;
        _dragActive = false; // ReleaseMouseCapture raises LostMouseCapture synchronously.
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (_dragged)
        {
            ClampPosition(); SavePosition();
            QueuePlacement(savePosition: true); // WPF may still be applying the destination monitor's DPI.
        }
        else if (openOnClick) _host.ShowMain();
    }

    public void Refresh()
    {
        var manager = _host.Manager;
        var settings = manager.Settings;
        var character = manager.State.Characters.FirstOrDefault(x => x.Id == manager.State.DisplayCharacterId)
            ?? manager.State.Characters.FirstOrDefault(x => x.Id == manager.State.GrowingCharacterId);
        _sprite.ShowCharacter(character?.Id, manager.State.EffectiveDisplayStage);
        _sprite.IsPlaying = manager.IsPracticing;
        var newSize = Math.Clamp(settings.CharacterSize, 48, 384);
        bool sizeChanged = Width != newSize;
        Width = Height = newSize;
        Topmost = settings.AlwaysOnTop;
        bool correctedPosition = false;
        if (!_applying)
        {
            _applying = true;
            bool hasSavedPosition = settings.CharacterLeft is double left && double.IsFinite(left) &&
                settings.CharacterTop is double top && double.IsFinite(top);
            // Keep existing saved WPF DIP coordinates compatible. Native geometry is used after the HWND exists.
            Left = hasSavedPosition ? settings.CharacterLeft!.Value : 0;
            Top = hasSavedPosition ? settings.CharacterTop!.Value : 0;
            new WindowInteropHelper(this).EnsureHandle(); // Creates the HWND without showing or activating it.
            if (!hasSavedPosition) PlaceAtDefaultPosition();
            correctedPosition = ClampPosition();
        }
        if (sizeChanged) correctedPosition |= ClampPosition();
        if (sizeChanged || correctedPosition) QueuePlacement(savePosition: correctedPosition);
        if (settings.CharacterVisible) { if (!IsVisible) Show(); }
        else { FinishDrag(false); Hide(); }
    }

    public void ResetPosition()
    {
        FinishDrag(false);
        _host.Manager.Settings.CharacterLeft = null; _host.Manager.Settings.CharacterTop = null;
        _applying = false; Refresh(); _host.Manager.Save();
    }
    public void ApplySavedPosition() { FinishDrag(false); _applying = false; Refresh(); }
    public void Celebrate() => _sprite.Celebrate();

    private void PlaceAtDefaultPosition()
    {
        var monitors = GetWorkingAreas();
        if (monitors.Areas.Count == 0 || !GetWindowRect(_hwnd, out var bounds)) return;
        var area = monitors.Primary;
        MoveWindow(area.Right - (bounds.Right - bounds.Left) - (int)Math.Round(40 * WindowScale),
            area.Bottom - (bounds.Bottom - bounds.Top) - (int)Math.Round(35 * WindowScale));
    }

    internal bool ClampPosition()
    {
        if (_hwnd == IntPtr.Zero || _dragActive || _closed) return false;
        var areas = GetWorkingAreas().Areas;
        if (areas.Count == 0) return false;
        bool moved = false;
        // Moving to another monitor can change the physical window size through WPF's DPI handling.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (!GetWindowRect(_hwnd, out var native)) break;
            var bounds = new PixelBounds(native.Left, native.Top, native.Right, native.Bottom);
            if (bounds.Width <= 0 || bounds.Height <= 0) break;
            var clamped = DesktopPlacement.Clamp(bounds, areas);
            if (clamped.Left == bounds.Left && clamped.Top == bounds.Top) break;
            if (!MoveWindow(clamped.Left, clamped.Top)) break;
            moved = true;
        }
        return moved;
    }

    private void QueuePlacement(bool savePosition = false)
    {
        if (_closed || _host.IsExiting) return;
        _saveAfterPlacement |= savePosition;
        if (_placementQueued) return;
        _placementQueued = true;
        // WPF queues its DPI/layout update at Loaded priority; read native bounds after it finishes.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _placementQueued = false;
            bool save = _saveAfterPlacement; _saveAfterPlacement = false;
            if (_closed || _host.IsExiting || _dragActive) return;
            if (ClampPosition() || save) SavePosition();
        }));
    }

    private void SavePosition()
    {
        if (_closed || _host.IsExiting || !double.IsFinite(Left) || !double.IsFinite(Top)) return;
        var settings = _host.Manager.Settings;
        // WPF updates Left/Top from WM_MOVE, so these remain the legacy DIP values even after native movement.
        if (settings.CharacterLeft == Left && settings.CharacterTop == Top) return;
        settings.CharacterLeft = Left; settings.CharacterTop = Top;
        _host.Manager.Save();
    }

    private bool MoveWindow(int left, int top)
    {
        if (_hwnd == IntPtr.Zero) return false;
        _movingWindow = true;
        try { return SetWindowPos(_hwnd, IntPtr.Zero, left, top, 0, 0, MoveFlags); }
        finally { _movingWindow = false; }
    }

    private double WindowScale
    {
        get { uint dpi = GetDpiForWindow(_hwnd); return dpi == 0 ? 1 : dpi / 96d; }
    }

    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
        if (message == 0x02E0) QueuePlacement(savePosition: true);
        else if (message is 0x007E or 0x001A || message == 0x0047 && !_movingWindow && !_dragActive)
            QueuePlacement(); // DPI, display/work-area, or completed native window-position change.
        return IntPtr.Zero; // WPF must process WM_DPICHANGED itself.
    }

    private static (List<PixelBounds> Areas, PixelBounds Primary) GetWorkingAreas()
    {
        var areas = new List<PixelBounds>();
        PixelBounds primary = default;
        MonitorEnum callback = (IntPtr monitor, IntPtr hdc, ref NativeRect rect, IntPtr data) =>
        {
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var area = new PixelBounds(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
                if (area.Width > 0 && area.Height > 0)
                {
                    areas.Add(area);
                    if ((info.Flags & 1) != 0) primary = area;
                }
            }
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        if (primary.Width <= 0 && areas.Count > 0) primary = areas[0];
        return (areas, primary);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public uint Size; public NativeRect Bounds, Work; public uint Flags; }
    private delegate bool MonitorEnum(IntPtr monitor, IntPtr hdc, ref NativeRect rect, IntPtr data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnum callback, IntPtr data);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
