using System.Security.Cryptography;

namespace MyMelody.Core;

/// <summary>Single-threaded domain coordinator. Call on the UI dispatcher, including MIDI callbacks.</summary>
public sealed class PracticeManager : IDisposable
{
    /// <summary>
    /// Restores a fully validated backup before the manager can open a damaged database.
    /// Call only while holding the application's single-instance lock and with all database
    /// handles closed. Returns the directory preserving the previous database and sidecars.
    /// </summary>
    public static string RecoverFromBackup(string dataDirectory, string sourceZip)
        => PracticeStore.RecoverFromBackup(dataDirectory, sourceZip);

    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _zone;
    private readonly PracticeStore _store;
    private List<PracticeSession> _sessions;
    private readonly HashSet<string> _dirtySessions = new(StringComparer.Ordinal);
    private PracticeSession? _currentSession;
    private bool _automatic;
    private long _cursorTimestamp;
    private DateTimeOffset _cursorUtc;
    private long _lastNoteTimestamp;
    private long _lastSaveTimestamp;
    private DateOnly? _lastAutomaticBackupDate;
    private bool _growthSaveRequired;
    private bool _disposed;

    public string DataDirectory { get; }
    public AppState State { get; private set; }
    public AppSettings Settings { get; private set; }
    public IReadOnlyList<PracticeSession> Sessions => _sessions.AsReadOnly();
    public bool IsPracticing => !IsPaused && (IsManual || _automatic);
    public bool IsPaused { get; private set; }
    public bool IsManual { get; private set; }
    public string? LastStorageError { get; private set; }
    public double TotalSeconds => _sessions.Sum(x => x.PracticeSeconds);
    public double TodaySeconds => _sessions.Where(x => x.LocalDate == LocalDate(_time.GetUtcNow())).Sum(x => x.PracticeSeconds);
    public double WeekSeconds
    {
        get
        {
            var today = LocalDate(_time.GetUtcNow());
            var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            return _sessions.Where(x => x.LocalDate >= monday && x.LocalDate <= today).Sum(x => x.PracticeSeconds);
        }
    }
    public event EventHandler? Changed;

