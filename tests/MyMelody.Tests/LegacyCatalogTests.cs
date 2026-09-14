using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MyMelody.Core;
using Xunit;

namespace MyMelody.Tests;

public sealed class LegacyCatalogTests : IDisposable
{
    // Freeze the shipped catalogs and storage format independently of the current catalog.
    private static readonly string[] Version102Ids =
        ["ribbon", "piano", "strawberry", "pajamas", "garden", "baking", "reading", "rain", "starry"];
    private static readonly string[] Version111Ids = [.. Version102Ids, "sheep", "egg"];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MyMelodyTests", Guid.NewGuid().ToString("N"));
    private readonly FakeTime _clock = new(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));
    private PracticeManager Open() => new(_directory, _clock, TimeZoneInfo.Utc);

    [Theory]
    [InlineData(9, "v1.0.2")]
    [InlineData(11, "v1.1.1")]
    public void CompletedLegacyCollectionKeepsItsHistoryAndUnlocksOnlyAddedCharacters(int legacyCount, string legacyVersion)
    {
        string[] legacyIds = GetLegacyIds(legacyCount);
        string[] addedIds = legacyCount == 9 ? ["sheep", "egg", "dinosaur"] : ["dinosaur"];
        Assert.Equal(legacyIds, CharacterCatalog.All.Take(legacyCount).Select(x => x.Id));
        Assert.Equal(addedIds, CharacterCatalog.All.Skip(legacyCount).Select(x => x.Id));
        string legacyBackup = WriteLegacyStore(legacyIds, legacyVersion, 36 * 3600);
        string characters, sessions, settings;
        using (var legacy = Open())
        {
            Assert.Equal(legacyCount * 36 * 3600, legacy.TotalSeconds);
            Assert.Equal(legacyIds[^1], legacy.State.GrowingCharacterId);
            Assert.Equal("strawberry", legacy.State.DisplayCharacterId);
            Assert.True(legacy.State.CanDraw);
            Assert.False(legacy.IsPracticing);
            characters = JsonSerializer.Serialize(legacy.State.Characters);
            sessions = JsonSerializer.Serialize(legacy.Sessions);
            settings = JsonSerializer.Serialize(legacy.Settings);
            legacy.Save();
        }

        using var manager = Open();
        Assert.Equal(characters, JsonSerializer.Serialize(manager.State.Characters));
        Assert.Equal(sessions, JsonSerializer.Serialize(manager.Sessions));
        Assert.Equal(settings, JsonSerializer.Serialize(manager.Settings));
        var originalSessionIds = manager.Sessions.Select(x => x.Id).ToHashSet();
        var newIds = new HashSet<string>();
        for (int i = 0; i < addedIds.Length; i++)
        {
            var added = manager.Draw();
            Assert.Contains(added.Id, addedIds);
            Assert.True(newIds.Add(added.Id));
            Assert.Equal(0, added.PracticeSeconds);
            Assert.Equal(1, added.Stage);
            Assert.False(manager.State.CanDraw);
            Assert.Throws<InvalidOperationException>(() => manager.Draw());
            manager.SelectDisplay("strawberry");
            Assert.Equal(added.Id, manager.State.GrowingCharacterId);
            manager.StartManual();
            Advance(manager, 12 * 3600);
            Assert.Equal(2, added.Stage);
            Advance(manager, 12 * 3600);
            Assert.Equal(3, added.Stage);
            Assert.False(added.IsComplete);
            Advance(manager, 12 * 3600);
            Assert.True(added.IsComplete);
            manager.StopManual();
        }
        Assert.Equal(12, manager.State.Characters.Count);
        Assert.False(manager.State.CanDraw);
        Assert.Throws<InvalidOperationException>(() => manager.Draw());
        Assert.Equal(characters, JsonSerializer.Serialize(manager.State.Characters.Take(legacyCount)));
        Assert.Equal(sessions, JsonSerializer.Serialize(manager.Sessions.Where(x => originalSessionIds.Contains(x.Id))));
        Assert.Equal(12 * 36 * 3600, manager.TotalSeconds);

        string expandedBackup = Path.Combine(_directory, "expanded.zip");
        manager.Backup(expandedBackup);
        string expandedState = JsonSerializer.Serialize(manager.State);
        string expandedSessions = JsonSerializer.Serialize(manager.Sessions);
        manager.Restore(legacyBackup);
        Assert.Equal(characters, JsonSerializer.Serialize(manager.State.Characters));
        Assert.Equal(sessions, JsonSerializer.Serialize(manager.Sessions));
        Assert.Equal(settings, JsonSerializer.Serialize(manager.Settings));
        Assert.Equal("strawberry", manager.State.DisplayCharacterId);
        Assert.Equal(legacyIds[^1], manager.State.GrowingCharacterId);
        Assert.True(manager.State.CanDraw);

        manager.Restore(expandedBackup);
        Assert.Equal(expandedState, JsonSerializer.Serialize(manager.State));
        Assert.Equal(expandedSessions, JsonSerializer.Serialize(manager.Sessions));
        Assert.Equal(settings, JsonSerializer.Serialize(manager.Settings));
        Assert.False(manager.State.CanDraw);
        manager.StartManual();
        Advance(manager, 30);
        Assert.Equal(12 * 36 * 3600 + 30, manager.TotalSeconds);
        Assert.All(manager.State.Characters, c => Assert.Equal(36 * 3600, c.PracticeSeconds));
        Assert.Equal(expandedState, JsonSerializer.Serialize(manager.State));
    }

    [Theory]
    [InlineData(9, "v1.0.2")]
    [InlineData(11, "v1.1.1")]
    public void UnfinishedLegacyCharacterMustFinishBeforeAnyAddedCharacterCanBeDrawn(int legacyCount, string legacyVersion)
    {
        string[] legacyIds = GetLegacyIds(legacyCount);
        string[] addedIds = legacyCount == 9 ? ["sheep", "egg", "dinosaur"] : ["dinosaur"];
        string legacyBackup = WriteLegacyStore(legacyIds, legacyVersion, 13 * 3600);
        string state, sessions, settings;
        using (var legacy = Open())
        {
            state = JsonSerializer.Serialize(legacy.State);
            sessions = JsonSerializer.Serialize(legacy.Sessions);
            settings = JsonSerializer.Serialize(legacy.Settings);
            legacy.Save();
        }
        using var manager = Open();
        Assert.Equal(state, JsonSerializer.Serialize(manager.State));
        Assert.Equal(sessions, JsonSerializer.Serialize(manager.Sessions));
        Assert.Equal(settings, JsonSerializer.Serialize(manager.Settings));
        manager.StartManual();
        Advance(manager, 5);
        manager.Restore(legacyBackup);
        Assert.Equal(state, JsonSerializer.Serialize(manager.State));
        Assert.Equal(sessions, JsonSerializer.Serialize(manager.Sessions));
        Assert.Equal(settings, JsonSerializer.Serialize(manager.Settings));
        Assert.False(manager.IsPracticing);
        Assert.Equal((legacyCount - 1) * 36 * 3600 + 13 * 3600, manager.TotalSeconds);
        var growing = manager.State.GrowingCharacter!;
        Assert.Equal(legacyIds[^1], growing.Id);
        Assert.Equal(13 * 3600, growing.PracticeSeconds);
        Assert.Equal(2, growing.Stage);
        Assert.False(manager.State.CanDraw);
        Assert.Throws<InvalidOperationException>(() => manager.Draw());
        manager.StartManual();
        Advance(manager, 23 * 3600 - 1);
        Assert.False(manager.State.CanDraw);
        Advance(manager, 1);
        Assert.True(manager.State.CanDraw);
        Assert.True(growing.IsComplete);
        Assert.NotNull(growing.CompletedAt);
        Advance(manager, 60);
        var next = manager.Draw();
        Assert.Contains(next.Id, addedIds);
        Assert.Equal(0, next.PracticeSeconds);
        Assert.Equal(36 * 3600, growing.PracticeSeconds);
        Assert.Equal(legacyCount * 36 * 3600 + 60, manager.TotalSeconds);
    }

    private static string[] GetLegacyIds(int count) => count switch
    {
        9 => Version102Ids,
        11 => Version111Ids,
        _ => throw new ArgumentOutOfRangeException(nameof(count))
    };

    private string WriteLegacyStore(string[] legacyIds, string legacyVersion, double lastPracticeSeconds)
    {
        Directory.CreateDirectory(_directory);
        string databasePath = Path.Combine(_directory, "practice.sqlite");
        var start = new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.Zero);
        var characters = legacyIds.Select((id, index) => new
        {
            Id = id, PracticeSeconds = index == legacyIds.Length - 1 ? lastPracticeSeconds : 36 * 3600,
            AcquiredAt = start.AddDays(index * 3),
            CompletedAt = index == legacyIds.Length - 1 && lastPracticeSeconds < 36 * 3600
                ? (DateTimeOffset?)null : start.AddDays(index * 3 + 2).AddHours(12)
        }).ToArray();
        using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA application_id=1297698893;
                PRAGMA user_version=1;
                CREATE TABLE app_state(id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL);
                CREATE TABLE practice_sessions(id TEXT PRIMARY KEY, start_utc TEXT NOT NULL, end_utc TEXT NOT NULL,
                    local_date TEXT NOT NULL, seconds REAL NOT NULL CHECK(seconds>=0),
                    note_count INTEGER NOT NULL CHECK(note_count>=0), mode INTEGER NOT NULL CHECK(mode IN (0,1)));
                CREATE INDEX idx_practice_date ON practice_sessions(local_date);
                INSERT INTO app_state(id,json) VALUES(1,$json);
                """;
            command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(new
            {
                State = new { Characters = characters, GrowingCharacterId = legacyIds[^1], DisplayCharacterId = "strawberry" },
                Settings = new
                {
                    MidiDeviceId = "legacy-keyboard", CharacterSize = 144, CharacterLeft = 80, CharacterTop = 120,
                    AlwaysOnTop = false, CharacterVisible = true, AutoStart = true, SoundEnabled = false,
                    AutoCheckUpdates = false, LastUpdateCheck = start.AddDays(30)
                }
            }));
            command.ExecuteNonQuery();
            for (int i = 0; i < characters.Length; i++)
            {
                double remaining = characters[i].PracticeSeconds;
                for (int day = 0; remaining > 0; day++)
                {
                    double seconds = Math.Min(12 * 3600, remaining);
                    var sessionStart = start.AddDays(i * 3 + day);
                    command.Parameters.Clear();
                    command.CommandText = "INSERT INTO practice_sessions VALUES($id,$start,$end,$date,$seconds,$notes,$mode);";
                    command.Parameters.AddWithValue("$id", (i * 3 + day + 1).ToString("x32"));
                    command.Parameters.AddWithValue("$start", sessionStart.ToString("O"));
                    command.Parameters.AddWithValue("$end", sessionStart.AddSeconds(seconds).ToString("O"));
                    command.Parameters.AddWithValue("$date", sessionStart.ToString("yyyy-MM-dd"));
                    command.Parameters.AddWithValue("$seconds", seconds);
                    command.Parameters.AddWithValue("$notes", (i + 1) * (day + 1) * 25);
                    command.Parameters.AddWithValue("$mode", day % 2);
                    command.ExecuteNonQuery();
                    remaining -= seconds;
                }
            }
        }
        string backupPath = Path.Combine(_directory, $"{legacyVersion}-legacy.zip");
        byte[] database = File.ReadAllBytes(databasePath);
        using var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create);
        using (var stream = archive.CreateEntry("practice.sqlite").Open()) stream.Write(database);
        using var manifest = archive.CreateEntry("manifest.json").Open();
        JsonSerializer.Serialize(manifest, new
        {
            Application = "MyMelodyPractice", SchemaVersion = 1, CreatedAt = start.AddDays(legacyIds.Length * 3 + 1),
            Sha256 = Convert.ToHexString(SHA256.HashData(database))
        });
        return backupPath;
    }

    private void Advance(PracticeManager manager, double seconds)
    {
        _clock.Advance(TimeSpan.FromSeconds(seconds)); manager.Tick();
    }

    public void Dispose()
    {
        // Only this test's generated GUID directory is removed.
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
    }
}
