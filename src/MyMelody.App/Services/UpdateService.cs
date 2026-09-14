namespace MyMelody.App.Services;

/// <summary>Checking is read-only. Download and apply are separate explicit user actions.</summary>
public sealed partial class UpdateService
{
    public const string RepositoryUrl = "https://github.com/JjangguKkilolo/mymelody";
    public static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(6);
    private readonly IUpdateBackend _backend;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly SynchronizationContext? _context;
    private UpdateCandidate? _candidate;
    private bool _downloadVerified;

    public UpdateService(IUpdateBackend backend)
    {
        _backend = backend;
        _context = SynchronizationContext.Current;
    }

    public event EventHandler? Changed;
    public UpdateState State { get; private set; } = UpdateState.Idle;
    public string StatusText { get; private set; } = "업데이트를 아직 확인하지 않았어요.";
    public int Progress { get; private set; }
    public string ReleaseNotes => _candidate?.ReleaseNotes ?? "";
    public string CurrentVersion => _backend.CurrentVersion;
    public string? AvailableVersion => _candidate?.Version;
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public bool IsBusy => _operation.CurrentCount == 0;
    public bool CanApply => _downloadVerified && _candidate is not null && State == UpdateState.ReadyToApply;

    public async Task CheckAsync()
    {
        if (!await _operation.WaitAsync(0)) return;
        try
        {
            if (_downloadVerified) return; // Do not replace a candidate between download and the explicit apply.
            LastCheckedAt = DateTimeOffset.Now;
            if (!_backend.IsInstalled)
            {
                SetState(UpdateState.Unavailable, "개발 실행에서는 업데이트를 설치할 수 없어요. 릴리스의 설치형 또는 포터블 앱을 사용해 주세요.");
                return;
            }
            SetState(UpdateState.Checking, "새 버전을 확인하고 있어요…");
            var candidate = await _backend.CheckAsync();
            _candidate = candidate is not null && IsNewerStable(candidate.Version, CurrentVersion) ? candidate : null;
            SetState(_candidate is null ? UpdateState.UpToDate : UpdateState.Available,
                _candidate is null ? "최신 버전을 사용하고 있어요." : $"새 버전 {_candidate.Version}을 사용할 수 있어요.");
        }
        catch (Exception ex)
        {
            SetState(UpdateState.Failed, $"업데이트를 확인하지 못했어요. 현재 버전은 계속 사용할 수 있어요. {ex.Message}");
        }
        finally
        {
            _operation.Release();
            Notify();
        }
    }

    public async Task<bool> DownloadAsync(CancellationToken cancellationToken = default)
    {
        if (!await _operation.WaitAsync(0, cancellationToken)) return false;
        try
        {
            if (_downloadVerified && _candidate is not null)
            {
                SetState(UpdateState.ReadyToApply, "업데이트 준비가 끝났어요. 기록을 저장한 뒤 다시 시작해요.");
                return true;
            }
            if (_candidate is null || !_backend.IsInstalled) return false;
            Progress = 0;
            SetState(UpdateState.Downloading, "업데이트를 내려받고 확인하고 있어요…");
            await _backend.DownloadAsync(_candidate, percent =>
            {
                Progress = Math.Clamp(percent, 0, 100);
                Notify();
            }, cancellationToken);
            // Velopack returns only after package size and checksum validation complete.
            _downloadVerified = true;
            Progress = 100;
            SetState(UpdateState.ReadyToApply, "업데이트 준비가 끝났어요. 기록을 저장한 뒤 다시 시작해요.");
            return true;
        }
        catch (OperationCanceledException)
        {
            _downloadVerified = false;
            SetState(UpdateState.Available, "다운로드를 취소했어요. 나중에 다시 시도할 수 있어요.");
            return false;
        }
        catch (Exception ex)
        {
            _downloadVerified = false;
            SetState(UpdateState.Failed, $"업데이트 다운로드 또는 검증에 실패했어요. 다시 시도할 수 있어요. {ex.Message}");
            return false;
        }
        finally
        {
            _operation.Release();
            Notify();
        }
    }

    /// <summary>Caller must first stop practice, save, back up, and close MIDI/database handles.</summary>
    public void ApplyAndRestart()
    {
        if (!_operation.Wait(0)) throw new InvalidOperationException("이미 업데이트 작업을 진행 중이에요.");
        try
        {
            if (!CanApply) throw new InvalidOperationException("검증을 완료한 업데이트가 없어요.");
            SetState(UpdateState.Applying, "업데이트를 설치하고 다시 시작하고 있어요…");
            _backend.ApplyAndRestart(_candidate!);
        }
        catch (Exception ex)
        {
            SetState(UpdateState.Failed, $"업데이트를 적용하지 못했어요. {ex.Message}");
            throw;
        }
        finally
        {
            _operation.Release();
            Notify();
        }
    }

    public static bool IsNewerStable(string candidate, string current)
    {
        // Releases are exactly major.minor.patch, with optional SemVer build metadata.
        // GithubSource also excludes GitHub draft/prerelease entries. Reject prerelease package versions defensively.
        static Version? Parse(string value)
        {
            var core = value.Split('+')[0];
            if (core.Contains('-') || core.Split('.').Length != 3 || !Version.TryParse(core, out var parsed)) return null;
            return parsed;
        }
        var next = Parse(candidate);
        var installed = Parse(current);
        return next is not null && installed is not null && next > installed;
    }

    private void SetState(UpdateState state, string message)
    {
        State = state;
        StatusText = message;
        Notify();
    }

    private void Notify()
    {
        if (_context is not null && SynchronizationContext.Current != _context)
            _context.Post(_ => Changed?.Invoke(this, EventArgs.Empty), null);
        else Changed?.Invoke(this, EventArgs.Empty);
    }
}
