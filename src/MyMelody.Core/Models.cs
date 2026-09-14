using System.Text.Json.Serialization;

namespace MyMelody.Core;

public sealed record CharacterDefinition(string Id, string Name, string Emoji, string Color)
{
    public string Description => Id switch
    {
        "ribbon" => "리본처럼 포근한 첫 만남", "piano" => "작은 피아노로 전하는 응원",
        "strawberry" => "딸기처럼 달콤한 연습 시간", "pajamas" => "잠들기 전의 잔잔한 멜로디",
        "garden" => "매일 조금씩 피어나는 마음", "baking" => "차곡차곡 구워지는 자신감",
        "reading" => "한 페이지씩 쌓이는 이야기", "rain" => "빗소리와 함께하는 작은 연주",
        "starry" => "별빛 아래에서 만나는 단짝", "sheep" => "몽실몽실 포근하게 감싸는 응원",
        "egg" => "작은 껍질에서 피어나는 새 멜로디", "dinosaur" => "작은 발걸음으로 함께하는 음악 탐험",
        _ => "함께 연습하며 자라는 단짝"
    };
}

public static class CharacterCatalog
{
    public static IReadOnlyList<CharacterDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        new CharacterDefinition("ribbon", "리본", "🎀", "#F6B7CC"),
        new CharacterDefinition("piano", "피아노", "🎹", "#DACBF0"),
        new CharacterDefinition("strawberry", "딸기", "🍓", "#F3A5AD"),
        new CharacterDefinition("pajamas", "잠옷", "☁", "#D9D0EF"),
        new CharacterDefinition("garden", "정원", "🌷", "#C8DEC7"),
        new CharacterDefinition("baking", "베이킹", "🧁", "#F2D1B3"),
        new CharacterDefinition("reading", "독서", "📖", "#D6C2B4"),
        new CharacterDefinition("rain", "비 오는 날", "☂", "#B9D8E7"),
        new CharacterDefinition("starry", "별밤", "✦", "#C1C5E8"),
        new CharacterDefinition("sheep", "양", "🐑", "#E7DBEC"),
        new CharacterDefinition("egg", "계란", "🥚", "#F5DFAD"),
        new CharacterDefinition("dinosaur", "공룡", "🦕", "#F2BDD0")
    });
    public static CharacterDefinition Get(string id) => All.FirstOrDefault(x => x.Id == id)
        ?? throw new ArgumentException("알 수 없는 캐릭터입니다.", nameof(id));
}

public static class GrowthRules
{
    public const double StageSeconds = 12 * 60 * 60;
    public const double CompletionSeconds = StageSeconds * 3;
    public const double GraceSeconds = 30;
}

public sealed class CharacterProgress
{
    public string Id { get; set; } = "";
    public double PracticeSeconds { get; set; }
    public DateTimeOffset AcquiredAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    [JsonIgnore] public int Stage => Math.Min(3, 1 + (int)(PracticeSeconds / GrowthRules.StageSeconds));
    [JsonIgnore] public bool IsComplete => PracticeSeconds >= GrowthRules.CompletionSeconds;
    [JsonIgnore] public double ProgressToNextStage => IsComplete ? 1 : Math.Clamp((PracticeSeconds - (Stage - 1) * GrowthRules.StageSeconds) / GrowthRules.StageSeconds, 0, 1);
    [JsonIgnore] public double SecondsToNextStage => IsComplete ? 0 : Stage * GrowthRules.StageSeconds - PracticeSeconds;
    [JsonIgnore] public CharacterDefinition Definition => CharacterCatalog.Get(Id);
}

public sealed class AppState
{
    public List<CharacterProgress> Characters { get; set; } = [];
    public string? GrowingCharacterId { get; set; }
    public string? DisplayCharacterId { get; set; }
    [JsonIgnore] public CharacterProgress? GrowingCharacter => Characters.FirstOrDefault(x => x.Id == GrowingCharacterId);
    [JsonIgnore] public CharacterProgress? DisplayCharacter => Characters.FirstOrDefault(x => x.Id == DisplayCharacterId);
    [JsonIgnore] public bool CanDraw => Characters.Count < CharacterCatalog.All.Count && (GrowingCharacter is null || GrowingCharacter.IsComplete);
}

public sealed class AppSettings
{
    public string? MidiDeviceId { get; set; }
    public double CharacterSize { get; set; } = 96;
    public double? CharacterLeft { get; set; }
    public double? CharacterTop { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public bool CharacterVisible { get; set; } = true;
    public bool AutoStart { get; set; }
    public bool SoundEnabled { get; set; }
    public bool AutoCheckUpdates { get; set; } = true;
    public DateTimeOffset? LastUpdateCheck { get; set; }

    internal void Normalize()
    {
        CharacterSize = double.IsFinite(CharacterSize) ? Math.Clamp(CharacterSize, 48, 384) : 96;
        if (CharacterLeft is double left && !double.IsFinite(left)) CharacterLeft = null;
        if (CharacterTop is double top && !double.IsFinite(top)) CharacterTop = null;
        MidiDeviceId = string.IsNullOrWhiteSpace(MidiDeviceId) ? null : MidiDeviceId;
    }
}

public enum PracticeMode { Automatic, Manual }

public sealed class PracticeSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public DateOnly LocalDate { get; set; }
    public double PracticeSeconds { get; set; }
    public long NoteCount { get; set; }
    public PracticeMode Mode { get; set; }
}

public sealed record DailyPracticeStat(DateOnly Date, double PracticeSeconds, long NoteCount);

internal sealed class StoredState
{
    public AppState State { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}
