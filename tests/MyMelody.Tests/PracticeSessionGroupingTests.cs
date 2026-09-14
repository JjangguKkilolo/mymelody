using System.Text.Json;
using MyMelody.Core;
using Xunit;

namespace MyMelody.Tests;

public sealed class PracticeSessionGroupingTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MyMelodyTests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(1799.999, 1)]
    [InlineData(1800, 2)]
    [InlineData(1800.001, 2)]
    public void ThirtyMinutesOfUncreditedRestStartsANewDisplaySession(double gapSeconds, int expectedCount)
    {
        var intervals = new[] { Interval(0, 23, 4), Interval(23 + gapSeconds, 30, 7) };
        var groups = PracticeSessionGrouping.Group(intervals);
        Assert.Equal(expectedCount, groups.Count);
        Assert.Equal(53, groups.Sum(x => x.PracticeSeconds));
        Assert.Equal(11, groups.Sum(x => x.NoteCount));
    }

    [Fact]
    public void ExistingShortIntervalsBecomeOneDisplaySessionWithoutChangingTheRawRecords()
    {
        var intervals = new[] { Interval(0, 23, 4), Interval(143, 30, 7) };
        var original = JsonSerializer.Serialize(intervals);
        var group = Assert.Single(PracticeSessionGrouping.Group(intervals));
        Assert.Equal(Start, group.StartedAt);
        Assert.Equal(Start.AddSeconds(173), group.EndedAt);
        Assert.Equal(53, group.PracticeSeconds);
        Assert.Equal(11, group.NoteCount);
        Assert.Equal(original, JsonSerializer.Serialize(intervals));

        // Returned values are snapshots, not mutable aliases of saved intervals.
        intervals[0].PracticeSeconds = 999;
        intervals[0].StartedAt = Start.AddDays(1);
        Assert.Equal(53, group.PracticeSeconds);
        Assert.Equal(Start, group.StartedAt);
    }

    [Fact]
    public void EachShortBreakCanExtendASessionPastThirtyMinutesWithoutCreditingTheBreaks()
    {
        var intervals = new[] { Interval(0, 23, 1), Interval(1223, 30, 2), Interval(2453, 40, 3) };
        var group = Assert.Single(PracticeSessionGrouping.Group(intervals));
        Assert.True(group.EndedAt - group.StartedAt > PracticeSessionGrouping.SessionBreak);
        Assert.Equal(93, group.PracticeSeconds);
        Assert.Equal(6, group.NoteCount);
    }

    [Fact]
    public void ModeChangesAreBarriersEvenWhenAutomaticRecordingReturnsImmediately()
    {
        var intervals = new[]
        {
            Interval(0, 10, 1),
            Interval(20, 10, 2, PracticeMode.Manual),
            Interval(40, 10, 3)
        };
        var groups = PracticeSessionGrouping.Group(intervals);
        Assert.Equal(3, groups.Count);
        Assert.Equal(new[] { PracticeMode.Automatic, PracticeMode.Manual, PracticeMode.Automatic }, groups.Select(x => x.Mode));
        Assert.Equal(intervals.Sum(x => x.PracticeSeconds), groups.Sum(x => x.PracticeSeconds));
        Assert.Equal(intervals.Sum(x => x.NoteCount), groups.Sum(x => x.NoteCount));
    }

    [Fact]
    public void ShortManualBreaksAlsoGroupWithoutCountingStoppedTimerTime()
    {
        var group = Assert.Single(PracticeSessionGrouping.Group(new[]
        {
            Interval(0, 23, 0, PracticeMode.Manual),
            Interval(120, 30, 0, PracticeMode.Manual)
        }));
        Assert.Equal(PracticeMode.Manual, group.Mode);
        Assert.Equal(53, group.PracticeSeconds);
    }

    [Fact]
    public void StoredLocalDateKeepsMidnightSplitIndependentOfUtcDate()
    {
        // Korean midnight occurs within a single UTC date. Keep the recorded day boundary.
        var beforeMidnight = Interval(3 * 3600 - 10, 10, 1);
        var afterMidnight = Interval(3 * 3600, 10, 2);
        beforeMidnight.LocalDate = new DateOnly(2026, 9, 14);
        afterMidnight.LocalDate = new DateOnly(2026, 9, 15);
        var groups = PracticeSessionGrouping.Group(new[] { beforeMidnight, afterMidnight });
        Assert.Equal(2, groups.Count);
        Assert.Equal(beforeMidnight.LocalDate, groups[0].LocalDate);
        Assert.Equal(afterMidnight.LocalDate, groups[1].LocalDate);
        Assert.Equal(20, groups.Sum(x => x.PracticeSeconds));
    }

    [Fact]
    public void OverlappingClockTimesRemainSeparateAndCannotSubtractRecordedTime()
    {
        var groups = PracticeSessionGrouping.Group(new[] { Interval(0, 30, 1), Interval(10, 30, 2) });
        Assert.Equal(2, groups.Count);
        Assert.Equal(60, groups.Sum(x => x.PracticeSeconds));
        Assert.Equal(3, groups.Sum(x => x.NoteCount));
    }

    [Fact]
    public void UnsortedInputIsGroupedChronologically()
    {
        var group = Assert.Single(PracticeSessionGrouping.Group(new[] { Interval(120, 30, 2), Interval(0, 23, 1) }));
        Assert.Equal(Start, group.StartedAt);
        Assert.Equal(Start.AddSeconds(150), group.EndedAt);
        Assert.Equal(53, group.PracticeSeconds);
    }

    [Fact]
    public void EmptyAndSingleIntervalsKeepTheirMeaning()
    {
        Assert.Empty(PracticeSessionGrouping.Group(Array.Empty<PracticeSession>()));
        var group = Assert.Single(PracticeSessionGrouping.Group(new[] { Interval(0, 0, 1) }));
        Assert.Equal(0, group.PracticeSeconds);
        Assert.Equal(1, group.NoteCount);
        Assert.Equal(Start, group.StartedAt);
        Assert.Equal(Start, group.EndedAt);
    }

    [Fact]
    public void SaveReloadAndBackupRestoreKeepRawIntervalsAndGrowthWhileGroupingShortRestarts()
    {
        var clock = new FakeTime(Start);
        var backup = Path.Combine(_directory, "before-grouping.mymelody-backup.zip");
        string rawSnapshot;
        PracticeSessionGroup[] groupedSnapshot;
        using (var manager = new PracticeManager(_directory, clock, TimeZoneInfo.Utc))
        {
            manager.Draw();
            manager.NoteOn(); Advance(manager, clock, 23); manager.Suspend();
            Advance(manager, clock, 120);
            manager.NoteOn(); Advance(manager, clock, 30);
            Assert.Equal(2, manager.Sessions.Count);
            Assert.Equal(53, manager.TotalSeconds);
            Assert.Equal(53, manager.State.GrowingCharacter!.PracticeSeconds);
            rawSnapshot = JsonSerializer.Serialize(manager.Sessions);
            groupedSnapshot = manager.GetPracticeSessions().ToArray();
            Assert.Single(groupedSnapshot);
            Assert.Equal(rawSnapshot, JsonSerializer.Serialize(manager.Sessions));
            manager.Backup(backup);
        }

        clock.Advance(TimeSpan.FromMinutes(5));
        using var reloaded = new PracticeManager(_directory, clock, TimeZoneInfo.Utc);
        Assert.False(reloaded.IsPracticing);
        Assert.Equal(53, reloaded.TotalSeconds);
        Assert.Equal(rawSnapshot, JsonSerializer.Serialize(reloaded.Sessions));
        Assert.Equal(groupedSnapshot, reloaded.GetPracticeSessions().ToArray());

        reloaded.NoteOn(); Advance(reloaded, clock, 10); reloaded.Suspend();
        Assert.Equal(3, reloaded.Sessions.Count);
        Assert.Equal(63, Assert.Single(reloaded.GetPracticeSessions()).PracticeSeconds);
        Assert.Equal(63, reloaded.State.GrowingCharacter!.PracticeSeconds);

        reloaded.Restore(backup);
        Assert.False(reloaded.IsPracticing);
        Assert.Equal(53, reloaded.TotalSeconds);
        Assert.Equal(53, reloaded.State.GrowingCharacter!.PracticeSeconds);
        Assert.Equal(rawSnapshot, JsonSerializer.Serialize(reloaded.Sessions));
        Assert.Equal(groupedSnapshot, reloaded.GetPracticeSessions().ToArray());
    }

    private static PracticeSession Interval(double offset, double seconds, long notes, PracticeMode mode = PracticeMode.Automatic)
        => new()
        {
            StartedAt = Start.AddSeconds(offset), EndedAt = Start.AddSeconds(offset + seconds),
            LocalDate = DateOnly.FromDateTime(Start.UtcDateTime), PracticeSeconds = seconds, NoteCount = notes, Mode = mode
        };

    private static void Advance(PracticeManager manager, FakeTime clock, double seconds)
    {
        clock.Advance(TimeSpan.FromSeconds(seconds)); manager.Tick();
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
