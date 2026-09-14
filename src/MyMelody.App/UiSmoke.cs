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
        VerifyIcon(host.Main, checks, output);
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
        VerifySprites(output, checks);
        var backup = Path.Combine(output, "roundtrip.zip");
        manager.Backup(backup);
        double before = manager.TotalSeconds;
        manager.Restore(backup);
        if (manager.TotalSeconds != before || manager.State.GrowingCharacter?.Id != current.Id) throw new InvalidDataException("UI backup roundtrip failed.");
        checks.Add("UI backing state persists and roundtrips backup after7 unique draws.");
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { success = true, checks, manager.DataDirectory, totalSeconds = manager.TotalSeconds }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void VerifySprites(string output, List<string> checks)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Assets", "Characters");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "assets-manifest.json")));
        if (manifest.RootElement.GetProperty("schemaVersion").GetInt32() != 2)
            throw new InvalidDataException("The character manifest must use schema version 2.");
        var entries = manifest.RootElement.GetProperty("assets").EnumerateArray().ToList();
        var ids = entries.Select(entry => entry.GetProperty("id").GetString()).ToList();
        if (!ids.Order().SequenceEqual(CharacterCatalog.All.Select(character => character.Id).Order()))
            throw new InvalidDataException("The character manifest must contain exactly one sprite sheet for every catalog ID.");

        foreach (var definition in CharacterCatalog.All)
        {
            var entry = entries.Single(asset => asset.GetProperty("id").GetString() == definition.Id);
            var filename = entry.GetProperty("filename").GetString();
            if (filename != definition.Id + ".png" || entry.GetProperty("columns").GetInt32() != SpriteSheet.Columns ||
                entry.GetProperty("rows").GetInt32() != SpriteSheet.Stages || entry.GetProperty("stages").GetInt32() != SpriteSheet.Stages ||
                entry.GetProperty("framesPerStage").GetInt32() != SpriteSheet.Columns)
                throw new InvalidDataException("Invalid sprite sheet manifest layout: " + definition.Id);
            var source = SpriteSheet.PathFor(definition.Id);
            var bitmap = SpriteSheet.Load(source) ?? throw new InvalidDataException("Missing character asset: " + source);
            if (bitmap.Format.BitsPerPixel < 32) throw new InvalidDataException("Character has no alpha channel: " + source);
            if (entry.GetProperty("width").GetInt32() != bitmap.PixelWidth || entry.GetProperty("height").GetInt32() != bitmap.PixelHeight ||
                entry.GetProperty("frameWidth").GetInt32() != bitmap.PixelWidth / SpriteSheet.Columns ||
                entry.GetProperty("frameHeight").GetInt32() != bitmap.PixelHeight / SpriteSheet.Stages)
                throw new InvalidDataException("Sprite dimensions differ from the manifest: " + source);

            var stageFrames = new List<byte[]>();
            for (var stage = 1; stage <= SpriteSheet.Stages; stage++)
            {
                var open = VerifyFrame(SpriteSheet.GridFrame(source, stage, false), $"{definition.Id}, stage {stage}, open");
                var blink = VerifyFrame(SpriteSheet.GridFrame(source, stage, true), $"{definition.Id}, stage {stage}, blink");
                if (open.AsSpan().SequenceEqual(blink)) throw new InvalidDataException($"Blink expression is identical: {definition.Id}, stage {stage}");
                if (stageFrames.Any(previous => previous.AsSpan().SequenceEqual(open)))
                    throw new InvalidDataException($"Growth appearance repeats an earlier stage: {definition.Id}, stage {stage}");
                stageFrames.Add(open);
            }
        }

        const int columns = 6, cellWidth = 160, cellHeight = 150;
        int width = columns * cellWidth;
        int height = (int)Math.Ceiling(CharacterCatalog.All.Count * SpriteSheet.Stages / (double)columns) * cellHeight;
        foreach (var blink in new[] { false, true })
        {
            var atlas = new System.Windows.Controls.Primitives.UniformGrid
            {
                Columns = columns, Width = width, Height = height,
                Background = new SolidColorBrush(Color.FromRgb(255, 245, 249))
            };
            foreach (var definition in CharacterCatalog.All)
            for (int stage = 1; stage <= SpriteSheet.Stages; stage++)
            {
                var sprite = new SpriteView { Width = 96, Height = 96 };
                sprite.ShowCharacter(definition.Id, stage);
                sprite.ShowExpression(blink);
                sprite.Measure(new Size(96, 96)); sprite.Arrange(new Rect(0, 0, 96, 96)); sprite.UpdateLayout();
                var suffix = blink ? "-blink" : "";
                SaveVisual(sprite, 96, 96, 1, Path.Combine(output, $"{definition.Id}-{stage}{suffix}-96.png"));
                var cell = new StackPanel { Margin = new Thickness(4, 12, 4, 4) };
                cell.Children.Add(sprite);
                cell.Children.Add(new TextBlock
                {
                    Text = $"{definition.Name} · {stage}단계", FontFamily = new FontFamily("Malgun Gothic"), FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(108, 71, 89)), HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 0)
                });
                atlas.Children.Add(cell);
            }
            atlas.Measure(new Size(width, height)); atlas.Arrange(new Rect(0, 0, width, height)); atlas.UpdateLayout();
            SaveVisual(atlas, width, height, 1, Path.Combine(output, blink ? "all-characters-blink.png" : "all-characters.png"));
        }
        checks.Add($"All {CharacterCatalog.All.Count} sprite sheets match the catalog and manifest, with {CharacterCatalog.All.Count * SpriteSheet.Stages} distinct growth appearances and {CharacterCatalog.All.Count * SpriteSheet.Stages * SpriteSheet.Columns} nonempty transparent expression frames rendered at 96 DIP.");
    }

    private static byte[] VerifyFrame(BitmapSource bitmap, string name)
    {
        var rgba = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int stride = rgba.PixelWidth * 4;
        var pixels = new byte[stride * rgba.PixelHeight];
        rgba.CopyPixels(pixels, stride, 0);
        // A few generated alpha values round to 1/255. Check the full border for visible
        // content, allowing only that quantization residue while preserving the source PNG.
        for (var x = 0; x < rgba.PixelWidth; x++)
            if (pixels[x * 4 + 3] > 1 || pixels[(rgba.PixelHeight - 1) * stride + x * 4 + 3] > 1)
                throw new InvalidDataException("Character content touches a horizontal expression-cell border: " + name);
        for (var y = 0; y < rgba.PixelHeight; y++)
            if (pixels[y * stride + 3] > 1 || pixels[y * stride + stride - 1] > 1)
                throw new InvalidDataException("Character content touches a vertical expression-cell border: " + name);
        var visiblePixels = 0;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset + 3] > 1) visiblePixels++;
            else pixels.AsSpan(offset, 4).Clear(); // Hidden RGB and quantization residue cannot make expressions pass.
        }
        if (visiblePixels == 0) throw new InvalidDataException("Expression cell is empty: " + name);
        return pixels;
    }

    private static void VerifyIcon(MainWindow window, List<string> checks, string output)
    {
        var decoder = BitmapDecoder.Create(AppIcon.ResourceUri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        int[] expectedSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        if (!decoder.Frames.Select(frame => frame.PixelWidth).Order().SequenceEqual(expectedSizes) ||
            decoder.Frames.Any(frame => frame.PixelWidth != frame.PixelHeight))
            throw new InvalidDataException("The bundled icon must contain all nine square Windows icon sizes.");
        if (window.Icon == null) throw new InvalidDataException("The management window icon was not assigned.");
        using var trayIcon = AppIcon.CreateTrayIcon();
        if (trayIcon.Width != trayIcon.Height || !expectedSizes.Contains(trayIcon.Width))
            throw new InvalidDataException("The tray icon could not select a bundled Windows icon size.");
        if (string.Equals(Path.GetFileName(Environment.ProcessPath), "MyMelodyPractice.exe", StringComparison.OrdinalIgnoreCase))
        {
            if (GetIconCount(Environment.ProcessPath!, -1, IntPtr.Zero, IntPtr.Zero, 0) is 0 or uint.MaxValue)
                throw new InvalidDataException("The application executable has no embedded icon.");
        }
        var preview = new StackPanel { Width = 940, Background = Brushes.White };
        foreach (var dark in new[] { false, true })
        {
            var section = new StackPanel { Margin = new Thickness(20, 14, 20, 14) };
            section.Children.Add(new TextBlock
            {
                Text = dark ? "Windows icon sizes · dark background" : "Windows icon sizes · light background",
                FontFamily = new FontFamily("Segoe UI"), FontSize = 16,
                Foreground = dark ? Brushes.White : Brushes.Black, Margin = new Thickness(0, 0, 0, 10)
            });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var frame in decoder.Frames.OrderBy(frame => frame.PixelWidth))
            {
                int size = frame.PixelWidth;
                var cell = new StackPanel { Width = Math.Max(48, size) + 12 };
                var imageArea = new Grid { Height = 256 };
                imageArea.Children.Add(new Image { Source = frame, Width = size, Height = size, Stretch = Stretch.None });
                cell.Children.Add(imageArea);
                cell.Children.Add(new TextBlock
                {
                    Text = $"{size} px", FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0),
                    Foreground = dark ? Brushes.White : Brushes.Black
                });
                row.Children.Add(cell);
            }
            section.Children.Add(row);
            preview.Children.Add(new Border { Background = dark ? new SolidColorBrush(Color.FromRgb(32, 33, 37)) : Brushes.White, Child = section });
        }
        preview.Measure(new Size(940, double.PositiveInfinity));
        preview.Arrange(new Rect(new Point(), preview.DesiredSize)); preview.UpdateLayout();
        SaveVisual(preview, preview.ActualWidth, preview.ActualHeight, 1, Path.Combine(output, "icons-preview.png"));
        checks.Add("All nine bundled ICO sizes decode; the window and tray load the custom icon, and the published executable embeds an icon.");
    }
    [System.Runtime.InteropServices.DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint GetIconCount(string path, int index, IntPtr largeIcons, IntPtr smallIcons, uint count);
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
