using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MyMelody.Core;

namespace MyMelody.App;

// Explicit, isolated rendering harness; never uses the personal database or MIDI device.
internal sealed class UiSmokeClock : TimeProvider
{
    private DateTimeOffset _now = new(DateTime.Today.AddDays(-14).AddHours(8));
    private long _timestamp;
    public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();
    public override long GetTimestamp() => _timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public void Advance(TimeSpan elapsed) { _now += elapsed; _timestamp += elapsed.Ticks; }
}

internal static class UiSmoke
{
    public static void Run(AppHost host, UiSmokeClock clock, string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var manager = host.Manager;
        var checks = new List<string>();
        host.Main.Refresh(); Render(host.Main, output, "welcome", 1);
        checks.Add("Welcome window renders before first draw.");
        for (int i = 0; i < 6; i++)
        {
            manager.Draw(); manager.StartManual(); clock.Advance(TimeSpan.FromHours(36)); manager.Tick(); manager.StopManual();
            clock.Advance(TimeSpan.FromHours(12));
        }
        var current = manager.Draw();
        manager.StartManual(); clock.Advance(TimeSpan.FromHours(13)); manager.Tick(); manager.StopManual();
        manager.NoteOn(); clock.Advance(TimeSpan.FromSeconds(23)); manager.Tick(); manager.Suspend();
        clock.Advance(TimeSpan.FromMinutes(2));
        manager.NoteOn(); clock.Advance(TimeSpan.FromSeconds(30)); manager.Tick();
        var groupedAutomatic = manager.GetPracticeSessions().Where(s => s.Mode == PracticeMode.Automatic).ToList();
        if (groupedAutomatic.Count != 1 || groupedAutomatic[0].PracticeSeconds != 53)
            throw new InvalidDataException("Short practice intervals should display as one 53-second session.");
        clock.Advance(TimeSpan.FromMinutes(30));
        manager.NoteOn(); clock.Advance(TimeSpan.FromSeconds(5)); manager.Tick(); manager.Suspend();
        if (manager.GetPracticeSessions().Count(s => s.Mode == PracticeMode.Automatic) != 2)
            throw new InvalidDataException("A thirty-minute break must display a new session.");
        checks.Add("23-second and30-second records share one53-second session; thirty-minute rest separates the next session without idle credit.");
        host.Main.Refresh();
        foreach (var page in new[] { "Home", "Collection", "Records", "Settings" })
        {
            host.Main.Navigate(page);
            foreach (var scale in new[] { 1.0, 1.5, 2.0 }) Render(host.Main, output, page.ToLowerInvariant(), scale);
            host.Main.PageScroll.ScrollToBottom();
            Render(host.Main, output, page.ToLowerInvariant() + "-bottom", 1);
        }
        checks.Add("All four views render at 100%,150%,200% with actual PNG sprites.");
        host.Main.Width = host.Main.MinWidth; host.Main.Height = host.Main.MinHeight;
        host.Main.Navigate("Home"); Render(host.Main, output, "home-minimum", 1);
        var atlas = new System.Windows.Controls.Primitives.UniformGrid { Columns = 9, Width = 1152, Height = 450, Background = new SolidColorBrush(Color.FromRgb(255, 245, 249)) };
        for (int stage = 1; stage <= 3; stage++)
        foreach (var definition in CharacterCatalog.All)
        {
            var source = Path.Combine(AppContext.BaseDirectory, "Assets", "Characters", $"{definition.Id}-{stage}.png");
            if (!File.Exists(source)) throw new InvalidDataException("Missing character asset: " + source);
            var bitmap = BitmapFrame.Create(new Uri(source), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (bitmap.Format.BitsPerPixel < 32) throw new InvalidDataException("Character has no alpha channel: " + source);
            if (bitmap.PixelWidth != bitmap.PixelHeight * 2) throw new InvalidDataException("Expected two horizontal expression frames: " + source);
            var rgba = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[rgba.PixelWidth * rgba.PixelHeight * 4];
            rgba.CopyPixels(pixels, rgba.PixelWidth * 4, 0);
            if (pixels[3] != 0 || pixels[rgba.PixelWidth * 4 - 1] != 0 || pixels[^1] != 0)
                throw new InvalidDataException("Character corners must be transparent: " + source);
            var sprite = new SpriteView { Width = 96, Height = 96 };
            sprite.ShowCharacter(definition.Id, stage);
            sprite.Measure(new Size(96, 96)); sprite.Arrange(new Rect(0, 0, 96, 96)); sprite.UpdateLayout();
            SaveVisual(sprite, 96, 96, 1, Path.Combine(output, $"{definition.Id}-{stage}-96.png"));
            var cell = new StackPanel { Margin = new Thickness(4, 12, 4, 4) };
            cell.Children.Add(sprite);
            cell.Children.Add(new TextBlock { Text = $"{definition.Name} · {stage}단계", FontFamily = new FontFamily("Malgun Gothic"), FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(108, 71, 89)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
            atlas.Children.Add(cell);
        }
        atlas.Measure(new Size(1152, 450)); atlas.Arrange(new Rect(0, 0, 1152, 450)); atlas.UpdateLayout();
        SaveVisual(atlas, 1152, 450, 1, Path.Combine(output, "all-characters.png"));
        checks.Add("All27 PNG assets contain two expression frames, transparent corners, and render at96DIP.");
        var backup = Path.Combine(output, "roundtrip.zip");
        manager.Backup(backup);
        double before = manager.TotalSeconds;
        manager.Restore(backup);
        if (manager.TotalSeconds != before || manager.State.GrowingCharacter?.Id != current.Id) throw new InvalidDataException("UI backup roundtrip failed.");
        checks.Add("UI backing state persists and roundtrips backup after7 unique draws.");
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { success = true, checks, manager.DataDirectory, totalSeconds = manager.TotalSeconds }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void Render(MainWindow window, string directory, string name, double scale)
    {
        window.UpdateLayout();
        if (window.Content is not FrameworkElement root) throw new InvalidOperationException("Window content missing.");
        root.UpdateLayout();
        SaveVisual(root, root.ActualWidth, root.ActualHeight, scale, Path.Combine(directory, $"{name}-{scale * 100:0}.png"));
    }
    private static void SaveVisual(Visual visual, double width, double height, double scale, string path)
    {
        var target = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        target.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
