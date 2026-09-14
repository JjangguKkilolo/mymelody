namespace MyMelody.App.Services;

public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, ReadyToApply, Applying, Failed, Unavailable }

public sealed record UpdateCandidate(string Version, string ReleaseNotes, object? NativePayload = null);

/// <summary>The backend owns package transport and hash verification; the service owns user consent and concurrency.</summary>
public interface IUpdateBackend
{
    bool IsInstalled { get; }
    string CurrentVersion { get; }
    Task<UpdateCandidate?> CheckAsync();
    Task DownloadAsync(UpdateCandidate candidate, Action<int> progress, CancellationToken cancellationToken);
    void ApplyAndRestart(UpdateCandidate candidate);
}
