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
        VerifyCollectionStageSelection(host, clock, output, checks);
        RecordsUiSmoke.Run(host, output, checks);
        PetPlacementUiSmoke.Run(host, output, checks);
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
        host.Main.Navigate("Collection"); Render(host.Main, output, "collection-minimum", 1);
        host.Main.PageScroll.ScrollToBottom(); Render(host.Main, output, "collection-minimum-bottom", 1);
        VerifyNarrowCollection(host.Main, output, checks);
        VerifySprites(output, checks);
        VerifyUniformSpriteSizes(output, checks);
        var backup = Path.Combine(output, "roundtrip.zip");
        manager.Backup(backup);
        double before = manager.TotalSeconds;
        manager.Restore(backup);
        if (manager.TotalSeconds != before || manager.State.GrowingCharacter?.Id != current.Id) throw new InvalidDataException("UI backup roundtrip failed.");
        checks.Add("UI backing state persists and roundtrips backup after7 unique draws.");
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { success = true, checks, manager.DataDirectory, totalSeconds = manager.TotalSeconds }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void VerifyCollectionStageSelection(AppHost host, UiSmokeClock clock, string output, List<string> checks)
    {
        var manager = host.Manager;
        bool originalVisibility = manager.Settings.CharacterVisible;
        // Exercise the real popup sprite without displaying a window on the user's desktop.
        manager.Settings.CharacterVisible = false;
        manager.Save();
        try
        {
            host.Main.Navigate("Collection"); host.Main.Refresh(); host.Main.UpdateLayout();
            host.Pet.Refresh();
            var growingId = manager.State.GrowingCharacterId ?? throw new InvalidDataException("Stage-selection fixture has no growing character.");
            var completedId = manager.State.Characters.First(character => character.IsComplete).Id;
            var unownedId = CharacterCatalog.All.First(character => manager.State.Characters.All(owned => owned.Id != character.Id)).Id;
            RequireStage(manager.State.GrowingCharacter?.Stage == 2, "Stage-selection fixture must have a character at stage 2.");
            var growingCard = CollectionCardFor(host.Main, growingId);
            RequireStage(manager.State.DisplayStage == null && growingCard.PreviewStage == 2 && growingCard.ApplyButton.IsEnabled,
                "An automatically displayed current stage must still allow explicitly fixing that appearance.");
            RequireStage(growingCard.StageButtons.Count == 3 && growingCard.StageButtons[0].IsEnabled && growingCard.StageButtons[1].IsEnabled && !growingCard.StageButtons[2].IsEnabled,
                "A stage-2 character must unlock only its first two previews.");
            var lockedCard = CollectionCardFor(host.Main, unownedId);
            RequireStage(lockedCard.StageButtons.Count == 3 && lockedCard.StageButtons.All(button => !button.IsEnabled) &&
                (lockedCard.ApplyButton.Visibility != Visibility.Visible || !lockedCard.ApplyButton.IsEnabled),
                "An unowned character must not allow stage selection or display application.");

            var beforePreview = CaptureStageState(manager);
            var beforePet = PetIdentity(host);
            ClickStageButton(growingCard.StageButtons[0]);
            growingCard = CollectionCardFor(host.Main, growingId);
            RequireStage(growingCard.PreviewStage == 1 && growingCard.Sprite.CharacterId == growingId && growingCard.Sprite.Stage == 1,
                "Clicking stage 1 must preview stage 1 in its collection card.");
            RequireStage(CaptureStageState(manager) == beforePreview && PetIdentity(host) == beforePet,
                "Previewing a stage changed saved state, experience, sessions, or the desktop character.");
            RequireHomeGrowth(host, growingId, 2);
            host.Main.Refresh(); host.Main.UpdateLayout();
            RequireStage(CollectionCardFor(host.Main, growingId).PreviewStage == 1, "Refreshing the collection discarded its stage preview.");

            ClickStageButton(CollectionCardFor(host.Main, growingId).ApplyButton);
            RequireDisplay(host, growingId, 1, 1);
            RequireHomeGrowth(host, growingId, 2);
            RequireSavedDisplay(manager, growingId, 1);
            RequireStage(!CollectionCardFor(host.Main, growingId).ApplyButton.IsEnabled, "An already fixed appearance should not need to be applied again.");

            double growthBefore = manager.State.GrowingCharacter!.PracticeSeconds;
            manager.StartManual(); clock.Advance(TimeSpan.FromSeconds(1)); manager.Tick(); manager.StopManual();
            host.Main.Refresh(); host.Pet.Refresh(); host.Main.UpdateLayout();
            RequireStage(manager.State.GrowingCharacter!.PracticeSeconds == growthBefore + 1 && manager.State.GrowingCharacter.Stage == 2,
                "The stage-selection fixture must advance exactly one practice second without changing growth stage.");
            RequireDisplay(host, growingId, 1, 1);
            RequireStage(CollectionCardFor(host.Main, growingId).PreviewStage == 1, "A practice update discarded the selected earlier appearance.");

            string backup = Path.Combine(output, "stage-selection-roundtrip.zip");
            manager.Backup(backup);
            double backedUpSeconds = manager.TotalSeconds;
            string backedUpSessions = JsonSerializer.Serialize(manager.Sessions);
            string unchangedGrowth = GrowthIdentity(manager);
            host.Main.PageScroll.ScrollToVerticalOffset(Math.Min(150, host.Main.PageScroll.ScrollableHeight / 2));
            host.Main.UpdateLayout();
            double collectionOffset = host.Main.PageScroll.VerticalOffset;
            RequireStage(collectionOffset > 0, "The collection fixture must be scrollable for preview-position checks.");
            for (var stage = 1; stage <= 3; stage++)
            {
                var completedCard = CollectionCardFor(host.Main, completedId);
                RequireStage(completedCard.StageButtons.All(button => button.IsEnabled) && completedCard.FollowGrowthButton.Visibility == Visibility.Collapsed,
                    "Completed characters must unlock all stage previews and hide follow-growth mode.");
                beforePreview = CaptureStageState(manager); beforePet = PetIdentity(host);
                ClickStageButton(completedCard.StageButtons[stage - 1]);
                completedCard = CollectionCardFor(host.Main, completedId);
                RequireStage(completedCard.PreviewStage == stage && completedCard.Sprite.Stage == stage,
                    $"Completed character did not preview stage {stage}.");
                RequireStage(CaptureStageState(manager) == beforePreview && PetIdentity(host) == beforePet,
                    "A completed-character preview changed saved or desktop state before application.");
                ClickStageButton(completedCard.ApplyButton);
                RequireDisplay(host, completedId, stage, stage);
                RequireHomeGrowth(host, growingId, 2);
                RequireStage(GrowthIdentity(manager) == unchangedGrowth, "Displaying a completed character altered the actual growing character or practice records.");
                host.Main.UpdateLayout();
                RequireStage(CollectionCardFor(host.Main, growingId).PreviewStage == 1, "Rebuilding collection cards lost another character's preview.");
                RequireStage(Math.Abs(host.Main.PageScroll.VerticalOffset - collectionOffset) <= 0.5,
                    "Applying an appearance moved the collection scroll position.");
            }

            manager.Restore(backup);
            host.Main.Refresh(); host.Pet.Refresh(); host.Main.UpdateLayout();
            RequireDisplay(host, growingId, 1, 1);
            RequireSavedDisplay(manager, growingId, 1);
            RequireStage(manager.TotalSeconds == backedUpSeconds && JsonSerializer.Serialize(manager.Sessions) == backedUpSessions,
                "Restoring a selected stage changed the original practice records.");
            RequireHomeGrowth(host, growingId, 2);

            growingCard = CollectionCardFor(host.Main, growingId);
            RequireStage(growingCard.FollowGrowthButton.Visibility == Visibility.Visible, "Growing characters must offer follow-growth mode.");
            ClickStageButton(growingCard.FollowGrowthButton);
            RequireDisplay(host, growingId, null, 2);
            RequireSavedDisplay(manager, growingId, null);
            RequireHomeGrowth(host, growingId, 2);
            RequireStage(GrowthIdentity(manager) == unchangedGrowth, "Switching to follow-growth changed experience or records.");

            // Leave a real earlier-stage preview in the collection screenshots while the popup follows growth.
            ClickStageButton(CollectionCardFor(host.Main, growingId).StageButtons[0]);
            host.Main.Navigate("Home"); host.Main.Navigate("Collection"); host.Main.UpdateLayout();
            RequireStage(CollectionCardFor(host.Main, growingId).PreviewStage == 1, "Returning to the collection lost a preview selection.");
            RequireDisplay(host, growingId, null, 2);
            File.WriteAllText(Path.Combine(output, "stage-selection-checks.json"), JsonSerializer.Serialize(new
            {
                success = true, growingCharacterId = growingId, actualGrowthStage = 2,
                completedCharacterId = completedId, unownedCharacterId = unownedId,
                checkedCompletedStages = new[] { 1, 2, 3 }, fixedStageRestoredFromBackup = 1,
                finalDisplayStage = manager.State.DisplayStage, finalEffectiveStage = manager.State.EffectiveDisplayStage,
                finalPreviewStage = CollectionCardFor(host.Main, growingId).PreviewStage, collectionOffset
            }, new JsonSerializerOptions { WriteIndented = true }));
            checks.Add("Real collection buttons preview unlocked stages without changing saved state, popup, experience, or sessions; future and unowned stages stay locked.");
            checks.Add("Fixed stage 1 survives practice and backup restore; completed characters can display all three stages independently of stage-2 growth, and follow-growth restores the current appearance.");
            checks.Add("Collection refresh, display application, and navigation preserve preview selection; applying an appearance preserves scroll position and the home view always shows actual growth.");
        }
        finally
        {
            manager.Settings.CharacterVisible = originalVisibility;
            host.Main.LoadSettings();
        }
    }

    private sealed record StageStateSnapshot(string State, string SavedState, string Sessions);
    private static StageStateSnapshot CaptureStageState(PracticeManager manager) => new(
        JsonSerializer.Serialize(manager.State), ReadSavedState(manager), JsonSerializer.Serialize(manager.Sessions));
    private static string GrowthIdentity(PracticeManager manager) => JsonSerializer.Serialize(new { manager.State.GrowingCharacterId, manager.State.Characters, manager.Sessions });
    private static (string? Id, int Stage) PetIdentity(AppHost host) => host.Pet.Content is SpriteView sprite
        ? (sprite.CharacterId, sprite.Stage) : throw new InvalidDataException("Desktop character content is not a SpriteView.");
    private static CollectionCard CollectionCardFor(MainWindow window, string id)
    {
        var grid = window.FindName("CollectionGrid") as Panel ?? throw new InvalidDataException("Collection panel is missing.");
        return grid.Children.OfType<CollectionCard>().Single(card => card.CharacterId == id);
    }
    private static void ClickStageButton(Button button)
    {
        RequireStage(button.IsEnabled && button.Visibility == Visibility.Visible, "The stage-selection test attempted to click a locked or hidden button.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    }
    private static void RequireDisplay(AppHost host, string id, int? fixedStage, int effectiveStage)
    {
        RequireStage(host.Manager.State.DisplayCharacterId == id && host.Manager.State.DisplayStage == fixedStage &&
            host.Manager.State.EffectiveDisplayStage == effectiveStage && PetIdentity(host) == (id, effectiveStage),
            $"Desktop selection does not match {id}, fixed stage {fixedStage?.ToString() ?? "automatic"}, effective stage {effectiveStage}.");
    }
    private static void RequireHomeGrowth(AppHost host, string id, int stage)
    {
        host.Main.Refresh();
        var hero = host.Main.FindName("HeroSprite") as SpriteView;
        RequireStage(host.Manager.State.GrowingCharacterId == id && host.Manager.State.GrowingCharacter?.Stage == stage &&
            hero?.CharacterId == id && hero.Stage == stage, "Home or actual growth followed a display-only stage selection.");
    }
    private static string ReadSavedState(PracticeManager manager)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(manager.DataDirectory, "practice.sqlite"), Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM app_state WHERE id=1;";
        return command.ExecuteScalar() as string ?? throw new InvalidDataException("Saved application state is missing.");
    }
    private static void RequireSavedDisplay(PracticeManager manager, string id, int? stage)
    {
        using var saved = JsonDocument.Parse(ReadSavedState(manager));
        var state = saved.RootElement.GetProperty("State");
        var storedStage = state.GetProperty("DisplayStage");
        RequireStage(state.GetProperty("DisplayCharacterId").GetString() == id &&
            (stage.HasValue ? storedStage.ValueKind == JsonValueKind.Number && storedStage.GetInt32() == stage : storedStage.ValueKind == JsonValueKind.Null),
            "The display stage was not persisted to SQLite.");
    }
    private static void RequireStage(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static void VerifyNarrowCollection(MainWindow window, string output, List<string> checks)
    {
        double previousMinimum = window.MinWidth, previousWidth = window.Width;
        try
        {
            window.MinWidth = 760; window.Width = 760;
            window.Navigate("Collection"); window.UpdateLayout();
            var grid = window.FindName("CollectionGrid") as System.Windows.Controls.Primitives.UniformGrid
                ?? throw new InvalidDataException("Collection grid is missing.");
            RequireStage(grid.Columns == 2, "A 760-DIP management window must arrange collection cards in two columns.");
            foreach (var card in grid.Children.OfType<CollectionCard>())
            foreach (var button in card.StageButtons)
            {
                var origin = button.TranslatePoint(new Point(), card);
                var label = new FormattedText(button.Content?.ToString() ?? "", System.Globalization.CultureInfo.CurrentUICulture,
                    button.FlowDirection, new Typeface(button.FontFamily, button.FontStyle, button.FontWeight, button.FontStretch),
                    button.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(button).PixelsPerDip);
                double textWidth = button.ActualWidth - button.Padding.Left - button.Padding.Right - button.BorderThickness.Left - button.BorderThickness.Right;
                RequireStage(button.ActualWidth > 0 && origin.X >= -0.5 && origin.X + button.ActualWidth <= card.ActualWidth + 0.5 &&
                    label.WidthIncludingTrailingWhitespace <= textWidth + 0.5,
                    $"A stage button is clipped in the narrow {card.CharacterId} collection card.");
            }
            Render(window, output, "collection-narrow", 1);
            window.PageScroll.ScrollToBottom(); Render(window, output, "collection-narrow-bottom", 1);
            checks.Add("At 760 DIP window width the collection uses two columns, with every stage button and its label fully inside its card.");
        }
        finally
        {
            window.MinWidth = previousMinimum; window.Width = previousWidth;
        }
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

    private sealed record SpriteSizeMeasurement(string Id, int Stage, bool Blink, int DipSize, double Scale,
        int PixelSize, int Left, int Top, int Right, int Bottom)
    {
        public int VisibleWidth => Right - Left + 1;
        public int VisibleHeight => Bottom - Top + 1;
        public double CenterX => (Left + Right) / 2.0;
    }

    private sealed record SpriteSizeSummary(int DipSize, double Scale, int Samples,
        int MinimumHeight, int MaximumHeight, int MinimumBottom, int MaximumBottom,
        int MaximumBlinkHeightDelta, int MaximumBlinkBottomDelta, double MaximumBlinkCenterDelta);

    private static void VerifyUniformSpriteSizes(string output, List<string> checks)
    {
        const int heightTolerance = 2, baselineTolerance = 1;
        const double centerTolerance = 1;
        var measurements = new List<SpriteSizeMeasurement>();
        var summaries = new List<SpriteSizeSummary>();
        var failures = new List<string>();
        foreach (var (size, scale) in new[] { (96, 1.0), (96, 1.5), (96, 2.0), (48, 1.0), (384, 1.0) })
        {
            var group = new List<SpriteSizeMeasurement>();
            int blinkHeightDelta = 0, blinkBottomDelta = 0;
            double blinkCenterDelta = 0;
            foreach (var definition in CharacterCatalog.All)
            for (int stage = 1; stage <= SpriteSheet.Stages; stage++)
            {
                var sprite = new SpriteView { Width = size, Height = size };
                sprite.ShowCharacter(definition.Id, stage);
                sprite.Measure(new Size(size, size)); sprite.Arrange(new Rect(0, 0, size, size)); sprite.UpdateLayout();
                SpriteSizeMeasurement? open = null;
                foreach (var blink in new[] { false, true })
                {
                    sprite.ShowExpression(blink); sprite.UpdateLayout();
                    var bitmap = RenderVisualBitmap(sprite, size, size, scale);
                    var measurement = MeasureSprite(bitmap, definition.Id, stage, blink, size, scale);
                    group.Add(measurement); measurements.Add(measurement);
                    if (measurement.Left <= 0 || measurement.Top <= 0 || measurement.Right >= bitmap.PixelWidth - 1 || measurement.Bottom >= bitmap.PixelHeight - 1)
                        failures.Add($"Clipped silhouette: {definition.Id} stage {stage}, blink={blink}, {size} DIP at {scale * 100:0}%.");
                    if (!blink) open = measurement;
                    else
                    {
                        int heightDelta = Math.Abs(measurement.VisibleHeight - open!.VisibleHeight);
                        int bottomDelta = Math.Abs(measurement.Bottom - open.Bottom);
                        double centerDelta = Math.Abs(measurement.CenterX - open.CenterX);
                        blinkHeightDelta = Math.Max(blinkHeightDelta, heightDelta);
                        blinkBottomDelta = Math.Max(blinkBottomDelta, bottomDelta);
                        blinkCenterDelta = Math.Max(blinkCenterDelta, centerDelta);
                        if (heightDelta > heightTolerance || bottomDelta > baselineTolerance || centerDelta > centerTolerance)
                            failures.Add($"Blink moved or resized {definition.Id} stage {stage} at {size} DIP / {scale * 100:0}%: height delta {heightDelta} px, bottom delta {bottomDelta} px, center delta {centerDelta:0.0} px.");
                    }
                }
            }
            var summary = new SpriteSizeSummary(size, scale, group.Count,
                group.Min(item => item.VisibleHeight), group.Max(item => item.VisibleHeight),
                group.Min(item => item.Bottom), group.Max(item => item.Bottom),
                blinkHeightDelta, blinkBottomDelta, blinkCenterDelta);
            summaries.Add(summary);
            if (summary.MaximumHeight - summary.MinimumHeight > heightTolerance)
                failures.Add($"Unequal character heights at {size} DIP / {scale * 100:0}%: {summary.MinimumHeight}–{summary.MaximumHeight} physical pixels.");
            if (summary.MaximumBottom - summary.MinimumBottom > baselineTolerance)
                failures.Add($"Unequal character baselines at {size} DIP / {scale * 100:0}%: y={summary.MinimumBottom}–{summary.MaximumBottom} physical pixels.");
        }
        var dpiMeasurements = measurements.Where(item => item.DipSize == 96).ToList();
        double minimumDipHeight = dpiMeasurements.Min(item => item.VisibleHeight / item.Scale);
        double maximumDipHeight = dpiMeasurements.Max(item => item.VisibleHeight / item.Scale);
        if (maximumDipHeight - minimumDipHeight > heightTolerance)
            failures.Add($"DPI changed the logical visible height: {minimumDipHeight:0.00}–{maximumDipHeight:0.00} DIP.");

        // A comparison within the current catalog cannot catch every character shrinking
        // together after a wide costume is added. Keep the released v1.1.1 pixel baseline.
        var defaultMeasurements = measurements.Where(item => item.DipSize == 96 && item.Scale == 1).ToList();
        int expectedExpressions = CharacterCatalog.All.Count * SpriteSheet.Stages * SpriteSheet.Columns;
        if (defaultMeasurements.Count != expectedExpressions)
            failures.Add($"The released size baseline is missing expressions: expected {expectedExpressions}, found {defaultMeasurements.Count}.");
        foreach (var item in defaultMeasurements)
            if (item.VisibleHeight != 69 || item.Bottom != 84)
                failures.Add($"Character differs from the released size: {item.Id} stage {item.Stage}, blink={item.Blink}, expected 69 px height / y84 baseline but rendered {item.VisibleHeight} px / y{item.Bottom}.");

        File.WriteAllText(Path.Combine(output, "size-checks.json"), JsonSerializer.Serialize(new
        {
            success = failures.Count == 0, alphaThreshold = 16,
            tolerancesInPhysicalPixels = new { height = heightTolerance, baseline = baselineTolerance, blinkCenter = centerTolerance },
            releasedSizeBaseline = new { referenceVersion = "1.1.1", dipSize = 96, scale = 1, visibleHeight = 69, bottom = 84, expectedExpressions, measuredExpressions = defaultMeasurements.Count },
            configurations = summaries, minimumDipHeight, maximumDipHeight, measurements, failures
        }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        if (failures.Count != 0)
            throw new InvalidDataException("Character size regression; see size-checks.json. " + string.Join(" ", failures.Take(5)));
        checks.Add($"All {CharacterCatalog.All.Count * SpriteSheet.Stages * SpriteSheet.Columns} actual sprite expressions retain uniform visible height, a common baseline, and stable blink alignment at 96 DIP with 100/150/200% DPI and at 48/384 DIP; geometry differs by at most 2 physical pixels for height and 1 for baseline or blink center.");
        checks.Add($"All {expectedExpressions} expressions, including newly added characters, match the exact 69-pixel visible height and y84 baseline released in v1.1.1 at 96 DIP / 100%.");
    }

    private static SpriteSizeMeasurement MeasureSprite(BitmapSource bitmap, string id, int stage, bool blink, int dipSize, double scale)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        int left = bitmap.PixelWidth, top = bitmap.PixelHeight, right = -1, bottom = -1;
        for (int y = 0, offset = 3; y < bitmap.PixelHeight; y++)
        for (int x = 0; x < bitmap.PixelWidth; x++, offset += 4)
        {
            if (pixels[offset] < 16) continue;
            left = Math.Min(left, x); right = Math.Max(right, x);
            top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        if (right < left) throw new InvalidDataException($"Rendered sprite is empty: {id}, stage {stage}, blink={blink}.");
        return new(id, stage, blink, dipSize, scale, bitmap.PixelWidth, left, top, right, bottom);
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
        var target = RenderVisualBitmap(visual, width, height, scale);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static RenderTargetBitmap RenderVisualBitmap(Visual visual, double width, double height, double scale)
    {
        var target = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        target.Render(visual);
        return target;
    }
}
