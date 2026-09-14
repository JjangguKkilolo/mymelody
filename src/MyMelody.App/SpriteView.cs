using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MyMelody.App;

public sealed class SpriteView : Grid
{
    private readonly Image _image = new() { Stretch = Stretch.Uniform, Margin = new Thickness(2, 4, 2, 8) };
    private readonly TextBlock _empty = new() { Text = "♡", FontSize = 60, Foreground = new SolidColorBrush(Color.FromRgb(209, 143, 169)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _note = new() { Text = "♪", FontSize = 22, Foreground = new SolidColorBrush(Color.FromRgb(183, 102, 139)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 12, 0), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly TranslateTransform _bob = new();
    private readonly TextBlock _sparkles = new() { Text = "✧   ♡   ✧", FontSize = 16, Foreground = new SolidColorBrush(Color.FromRgb(183, 102, 139)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer _timer;
    private int _frame;
    private int _celebrationFrames;
    public bool IsPlaying { get; set; }
    public string? CharacterId { get; private set; }
    public int Stage { get; private set; }

    public SpriteView()
    {
        ClipToBounds = false;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        _image.RenderTransform = _bob;
        Children.Add(_empty); Children.Add(_image); Children.Add(_note); Children.Add(_sparkles);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _timer.Tick += (_, _) =>
        {
            _frame++;
            _sparkles.Visibility = _celebrationFrames > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_celebrationFrames > 0)
            {
                _celebrationFrames--;
                _sparkles.Opacity = _celebrationFrames % 4 < 2 ? 1 : 0.45;
                _sparkles.RenderTransform = new TranslateTransform(0, 4 - (18 - _celebrationFrames) / 2.0);
            }
            _bob.Y = IsPlaying ? (_frame % 4 < 2 ? -3 : 0) : (_frame % 20 < 10 ? 0 : -1);
            _note.Visibility = IsPlaying && _frame % 8 < 6 ? Visibility.Visible : Visibility.Collapsed;
            ShowExpression(_frame % 32 == 0);
        };
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
        IsVisibleChanged += (_, _) => { if (IsVisible) _timer.Start(); else _timer.Stop(); };
    }

    public void Celebrate() { _celebrationFrames = 18; }

    public void ShowCharacter(string? id, int stage)
    {
        if (id == CharacterId && stage == Stage && _image.Source != null) return;
        CharacterId = id; Stage = stage;
        _frame = 0;
        ShowExpression(false);
        _empty.Visibility = _image.Source == null ? Visibility.Visible : Visibility.Collapsed;
    }
    internal void ShowExpression(bool blink) => _image.Source = CharacterId == null ? null : SpriteSheet.Frame(CharacterId, Stage, blink);
}