    public PracticeManager(string? dataDirectory = null, TimeProvider? timeProvider = null, TimeZoneInfo? timeZone = null)
    {
        DataDirectory = Path.GetFullPath(dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyMelodyPractice"));
        _time = timeProvider ?? TimeProvider.System;
        _zone = timeZone ?? TimeZoneInfo.Local;
        _store = new PracticeStore(DataDirectory);
        try { (State, Settings, _sessions) = _store.Load(); }
        catch { _store.Dispose(); throw; }
        _lastSaveTimestamp = _time.GetTimestamp();
        string todayBackup = AutomaticBackupPath(LocalDate(_time.GetUtcNow()));
        if (File.Exists(todayBackup)) _lastAutomaticBackupDate = LocalDate(_time.GetUtcNow());
    }

    public void NoteOn()
    {
        ThrowIfDisposed();
        if (IsPaused) return;
        Advance();
        if (!IsPracticing) BeginInterval(PracticeMode.Automatic);
        _lastNoteTimestamp = _time.GetTimestamp();
        EnsureSession(_cursorUtc, IsManual ? PracticeMode.Manual : PracticeMode.Automatic);
        _currentSession!.NoteCount++;
        _dirtySessions.Add(_currentSession.Id);
        Notify();
    }

    public void Tick()
    {
        ThrowIfDisposed();
        bool changed = Advance();
        if (_growthSaveRequired || _time.GetElapsedTime(_lastSaveTimestamp, _time.GetTimestamp()).TotalSeconds >= 5)
        {
            try
            {
                SaveCore();
                CreateAutomaticBackup();
                if (LastStorageError is not null) changed = true;
                LastStorageError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or System.Text.Json.JsonException)
            {
                _lastSaveTimestamp = _time.GetTimestamp();
                LastStorageError = "자동 저장 또는 백업에 실패했습니다: " + ex.Message;
                changed = true;
            }
        }
        if (changed) Notify();
    }

    public void StartManual()
    {
        ThrowIfDisposed();
        if (IsManual && !IsPaused) return;
        Advance();
        EndInterval();
        IsPaused = false;
        BeginInterval(PracticeMode.Manual);
        SaveCore();
        Notify();
    }

    public void StopManual()
    {
        ThrowIfDisposed();
        if (!IsManual) return;
        Advance(); EndInterval(); SaveCore(); Notify();
    }

    public void Pause()
    {
        ThrowIfDisposed();
        Advance(); EndInterval(); IsPaused = true; SaveCore(); Notify();
    }

    public void Resume()
    {
        ThrowIfDisposed();
        IsPaused = false; Notify(); // A fresh note or explicit manual start is required.
    }

    /// <summary>Call before PC lock/suspend and when the selected MIDI input disconnects.</summary>
    public void Suspend()
    {
        ThrowIfDisposed();
        Advance(); EndInterval(); SaveCore(); Notify();
    }

    public CharacterProgress Draw()
    {
        ThrowIfDisposed();
        Advance();
        if (!State.CanDraw) throw new InvalidOperationException("현재 마이멜로디의 성장을 완료하면 다음 친구를 만날 수 있어요.");
        var owned = State.Characters.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var available = CharacterCatalog.All.Where(x => !owned.Contains(x.Id)).ToArray();
        if (available.Length == 0) throw new InvalidOperationException("모든 마이멜로디를 만났어요.");
        var character = new CharacterProgress { Id = available[RandomNumberGenerator.GetInt32(available.Length)].Id, AcquiredAt = _time.GetUtcNow() };
        string? previousGrowing = State.GrowingCharacterId;
        string? previousDisplay = State.DisplayCharacterId;
        State.Characters.Add(character);
        State.GrowingCharacterId = State.DisplayCharacterId = character.Id;
        try { SaveCore(); }
        catch
        {
            State.Characters.Remove(character); State.GrowingCharacterId = previousGrowing; State.DisplayCharacterId = previousDisplay;
            throw;
        }
        Notify();
        return character;
    }

    public void SelectDisplay(string characterId)
    {
        ThrowIfDisposed();
        if (!State.Characters.Any(x => x.Id == characterId)) throw new InvalidOperationException("먼저 이 마이멜로디를 만나 주세요.");
        State.DisplayCharacterId = characterId; SaveCore(); Notify();
    }

    public IReadOnlyList<DailyPracticeStat> GetDailyStats() => _sessions
        .GroupBy(x => x.LocalDate).OrderBy(x => x.Key)
        .Select(x => new DailyPracticeStat(x.Key, x.Sum(s => s.PracticeSeconds), x.Sum(s => s.NoteCount))).ToArray();

    /// <summary>Sessions for display; short gaps are grouped without crediting any break time.</summary>
    public IReadOnlyList<PracticeSessionGroup> GetPracticeSessions() => PracticeSessionGrouping.Group(_sessions);

    public void Save()
    {
        ThrowIfDisposed(); Advance(); SaveCore(); Notify();
    }

    public void Backup(string destination)
    {
        ThrowIfDisposed(); Advance(); SaveCore(); _store.Backup(destination, _time.GetUtcNow());
    }

    public void CreateAutomaticBackup()
    {
        ThrowIfDisposed();
        var today = LocalDate(_time.GetUtcNow());
        if (_lastAutomaticBackupDate == today) return;
        _store.Backup(AutomaticBackupPath(today), _time.GetUtcNow());
        _lastAutomaticBackupDate = today;
        var backups = Directory.EnumerateFiles(Path.Combine(DataDirectory, "Backups"), "auto-????-??-??.mymelody-backup.zip")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(7).ToArray();
        foreach (var backup in backups) File.Delete(backup);
    }

    public void Restore(string source)
    {
        ThrowIfDisposed();
        var restored = _store.Restore(source, () =>
        {
            Advance(); EndInterval(); SaveCore();
            string beforeRestore = Path.Combine(DataDirectory, "Backups", $"before-restore-{_time.GetUtcNow():yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.mymelody-backup.zip");
            _store.Backup(beforeRestore, _time.GetUtcNow());
        });
        EndInterval();
        State = restored.State; Settings = restored.Settings; _sessions = restored.Sessions;
        _dirtySessions.Clear(); _lastSaveTimestamp = _time.GetTimestamp(); LastStorageError = null;
        Notify();
    }

    private bool Advance()
    {
        if (!IsPracticing) return false;
        long now = _time.GetTimestamp();
        long end = now;
        long deadline = 0;
        if (_automatic)
        {
            deadline = _lastNoteTimestamp + (long)(GrowthRules.GraceSeconds * _time.TimestampFrequency);
            end = Math.Min(now, deadline);
        }
        double seconds = Math.Max(0, _time.GetElapsedTime(_cursorTimestamp, end).TotalSeconds);
        bool changed = seconds > 0;
        if (seconds > 0)
        {
            CreditInterval(_cursorUtc, seconds, IsManual ? PracticeMode.Manual : PracticeMode.Automatic);
            _cursorUtc = _cursorUtc.AddSeconds(seconds);
            _cursorTimestamp = end;
        }
        if (_automatic && now >= deadline) { EndInterval(); changed = true; }
        return changed;
    }

    private void BeginInterval(PracticeMode mode)
    {
        _automatic = mode == PracticeMode.Automatic;
        IsManual = mode == PracticeMode.Manual;
        _cursorTimestamp = _lastNoteTimestamp = _time.GetTimestamp();
        _cursorUtc = _time.GetUtcNow();
        EnsureSession(_cursorUtc, mode);
    }

    private void EndInterval()
    {
        _automatic = false; IsManual = false; _currentSession = null;
    }

    private void CreditInterval(DateTimeOffset startedAt, double seconds, PracticeMode mode)
    {
        int previousStage = State.GrowingCharacter?.Stage ?? 0;
        bool previouslyComplete = State.GrowingCharacter?.IsComplete ?? true;
        var cursor = startedAt;
        var finalEnd = startedAt.AddSeconds(seconds);
        while (cursor < finalEnd)
        {
            EnsureSession(cursor, mode);
            var dayEnd = NextLocalMidnightUtc(cursor);
            var partEnd = finalEnd < dayEnd ? finalEnd : dayEnd;
            double duration = (partEnd - cursor).TotalSeconds;
            _currentSession!.PracticeSeconds += duration;
            _currentSession.EndedAt = partEnd;
            _dirtySessions.Add(_currentSession.Id);
            if (State.GrowingCharacter is { IsComplete: false } growing)
            {
                double credited = Math.Min(duration, GrowthRules.CompletionSeconds - growing.PracticeSeconds);
                growing.PracticeSeconds += credited;
                if (growing.IsComplete)
                {
                    var completedAt = cursor.AddSeconds(credited);
                    growing.CompletedAt = completedAt < growing.AcquiredAt ? growing.AcquiredAt : completedAt;
                }
            }
            cursor = partEnd;
            if (cursor == dayEnd) _currentSession = null;
        }
        // Persist only after Advance updates its monotonic cursor. A failed write must never
        // cause the same interval to be credited twice on the next timer tick.
        if (previousStage != (State.GrowingCharacter?.Stage ?? 0) || previouslyComplete != (State.GrowingCharacter?.IsComplete ?? true)) _growthSaveRequired = true;
    }

    private void EnsureSession(DateTimeOffset instant, PracticeMode mode)
    {
        var date = LocalDate(instant);
        if (_currentSession is not null && _currentSession.LocalDate == date && _currentSession.Mode == mode) return;
        _currentSession = new PracticeSession { StartedAt = instant, EndedAt = instant, LocalDate = date, Mode = mode };
        _sessions.Add(_currentSession); _dirtySessions.Add(_currentSession.Id);
    }

    private DateOnly LocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _zone).DateTime);

    private DateTimeOffset NextLocalMidnightUtc(DateTimeOffset instant)
    {
        DateTime localMidnight = LocalDate(instant).AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // A few time zones jump at midnight. Find the first valid instant of the next local day.
        while (_zone.IsInvalidTime(localMidnight)) localMidnight = localMidnight.AddMinutes(1);
        var utc = TimeZoneInfo.ConvertTimeToUtc(localMidnight, _zone);
        return new DateTimeOffset(utc);
    }

    private string AutomaticBackupPath(DateOnly date) => Path.Combine(DataDirectory, "Backups", $"auto-{date:yyyy-MM-dd}.mymelody-backup.zip");
    private void SaveCore()
    {
        _store.Save(State, Settings, _sessions, _dirtySessions);
        _lastSaveTimestamp = _time.GetTimestamp(); _growthSaveRequired = false; LastStorageError = null;
    }
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose()
    {
        if (_disposed) return;
        try { Advance(); EndInterval(); SaveCore(); }
        finally { _store.Dispose(); _disposed = true; }
    }
}
