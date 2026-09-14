using MyMelody.App.Services;
using Xunit;

namespace MyMelody.UpdateTests;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task CheckingNeverDownloadsOrApplies()
    {
        var backend = new FakeBackend();
        var service = new UpdateService(backend);
        await service.CheckAsync();
        Assert.Equal(UpdateState.Available, service.State);
        Assert.Equal("1.1.0", service.AvailableVersion);
        Assert.Equal("Changes", service.ReleaseNotes);
        Assert.NotNull(service.LastCheckedAt);
        Assert.False(service.CanApply);
        Assert.Equal(0, backend.DownloadCount);
        Assert.Equal(0, backend.ApplyCount);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("0.9.0")]
    [InlineData("2.0.0-beta.1")]
    [InlineData("1.0.0+different-build")]
    [InlineData("garbage")]
    [InlineData("2.0.0.0")]
    public async Task OnlyNewerStableVersionsAreCandidates(string version)
    {
        var backend = new FakeBackend { Candidate = new UpdateCandidate(version, "") };
        var service = new UpdateService(backend);
        await service.CheckAsync();
        Assert.Equal(UpdateState.UpToDate, service.State);
        Assert.Null(service.AvailableVersion);
        Assert.False(await service.DownloadAsync());
        Assert.Equal(0, backend.DownloadCount);
    }

    [Fact]
    public async Task NoUpdateDoesNotDownload()
    {
        var backend = new FakeBackend { Candidate = null };
        var service = new UpdateService(backend);
        await service.CheckAsync();
        Assert.Equal(UpdateState.UpToDate, service.State);
        Assert.False(await service.DownloadAsync());
    }

    [Fact]
    public async Task DevelopmentExecutionNeverQueriesFeed()
    {
        var backend = new FakeBackend { IsInstalled = false };
        var service = new UpdateService(backend);
        await service.CheckAsync();
        Assert.Equal(UpdateState.Unavailable, service.State);
        Assert.Equal(0, backend.CheckCount);
    }

    [Fact]
    public async Task DownloadMustFinishVerificationBeforeExplicitApply()
    {
        var backend = new FakeBackend();
        var service = new UpdateService(backend);
        await service.CheckAsync();
        Assert.Throws<InvalidOperationException>(service.ApplyAndRestart);
        Assert.Equal(0, backend.ApplyCount);
        Assert.True(await service.DownloadAsync());
        Assert.True(service.CanApply);
        Assert.Equal(100, service.Progress);
        Assert.Equal(0, backend.ApplyCount);
        service.ApplyAndRestart();
        Assert.Equal(1, backend.ApplyCount);
        Assert.Throws<InvalidOperationException>(service.ApplyAndRestart);
        Assert.Equal(1, backend.ApplyCount);
    }

    [Fact]
    public async Task NetworkFailureRetainsRunningVersionAndCanRetry()
    {
        var backend = new FakeBackend { CheckError = new HttpRequestException("offline") };
        var service = new UpdateService(backend);
        await service.CheckAsync();
        Assert.Equal(UpdateState.Failed, service.State);
        Assert.False(service.IsBusy);
        Assert.Equal("1.0.0", service.CurrentVersion);
        backend.CheckError = null;
        await service.CheckAsync();
        Assert.Equal(UpdateState.Available, service.State);
    }

    [Fact]
    public async Task CorruptPackageCannotBeAppliedAndCanRedownload()
    {
        var backend = new FakeBackend { DownloadError = new IOException("checksum mismatch") };
        var service = new UpdateService(backend);
        await service.CheckAsync();
        Assert.False(await service.DownloadAsync());
        Assert.False(service.CanApply);
        Assert.Equal(UpdateState.Failed, service.State);
        Assert.Throws<InvalidOperationException>(service.ApplyAndRestart);
        backend.DownloadError = null;
        Assert.True(await service.DownloadAsync());
        Assert.Equal(2, backend.DownloadCount);
    }

    [Fact]
    public async Task ConcurrentClickDoesNotStartSecondDownloadOrCheck()
    {
        var backend = new FakeBackend { DownloadBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service = new UpdateService(backend);
        await service.CheckAsync();
        var first = service.DownloadAsync();
        Assert.True(service.IsBusy);
        Assert.False(await service.DownloadAsync());
        await service.CheckAsync();
        Assert.Equal(1, backend.CheckCount);
        Assert.Equal(1, backend.DownloadCount);
        Assert.Throws<InvalidOperationException>(service.ApplyAndRestart);
        backend.DownloadBarrier.SetResult();
        Assert.True(await first);
        Assert.Equal(0, backend.ApplyCount);
    }

    [Fact]
    public async Task InterruptedDownloadDoesNotApply()
    {
        var backend = new FakeBackend { DownloadError = new OperationCanceledException() };
        var service = new UpdateService(backend);
        await service.CheckAsync();
        Assert.False(await service.DownloadAsync());
        Assert.Equal(UpdateState.Available, service.State);
        Assert.False(service.CanApply);
        Assert.Equal(0, backend.ApplyCount);
    }

    [Fact]
    public async Task BackgroundCheckCannotReplaceVerifiedCandidate()
    {
        var backend = new FakeBackend();
        var service = new UpdateService(backend);
        await service.CheckAsync();
        await service.DownloadAsync();
        backend.Candidate = new UpdateCandidate("1.2.0", "Newer");
        await service.CheckAsync();
        Assert.Equal("1.1.0", service.AvailableVersion);
        Assert.Equal(1, backend.CheckCount);
        Assert.True(service.CanApply);
    }

    private sealed class FakeBackend : IUpdateBackend
    {
        public bool IsInstalled { get; init; } = true;
        public string CurrentVersion => "1.0.0";
        public UpdateCandidate? Candidate { get; set; } = new("1.1.0", "Changes");
        public Exception? CheckError { get; set; }
        public Exception? DownloadError { get; set; }
        public TaskCompletionSource? DownloadBarrier { get; init; }
        public int CheckCount { get; private set; }
        public int DownloadCount { get; private set; }
        public int ApplyCount { get; private set; }
        public Task<UpdateCandidate?> CheckAsync()
        {
            CheckCount++;
            if (CheckError is not null) throw CheckError;
            return Task.FromResult(Candidate);
        }
        public async Task DownloadAsync(UpdateCandidate candidate, Action<int> progress, CancellationToken cancellationToken)
        {
            DownloadCount++;
            progress(25);
            if (DownloadBarrier is not null) await DownloadBarrier.Task.WaitAsync(cancellationToken);
            if (DownloadError is not null) throw DownloadError;
            progress(100);
        }
        public void ApplyAndRestart(UpdateCandidate candidate) => ApplyCount++;
    }
}
