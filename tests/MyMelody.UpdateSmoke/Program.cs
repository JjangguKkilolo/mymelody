using System.Runtime.InteropServices;
using System.Text.Json;
using MyMelody.App.Services;
using MyMelody.Core;
using Velopack;

namespace MyMelody.UpdateSmoke;

internal static class Program
{
    public static int Main(string[] args)
    {
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        var root = Environment.GetEnvironmentVariable("MYMELODY_SMOKE_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return 0; // Package verification and accidental launches do no work.
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        try { return RunAsync(root).GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(root, "failure.txt"), ex.ToString());
            return 1;
        }
    }

    private static async Task<int> RunAsync(string root)
    {
        string feed = Environment.GetEnvironmentVariable("MYMELODY_SMOKE_FEED") ?? throw new InvalidOperationException("Missing feed");
        var backend = new VelopackUpdateBackend(new UpdateManager(feed,
            new UpdateOptions { ExplicitChannel = "win", AllowVersionDowngrade = false }));
        var updater = new UpdateService(backend);
        Require(backend.IsInstalled, "Velopack must recognize real installed/portable layout.");
        string dataPath = Path.Combine(root, "data");
        string backup = Path.Combine(root, "before-update.mymelody-backup.zip");
        var clock = new TestClock();
        using var practice = new PracticeManager(dataPath, clock, TimeZoneInfo.Utc);
        File.AppendAllText(Path.Combine(root, "launches.txt"), $"{DateTimeOffset.UtcNow:O} {backend.CurrentVersion} {Environment.ProcessPath}\n");

        if (backend.CurrentVersion == "1.0.0")
        {
            Require(practice.State.Characters.Count == 0, "Each smoke run needs a fresh isolated data directory.");
            practice.Draw();
            practice.Settings.CharacterSize = 144;
            practice.Settings.AlwaysOnTop = false;
            practice.StartManual();
            clock.Advance(TimeSpan.FromHours(13));
            practice.Tick();
            practice.NoteOn();
            practice.StopManual();
            practice.Save();
            Require(practice.TotalSeconds == 13 * 3600, "Initial 13-hour record.");
            Require(practice.State.GrowingCharacter?.Stage == 2, "Initial stage 2.");
            var before = Snapshot(practice);
            File.WriteAllText(Path.Combine(root, "before.json"), JsonSerializer.Serialize(before));

            await updater.CheckAsync();
            Require(updater.State == UpdateState.Available && updater.AvailableVersion == "1.0.1", updater.StatusText);
            Require(!updater.CanApply, "Checking must not download or prepare an update.");
            // This harness is an explicit test action. Production UI invokes these only on the user's button.
            Require(await updater.DownloadAsync(), updater.StatusText);
            Require(updater.CanApply, "Package must pass real Velopack hash verification.");
            practice.Suspend();
            practice.Save();
            practice.Backup(backup);
            practice.Dispose();
            updater.ApplyAndRestart();
            throw new InvalidOperationException("Real update should terminate the original process.");
        }

        Require(backend.CurrentVersion == "1.0.1", "Unexpected restarted version: " + backend.CurrentVersion);
        var beforeJson = File.ReadAllText(Path.Combine(root, "before.json"));
        var afterJson = JsonSerializer.Serialize(Snapshot(practice));
        Require(beforeJson == afterJson, "Practice/character/settings snapshot changed across binary update.");
        Require(!practice.IsPracticing, "No offline practice credit on restart.");
        Require(File.Exists(backup), "Update must create a backup before applying.");
        using (var restored = new PracticeManager(Path.Combine(root, "restored"), clock, TimeZoneInfo.Utc))
        {
            restored.Restore(backup);
            Require(JsonSerializer.Serialize(Snapshot(restored)) == beforeJson, "Pre-update backup must restore the same records.");
        }
        string runtime = RuntimeEnvironment.GetRuntimeDirectory();
        Require(Path.GetFullPath(runtime).StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase),
            "Self-contained app must use its bundled runtime, not an installed SDK/shared runtime.");
        await updater.CheckAsync();
        Require(updater.State == UpdateState.UpToDate, "After update there should be no further update.");
        File.WriteAllText(Path.Combine(root, "success.json"), JsonSerializer.Serialize(new
        {
            Passed = true,
            FromVersion = "1.0.0", ToVersion = backend.CurrentVersion,
            PracticeSeconds = practice.TotalSeconds,
            Stage = practice.State.GrowingCharacter?.Stage,
            NoteCount = practice.Sessions.Sum(x => x.NoteCount),
            RecordSettingsAndCharacterPreserved = true,
            BackupRestoreVerified = true,
            BundledRuntimeDirectory = runtime,
            AppDirectory = AppContext.BaseDirectory,
            VerifiedAt = DateTimeOffset.UtcNow
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static object Snapshot(PracticeManager manager) => new
    {
        manager.State, manager.Settings.CharacterSize, manager.Settings.AlwaysOnTop,
        Sessions = manager.Sessions.Select(s => new { s.Id, s.StartedAt, s.EndedAt, s.LocalDate, s.PracticeSeconds, s.NoteCount, s.Mode }).ToArray()
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class TestClock : TimeProvider
    {
        private readonly DateTimeOffset _start = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => _start.AddTicks(_ticks);
        public void Advance(TimeSpan duration) => _ticks += duration.Ticks;
    }
}
