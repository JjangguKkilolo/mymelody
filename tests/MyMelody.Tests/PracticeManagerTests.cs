using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using MyMelody.Core;
using Xunit;

namespace MyMelody.Tests;

public sealed class PracticeManagerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MyMelodyTests", Guid.NewGuid().ToString("N"));
    private readonly FakeTime _clock = new(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));
    private PracticeManager Create() => new(_directory, _clock, TimeZoneInfo.Utc);

    [Fact]
    public void GrowthHasExactBoundariesAndCompletionDoesNotCarryForward()
    {
        using var manager = Create();
        var character = manager.Draw();
        Assert.Equal(1, character.Stage);
        Assert.False(manager.State.CanDraw);
        Assert.Throws<InvalidOperationException>(() => manager.Draw());
        manager.StartManual();
        Advance(manager, 12 * 3600 - 1);
        Assert.Equal(1, character.Stage);
        Advance(manager, 1);
        Assert.Equal(2, character.Stage);
        Advance(manager, 12 * 3600);
        Assert.Equal(3, character.Stage);
        Assert.False(character.IsComplete);
        Advance(manager, 12 * 3600);
        Assert.True(character.IsComplete);
        Assert.True(manager.State.CanDraw);
        Assert.NotNull(character.CompletedAt);
        Advance(manager, 3600);
        Assert.Equal(36 * 3600, character.PracticeSeconds);
        Assert.Equal(37 * 3600, manager.TotalSeconds);
        var next = manager.Draw();
        Assert.Equal(0, next.PracticeSeconds);
        Assert.NotEqual(character.Id, next.Id);
        Assert.False(manager.State.CanDraw);
        manager.SelectDisplay(character.Id);
        Advance(manager, 60);
        Assert.Equal(60, next.PracticeSeconds);
        Assert.Equal(character.Id, manager.State.DisplayCharacterId);
        Assert.Equal(next.Id, manager.State.GrowingCharacterId);
    }

    [Fact]
    public void EveryCatalogCharacterCanBeDrawnOnceAndThenCollectionEnds()
    {
        using var manager = Create();
        var ids = new HashSet<string>();
        manager.StartManual();
        for (int i = 0; i < CharacterCatalog.All.Count; i++)
        {
            var character = manager.Draw();
            Assert.True(ids.Add(character.Id));
            Advance(manager, GrowthRules.CompletionSeconds);
        }
        Assert.Equal(CharacterCatalog.All.Count, ids.Count);
        Assert.False(manager.State.CanDraw);
        Assert.Throws<InvalidOperationException>(() => manager.Draw());
        double previous = manager.TotalSeconds;
        Advance(manager, 100);
        Assert.Equal(previous + 100, manager.TotalSeconds);
        Assert.All(manager.State.Characters, c => Assert.Equal(GrowthRules.CompletionSeconds, c.PracticeSeconds));
    }

    [Fact]
    public void AutomaticGraceIsThirtySecondsAndFastNotesDoNotMultiplyTime()
    {
        using var manager = Create();
        manager.Draw();
        for (int i = 0; i < 100; i++) manager.NoteOn();
        Advance(manager, 10);
        Assert.Equal(10, manager.TotalSeconds);
        manager.NoteOn();
        Advance(manager, 200);
        Assert.Equal(40, manager.TotalSeconds);
        Assert.False(manager.IsPracticing);
        Assert.Equal(101, manager.Sessions.Sum(x => x.NoteCount));
        manager.NoteOn();
        Advance(manager, 5);
        Assert.Equal(45, manager.TotalSeconds);
        Assert.Equal(2, manager.Sessions.Count);
    }

    [Fact]
    public void PauseResumeAndSuspendRequireFreshInput()
    {
        using var manager = Create();
        manager.NoteOn(); Advance(manager, 5); manager.Pause();
        manager.NoteOn(); Advance(manager, 100);
        Assert.Equal(5, manager.TotalSeconds);
        manager.Resume(); Advance(manager, 100);
        Assert.False(manager.IsPracticing);
        manager.NoteOn(); Advance(manager, 5); manager.Suspend();
        Advance(manager, 300);
        Assert.Equal(10, manager.TotalSeconds);
        Assert.False(manager.IsPracticing);
        manager.NoteOn(); Advance(manager, 5);
        Assert.Equal(15, manager.TotalSeconds);
    }

    [Fact]
    public void ManualAndAutomaticAreExclusiveAndSuspendStopsManual()
    {
        using var manager = Create();
        manager.NoteOn(); Advance(manager, 10);
        manager.StartManual();
        Advance(manager, 20);
        manager.NoteOn();
        Advance(manager, 40);
        Assert.Equal(70, manager.TotalSeconds);
        Assert.Equal(60, manager.Sessions.Where(x => x.Mode == PracticeMode.Manual).Sum(x => x.PracticeSeconds));
        manager.StopManual(); Advance(manager, 100);
        Assert.Equal(70, manager.TotalSeconds);
        manager.StartManual(); Advance(manager, 10); manager.Suspend();
        Advance(manager, 1000);
        Assert.Equal(80, manager.TotalSeconds);
        Assert.False(manager.IsManual);
    }

    [Fact]
    public void MidnightSplitsSessionAndNotesBelongToActualDate()
    {
        _clock.SetUtc(new DateTimeOffset(2026, 9, 14, 23, 59, 50, TimeSpan.Zero));
        using var manager = Create();
        manager.NoteOn(); Advance(manager, 20); manager.NoteOn();
        Assert.Equal(2, manager.Sessions.Count);
        var days = manager.GetDailyStats();
        Assert.Equal(new DateOnly(2026, 9, 14), days[0].Date);
        Assert.Equal(10, days[0].PracticeSeconds);
        Assert.Equal(10, days[1].PracticeSeconds);
        Assert.Equal(1, days[0].NoteCount);
        Assert.Equal(1, days[1].NoteCount);
        Assert.Equal(10, manager.TodaySeconds);
        Assert.Equal(20, manager.WeekSeconds);
    }

    [Fact]
    public void SystemClockChangesCannotCreateExperience()
    {
        using var manager = Create();
        manager.StartManual(); Advance(manager, 5);
        _clock.SetUtc(_clock.GetUtcNow().AddDays(20));
        manager.Tick();
        Assert.Equal(5, manager.TotalSeconds);
        Advance(manager, 5);
        Assert.Equal(10, manager.TotalSeconds);
    }

    [Fact]
    public void RestartKeepsProgressSettingsAndDoesNotAccrueClosedTime()
    {
        string characterId;
        using (var manager = Create())
        {
            characterId = manager.Draw().Id;
            manager.Settings.CharacterSize = 200;
            manager.Settings.AlwaysOnTop = false;
            manager.StartManual(); Advance(manager, 500);
        }
        _clock.Advance(TimeSpan.FromHours(12));
        using var reloaded = Create();
        Assert.Equal(characterId, reloaded.State.GrowingCharacterId);
        Assert.Equal(500, reloaded.TotalSeconds);
        Assert.Equal(500, reloaded.State.GrowingCharacter!.PracticeSeconds);
        Assert.Equal(200, reloaded.Settings.CharacterSize);
        Assert.False(reloaded.Settings.AlwaysOnTop);
        Assert.False(reloaded.IsPracticing);
    }

    [Fact]
    public void BackupRestorePreservesAllStateAndCreatesSafetyCopy()
    {
        using var manager = Create();
        var character = manager.Draw();
        manager.Settings.CharacterSize = 144;
        manager.StartManual(); Advance(manager, 60);
        string backup = Path.Combine(_directory, "export.mymelody-backup.zip");
        manager.Backup(backup);
        Advance(manager, 100);
        manager.Settings.CharacterSize = 384;
        manager.Restore(backup);
        Assert.Equal(60, manager.TotalSeconds);
        Assert.Equal(60, manager.State.GrowingCharacter!.PracticeSeconds);
        Assert.Equal(character.Id, manager.State.GrowingCharacterId);
        Assert.Equal(144, manager.Settings.CharacterSize);
        Assert.False(manager.IsPracticing);
        Assert.Single(Directory.GetFiles(Path.Combine(_directory, "Backups"), "before-restore-*.zip"));
    }

    [Fact]
    public void CorruptBackupLeavesCurrentStateUntouched()
    {
        using var manager = Create();
        var character = manager.Draw();
        manager.StartManual(); Advance(manager, 60);
        string invalid = Path.Combine(_directory, "broken.zip");
        File.WriteAllText(invalid, "not a zip");
        Assert.Throws<InvalidDataException>(() => manager.Restore(invalid));
        Assert.Equal(character.Id, manager.State.GrowingCharacterId);
        Assert.Equal(60, manager.TotalSeconds);
        Assert.True(manager.IsManual);
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("schema")]
    [InlineData("catalog")]
    [InlineData("duration")]
    public void SemanticallyInvalidBackupsCannotOverwriteCurrentData(string mutation)
    {
        using var manager = Create();
        var id = manager.Draw().Id;
        manager.StartManual(); Advance(manager, 60);
        string backup = Path.Combine(_directory, "invalid.zip");
        manager.Backup(backup);
        MutateBackup(backup, mutation);
        Assert.Throws<InvalidDataException>(() => manager.Restore(backup));
        Assert.Equal(id, manager.State.GrowingCharacterId);
        Assert.Equal(60, manager.TotalSeconds);
    }

    [Fact]
    public void AutomaticBackupsRunOnceDailyAndKeepSeven()
    {
        using var manager = Create();
        manager.Draw();
        for (int i = 0; i < 10; i++) { _clock.Advance(TimeSpan.FromDays(1)); manager.Tick(); }
        string[] files = Directory.GetFiles(Path.Combine(_directory, "Backups"), "auto-*.zip");
        Assert.Equal(7, files.Length);
        var modified = files.ToDictionary(x => x, File.GetLastWriteTimeUtc);
        Advance(manager, 10);
        Assert.All(files, file => Assert.Equal(modified[file], File.GetLastWriteTimeUtc(file)));
    }

    [Fact]
    public void SettingsAreClampedBeforePersistence()
    {
        using (var manager = Create()) { manager.Settings.CharacterSize = -100; manager.Save(); }
        using var restored = Create();
        Assert.Equal(48, restored.Settings.CharacterSize);
        Assert.Throws<InvalidOperationException>(() => restored.SelectDisplay("piano"));
    }

    [Fact]
    public void AutosaveLimitsUncommittedPracticeToLastFiveSeconds()
    {
        using var manager = Create();
        manager.Draw(); manager.StartManual();
        Advance(manager, 5);
        Advance(manager, 4);
        using var connection = new SqliteConnection($"Data Source={Path.Combine(_directory, "practice.sqlite")};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT SUM(seconds) FROM practice_sessions;";
        Assert.Equal(5d, Convert.ToDouble(command.ExecuteScalar()));
        Assert.Equal(9d, manager.TotalSeconds);
    }

    [Fact]
    public void FailedGrowthSaveNeverDoubleCreditsTheInterval()
    {
        using var manager = Create();
        manager.Draw(); manager.StartManual();
        using var connection = new SqliteConnection($"Data Source={Path.Combine(_directory, "practice.sqlite")};Pooling=False");
        connection.Open();
        using (var command = connection.CreateCommand()) { command.CommandText = "BEGIN IMMEDIATE;"; command.ExecuteNonQuery(); }
        Advance(manager, GrowthRules.StageSeconds);
        Assert.NotNull(manager.LastStorageError);
        Assert.Equal(GrowthRules.StageSeconds, manager.State.GrowingCharacter!.PracticeSeconds);
        using (var command = connection.CreateCommand()) { command.CommandText = "ROLLBACK;"; command.ExecuteNonQuery(); }
        Advance(manager, 1);
        Assert.Null(manager.LastStorageError);
        Assert.Equal(GrowthRules.StageSeconds + 1, manager.State.GrowingCharacter.PracticeSeconds);
        Assert.Equal(GrowthRules.StageSeconds + 1, manager.TotalSeconds);
    }

    [Fact]
    public void StartupRecoveryRestoresValidBackupAndPreservesCorruptDatabaseAndSidecars()
    {
        string backup = Path.Combine(_directory, "known-good.zip");
        string characterId;
        using (var manager = Create())
        {
            characterId = manager.Draw().Id;
            manager.StartManual(); Advance(manager, 90);
            manager.Settings.CharacterLeft = 123;
            manager.Backup(backup);
        }
        var originalFiles = CreateCorruptDatabaseFiles();
        string preserved = PracticeManager.RecoverFromBackup(_directory, backup);
        foreach (var original in originalFiles)
            Assert.Equal(original.Value, File.ReadAllBytes(Path.Combine(preserved, Path.GetFileName(original.Key))));
        Assert.False(File.Exists(Path.Combine(_directory, "practice.sqlite-wal")));
        Assert.False(File.Exists(Path.Combine(_directory, "practice.sqlite-shm")));
        using var recovered = Create();
        Assert.Equal(characterId, recovered.State.GrowingCharacterId);
        Assert.Equal(90, recovered.TotalSeconds);
        Assert.Equal(123, recovered.Settings.CharacterLeft);
        Assert.False(recovered.IsPracticing);
    }

    [Fact]
    public void StartupRecoveryInvalidBackupLeavesOriginalFilesByteIdentical()
    {
        Directory.CreateDirectory(_directory);
        var originalFiles = CreateCorruptDatabaseFiles();
        string backup = Path.Combine(_directory, "invalid.zip");
        File.WriteAllText(backup, "corrupt archive");
        Assert.Throws<InvalidDataException>(() => PracticeManager.RecoverFromBackup(_directory, backup));
        foreach (var original in originalFiles) Assert.Equal(original.Value, File.ReadAllBytes(original.Key));
        Assert.False(Directory.Exists(Path.Combine(_directory, "Recovery")));
    }

    [Fact]
    public void StartupRecoveryFailedAtomicReplacementRestoresOriginalSidecars()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows sharing semantics are the deployment target.
        string backup = Path.Combine(_directory, "known-good.zip");
        using (var manager = Create()) { manager.Draw(); manager.Backup(backup); }
        var originalFiles = CreateCorruptDatabaseFiles();
        using var readLock = new FileStream(Path.Combine(_directory, "practice.sqlite"), FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Throws<IOException>(() => PracticeManager.RecoverFromBackup(_directory, backup));
        foreach (var original in originalFiles) Assert.Equal(original.Value, File.ReadAllBytes(original.Key));
        string preserved = Assert.Single(Directory.GetDirectories(Path.Combine(_directory, "Recovery")));
        foreach (var original in originalFiles)
            Assert.Equal(original.Value, File.ReadAllBytes(Path.Combine(preserved, Path.GetFileName(original.Key))));
    }

    [Fact]
    public void MalformedStoredDateTriggersRecoverableDataError()
    {
        using (var manager = Create()) { manager.StartManual(); Advance(manager, 5); }
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(_directory, "practice.sqlite")};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE practice_sessions SET start_utc='damaged timestamp';";
            command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidDataException>(() => Create());
    }

    private Dictionary<string, byte[]> CreateCorruptDatabaseFiles()
    {
        var files = new Dictionary<string, byte[]>();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            string path = Path.Combine(_directory, "practice.sqlite" + suffix);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("preserve these damaged bytes " + suffix);
            File.WriteAllBytes(path, bytes); files.Add(path, bytes);
        }
        return files;
    }

    private void MutateBackup(string path, string mutation)
    {
        byte[] bytes;
        JsonObject manifest;
        using (var input = ZipFile.OpenRead(path))
        {
            using var database = input.GetEntry("practice.sqlite")!.Open();
            using var memory = new MemoryStream(); database.CopyTo(memory); bytes = memory.ToArray();
            using var json = input.GetEntry("manifest.json")!.Open();
            manifest = JsonNode.Parse(json)!.AsObject();
        }
        if (mutation == "schema") manifest["SchemaVersion"] = 999;
        if (mutation is "catalog" or "duration")
        {
            string databasePath = Path.Combine(_directory, "mutate.sqlite");
            File.WriteAllBytes(databasePath, bytes);
            using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                connection.Open(); using var command = connection.CreateCommand();
                command.CommandText = "SELECT json FROM app_state WHERE id=1";
                var state = JsonNode.Parse((string)command.ExecuteScalar()!)!.AsObject();
                var character = state["State"]!["Characters"]![0]!;
                if (mutation == "catalog") character["Id"] = "unknown";
                else character["PracticeSeconds"] = -1;
                command.CommandText = "UPDATE app_state SET json=$json WHERE id=1";
                command.Parameters.AddWithValue("$json", state.ToJsonString()); command.ExecuteNonQuery();
            }
            bytes = File.ReadAllBytes(databasePath);
        }
        manifest["Sha256"] = mutation == "checksum" ? "WRONG" : Convert.ToHexString(SHA256.HashData(bytes));
        File.Delete(path);
        using var output = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var database = output.CreateEntry("practice.sqlite").Open()) database.Write(bytes);
        using var jsonOutput = output.CreateEntry("manifest.json").Open();
        System.Text.Json.JsonSerializer.Serialize(jsonOutput, manifest);
    }

    private void Advance(PracticeManager manager, double seconds) { _clock.Advance(TimeSpan.FromSeconds(seconds)); manager.Tick(); }
    public void Dispose()
    {
        // This path is a test-owned GUID directory, never a caller supplied location.
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class FakeTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _utc = start;
        private long _timestamp;
        public override DateTimeOffset GetUtcNow() => _utc;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan duration) { _utc += duration; _timestamp += duration.Ticks; }
        public void SetUtc(DateTimeOffset utc) => _utc = utc;
    }
}
