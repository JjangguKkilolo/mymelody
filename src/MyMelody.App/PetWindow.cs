using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MyMelody.Core;

namespace MyMelody.App;

public sealed class PetWindow : Window
{
    private readonly AppHost _host;
    private readonly SpriteView _sprite = new();
    private Point _press;
    private bool _dragged;
    private bool _applying;
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
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(hwnd, -20, new IntPtr(GetWindowLongPtr(hwnd, -20).ToInt64() | 0x08000000L | 0x00000080L));
            HwndSource.FromHwnd(hwnd)?.AddHook(WindowHook);
        };
        MouseLeftButtonDown += (_, e) => { _press = e.GetPosition(this); _dragged = false; CaptureMouse(); };
        MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || !IsMouseCaptured) return;
            var point = e.GetPosition(this);
            if (!_dragged && Math.Abs(point.X - _press.X) + Math.Abs(point.Y - _press.Y) < 5) return;
            _dragged = true;
            Left += point.X - _press.X; Top += point.Y - _press.Y;
        };
        MouseLeftButtonUp += (_, _) =>
        {
            if (!IsMouseCaptured) return;
            ReleaseMouseCapture();
            if (_dragged) { ClampPosition(); _host.Manager.Settings.CharacterLeft = Left; _host.Manager.Settings.CharacterTop = Top; _host.Manager.Save(); }
            else _host.ShowMain();
        };
        MouseRightButtonUp += (_, _) => _host.ShowMain("Settings");
        Closing += (_, e) => { if (!_host.IsExiting) { e.Cancel = true; _host.SetPetVisibility(false); } };
    }
    public void Refresh()
    {
        var manager = _host.Manager;
        var settings = manager.Settings;
        var character = manager.State.Characters.FirstOrDefault(x => x.Id == manager.State.DisplayCharacterId)
            ?? manager.State.Characters.FirstOrDefault(x => x.Id == manager.State.GrowingCharacterId);
        _sprite.ShowCharacter(character?.Id, character?.Stage ?? 1);
        _sprite.IsPlaying = manager.IsPracticing;
        var newSize = Math.Clamp(settings.CharacterSize, 48, 384);
        bool sizeChanged = Width != newSize;
        Width = Height = newSize;
        Topmost = settings.AlwaysOnTop;
        if (!_applying)
        {
            _applying = true;
            Left = settings.CharacterLeft ?? SystemParameters.WorkArea.Right - Width - 40;
            Top = settings.CharacterTop ?? SystemParameters.WorkArea.Bottom - Height - 35;
            ClampPosition();
        }
        if (sizeChanged) ClampPosition();
        if (settings.CharacterVisible) { if (!IsVisible) Show(); } else Hide();
    }
    public void ResetPosition()
    {
        _host.Manager.Settings.CharacterLeft = null; _host.Manager.Settings.CharacterTop = null;
        _applying = false; Refresh(); _host.Manager.Save();
    }
    public void ApplySavedPosition() { _applying = false; Refresh(); }
    public void Celebrate() => _sprite.Celebrate();
    private void ClampPosition()
    {
        var left = SystemParameters.VirtualScreenLeft; var top = SystemParameters.VirtualScreenTop;
        Left = Math.Clamp(double.IsFinite(Left) ? Left : left, left, Math.Max(left, left + SystemParameters.VirtualScreenWidth - Width));
        Top = Math.Clamp(double.IsFinite(Top) ? Top : top, top, Math.Max(top, top + SystemParameters.VirtualScreenHeight - Height));
    }
    private static IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
        return IntPtr.Zero;
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
