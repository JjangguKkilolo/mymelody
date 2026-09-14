namespace MyMelody.Core;

/// <summary>A display session containing only the credited time of its recorded intervals.</summary>
public sealed record PracticeSessionGroup(
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    DateOnly LocalDate,
    double PracticeSeconds,
    long NoteCount,
    PracticeMode Mode);

/// <summary>Groups short breaks for display without changing saved intervals or practice credit.</summary>
public static class PracticeSessionGrouping
{
    public static readonly TimeSpan SessionBreak = TimeSpan.FromMinutes(30);

    public static IReadOnlyList<PracticeSessionGroup> Group(IEnumerable<PracticeSession> intervals)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        var groups = new List<PracticeSessionGroup>();
        foreach (var interval in intervals.OrderBy(x => x.StartedAt))
        {
            var previous = groups.Count == 0 ? null : groups[^1];
            var gap = previous is null ? TimeSpan.Zero : interval.StartedAt - previous.EndedAt;
            if (previous is not null && previous.LocalDate == interval.LocalDate &&
                previous.Mode == interval.Mode && gap >= TimeSpan.Zero && gap < SessionBreak)
            {
                // The gap is a break, not practice: only add time and notes already recorded.
                groups[^1] = previous with
                {
                    EndedAt = interval.EndedAt,
                    PracticeSeconds = previous.PracticeSeconds + interval.PracticeSeconds,
                    NoteCount = checked(previous.NoteCount + interval.NoteCount)
                };
            }
            else
            {
                groups.Add(new PracticeSessionGroup(interval.StartedAt, interval.EndedAt,
                    interval.LocalDate, interval.PracticeSeconds, interval.NoteCount, interval.Mode));
            }
        }
        return groups.AsReadOnly();
    }
}
