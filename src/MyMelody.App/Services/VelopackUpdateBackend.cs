using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace MyMelody.App.Services;

public sealed partial class UpdateService
{
    public UpdateService() : this(new VelopackUpdateBackend()) { }
}

public sealed class VelopackUpdateBackend : IUpdateBackend
{
    private readonly UpdateManager _manager;
    private readonly string[] _restartArguments;

    public VelopackUpdateBackend()
        : this(new UpdateManager(new GithubSource(UpdateService.RepositoryUrl, null, prerelease: false),
            new UpdateOptions { AllowVersionDowngrade = false, ExplicitChannel = "win" })) { }

    // Injection supports a local release feed for real portable/install update smoke tests.
    public VelopackUpdateBackend(UpdateManager manager, string[]? restartArguments = null)
    {
        _manager = manager;
        _restartArguments = restartArguments ?? [];
    }

    public bool IsInstalled => _manager.IsInstalled;
    public string CurrentVersion => _manager.CurrentVersion?.ToString()
        ?? Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "1.0.0";

    public async Task<UpdateCandidate?> CheckAsync()
    {
        var info = await _manager.CheckForUpdatesAsync();
        if (info is null || info.IsDowngrade || info.TargetFullRelease.Version.IsPrerelease) return null;
        return new UpdateCandidate(info.TargetFullRelease.Version.ToString(), info.TargetFullRelease.NotesMarkdown ?? "변경 내역이 없어요.", info);
    }

    public Task DownloadAsync(UpdateCandidate candidate, Action<int> progress, CancellationToken cancellationToken)
        => _manager.DownloadUpdatesAsync(GetInfo(candidate), progress, cancellationToken);

    public void ApplyAndRestart(UpdateCandidate candidate)
        => _manager.ApplyUpdatesAndRestart(GetInfo(candidate).TargetFullRelease, _restartArguments);

    private static UpdateInfo GetInfo(UpdateCandidate candidate)
        => candidate.NativePayload as UpdateInfo ?? throw new InvalidOperationException("업데이트 패키지 정보가 없어요.");
}
