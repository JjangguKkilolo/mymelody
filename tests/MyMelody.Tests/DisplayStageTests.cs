using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using MyMelody.Core;
using Xunit;

namespace MyMelody.Tests;

public sealed class DisplayStageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MyMelodyTests", Guid.NewGuid().ToString("N"));
    private readonly FakeTime _clock = new(new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero));
    private string DatabasePath => Path.Combine(_directory, "practice.sqlite");
    private PracticeManager Open() => new(_directory, _clock, TimeZoneInfo.Utc);

    [Fact]
    public void OnlyOwnedAndReachedStagesCanBeSelectedWithoutChangingGrowthOrRecords()
    {
        using var manager = Open();
        Assert.Equal(1, manager.State.EffectiveDisplayStage);
        var character = manager.Draw();
        manager.StartManual(); manager.NoteOn();
        string unownedId = CharacterCatalog.All.First(x => x.Id != character.Id).Id;
        for (int reached = 1; reached <= 3; reached++)
        {
            if (reached > 1) Advance(manager, GrowthRules.StageSeconds);
            string characters = JsonSerializer.Serialize(manager.State.Characters);
            string sessions = JsonSerializer.Serialize(manager.Sessions);
            bool canDraw = manager.State.CanDraw;
            for (int stage = 1; stage <= reached; stage++)
            {
                manager.SelectDisplay(character.Id, stage);
                Assert.Equal(stage, manager.State.DisplayStage);
                Assert.Equal(stage, manager.State.EffectiveDisplayStage);
                Assert.Equal(reached, character.Stage);
            }
            string selectedState = JsonSerializer.Serialize(manager.State);
            for (int locked = reached + 1; locked <= 3; locked++)
                Assert.Throws<InvalidOperationException>(() => manager.SelectDisplay(character.Id, locked));
            Assert.Throws<InvalidOperationException>(() => manager.SelectDisplay(unownedId, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.SelectDisplay(character.Id, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.SelectDisplay(character.Id, 4));
            Assert.Equal(selectedState, JsonSerializer.Serialize(manager.State));
            Assert.Equal(characters, JsonSerializer.Serialize(manager.State.Characters));
            Assert.Equal(sessions, JsonSerializer.Serialize(manager.Sessions));
            Assert.Equal(character.Id, manager.State.GrowingCharacterId);
            Assert.Equal(canDraw, manager.State.CanDraw);
        }
    }

    [Fact]
    public void FixedStagesStayFixedWhileAutomaticDisplayFollowsGrowthAndNewDrawResetsIt()
    {
        using var manager = Open();
        var first = manager.Draw();
        manager.SelectDisplay(first.Id, 1);
        manager.StartManual();
        Advance(manager, GrowthRules.StageSeconds);
        Assert.Equal(2, first.Stage);
        Assert.Equal(1, manager.State.EffectiveDisplayStage);
        manager.SelectDisplay(first.Id);
        Assert.Null(manager.State.DisplayStage);
        Assert.Equal(2, manager.State.EffectiveDisplayStage);
        Advance(manager, GrowthRules.StageSeconds);
        Assert.Equal(3, manager.State.EffectiveDisplayStage);
        manager.SelectDisplay(first.Id, 2);
        Advance(manager, GrowthRules.StageSeconds);
        Assert.True(first.IsComplete);
        Assert.True(manager.State.CanDraw);
        Assert.Equal(2, manager.State.EffectiveDisplayStage);

        var next = manager.Draw();
        Assert.Null(manager.State.DisplayStage);
        Assert.Equal(next.Id, manager.State.DisplayCharacterId);
        Assert.Equal(1, manager.State.EffectiveDisplayStage);
        manager.SelectDisplay(first.Id, 1);
        Advance(manager, 60);
        Assert.Equal(first.Id, manager.State.DisplayCharacterId);
        Assert.Equal(1, manager.State.EffectiveDisplayStage);
        Assert.Equal(next.Id, manager.State.GrowingCharacterId);
        Assert.Equal(60, next.PracticeSeconds);
        Assert.Equal(GrowthRules.CompletionSeconds, first.PracticeSeconds);
        Assert.Equal(GrowthRules.CompletionSeconds + 60, manager.TotalSeconds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(2)]
    public void SelectedStageSurvivesRestartAndBackupRestoreWithoutChangingPractice(int? stage)
    {
        string backup = Path.Combine(_directory, "selected-stage.zip");
        string state, sessions;
        using (var manager = Open())
        {
            var character = manager.Draw();
            manager.StartManual(); manager.NoteOn();
            Advance(manager, 25 * 3600);
            manager.StopManual();
            manager.SelectDisplay(character.Id, stage);
            manager.Backup(backup);
            state = JsonSerializer.Serialize(manager.State);
            sessions = JsonSerializer.Serialize(manager.Sessions);
        }
        _clock.Advance(TimeSpan.FromDays(1));
        using var restored = Open();
        Assert.Equal(state, JsonSerializer.Serialize(restored.State));
        Assert.Equal(sessions, JsonSerializer.Serialize(restored.Sessions));
        Assert.Equal(stage ?? 3, restored.State.EffectiveDisplayStage);
        Assert.False(restored.IsPracticing);
        restored.StartManual();
        Advance(restored, 11 * 3600);
        restored.Draw();
        restored.Restore(backup);
        Assert.Equal(state, JsonSerializer.Serialize(restored.State));
        Assert.Equal(sessions, JsonSerializer.Serialize(restored.Sessions));
        Assert.Equal(stage ?? 3, restored.State.EffectiveDisplayStage);
        Assert.Equal(25 * 3600, restored.TotalSeconds);
        Assert.False(restored.IsPracticing);
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("four")]
    [InlineData("future")]
    [InlineData("no-character")]
    public void InvalidDisplayStagesInBackupAndDatabaseAreRejectedWithoutReplacingCurrentData(string mutation)
    {
        string backup = Path.Combine(_directory, "invalid-stage.zip");
        using (var manager = Open())
        {
            var character = manager.Draw();
            manager.StartManual(); manager.NoteOn(); Advance(manager, 10);
            manager.SelectDisplay(character.Id, 1);
            manager.Backup(backup);
            string state = JsonSerializer.Serialize(manager.State);
            string sessions = JsonSerializer.Serialize(manager.Sessions);
            MutateBackup(backup, mutation);
            Assert.Throws<InvalidDataException>(() => manager.Restore(backup));
            Assert.Equal(state, JsonSerializer.Serialize(manager.State));
            Assert.Equal(sessions, JsonSerializer.Serialize(manager.Sessions));
            Assert.True(manager.IsManual);
            Assert.Empty(Directory.GetFiles(Path.Combine(_directory, "Backups"), "before-restore-*.zip"));
        }
        MutateDatabase(DatabasePath, mutation);
        byte[] original = File.ReadAllBytes(DatabasePath);
        Assert.Throws<InvalidDataException>(() => Open());
        Assert.Equal(original, File.ReadAllBytes(DatabasePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedDisplaySelectionOrDrawRollsBackBothDisplayFields(bool draw)
    {
        using var manager = Open();
        var previous = manager.Draw();
        manager.StartManual(); Advance(manager, GrowthRules.CompletionSeconds); manager.StopManual();
        string targetId = draw ? previous.Id : manager.Draw().Id;
        manager.SelectDisplay(previous.Id, 2);
        string state = JsonSerializer.Serialize(manager.State);
        string sessions = JsonSerializer.Serialize(manager.Sessions);
        using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        connection.Open();
        Execute(connection, "CREATE TRIGGER fail_state_save BEFORE UPDATE ON app_state BEGIN SELECT RAISE(ABORT, 'simulated save failure'); END;");
        try
        {
            if (draw) Assert.Throws<SqliteException>(() => manager.Draw());
            else Assert.Throws<SqliteException>(() => manager.SelectDisplay(targetId));
            Assert.Equal(state, JsonSerializer.Serialize(manager.State));
            Assert.Equal(sessions, JsonSerializer.Serialize(manager.Sessions));
            Execute(connection, "DROP TRIGGER fail_state_save;");
            using var reader = Open();
            Assert.Equal(state, JsonSerializer.Serialize(reader.State));
        }
        finally { Execute(connection, "DROP TRIGGER IF EXISTS fail_state_save;"); }
        if (draw) targetId = manager.Draw().Id;
        else manager.SelectDisplay(targetId);
        Assert.Equal(targetId, manager.State.DisplayCharacterId);
        Assert.Null(manager.State.DisplayStage);
        Assert.Equal(1, manager.State.EffectiveDisplayStage);
    }

    private void MutateBackup(string path, string mutation)
    {
        string candidate = Path.Combine(_directory, "candidate.sqlite");
        JsonObject manifest;
        using (var archive = ZipFile.OpenRead(path))
        {
            using (var input = archive.GetEntry("practice.sqlite")!.Open())
            using (var output = File.Create(candidate)) input.CopyTo(output);
            using var manifestStream = archive.GetEntry("manifest.json")!.Open();
            manifest = JsonNode.Parse(manifestStream)!.AsObject();
        }
        MutateDatabase(candidate, mutation);
        byte[] database = File.ReadAllBytes(candidate);
        manifest["Sha256"] = Convert.ToHexString(SHA256.HashData(database));
        File.Delete(path);
        using var rewritten = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var stream = rewritten.CreateEntry("practice.sqlite").Open()) stream.Write(database);
        using var json = rewritten.CreateEntry("manifest.json").Open();
        JsonSerializer.Serialize(json, manifest);
    }

    private static void MutateDatabase(string path, string mutation)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM app_state WHERE id=1;";
        var payload = JsonNode.Parse((string)command.ExecuteScalar()!)!.AsObject();
        var state = payload["State"]!.AsObject();
        state["DisplayStage"] = mutation switch { "zero" => 0, "four" => 4, "future" => 2, _ => 1 };
        if (mutation == "no-character")
        {
            state["Characters"] = new JsonArray();
            state["GrowingCharacterId"] = null;
            state["DisplayCharacterId"] = null;
        }
        command.CommandText = "UPDATE app_state SET json=$json WHERE id=1;";
        command.Parameters.AddWithValue("$json", payload.ToJsonString()); command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
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
