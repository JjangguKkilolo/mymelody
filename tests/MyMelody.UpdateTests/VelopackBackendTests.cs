using System.Text.Json;
using MyMelody.App.Services;
using Velopack;
using Velopack.Locators;
using Xunit;

namespace MyMelody.UpdateTests;

public sealed class VelopackBackendTests
{
    [Fact]
    public async Task ActualVelopackRejectsDamagedPackageHash()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MyMelodyUpdateTest-" + Guid.NewGuid().ToString("N"));
        var feed = Path.Combine(directory, "feed");
        var downloaded = Path.Combine(directory, "downloaded");
        Directory.CreateDirectory(feed);
        Directory.CreateDirectory(downloaded);
        try
        {
            const string fileName = "MyMelodyPractice-1.0.1-full.nupkg";
            File.WriteAllBytes(Path.Combine(feed, fileName), new byte[512]);
            var manifest = new
            {
                Assets = new[]
                {
                    new { PackageId = "MyMelodyPractice", Version = "1.0.1", Type = "Full", FileName = fileName,
                        SHA1 = new string('0', 40), SHA256 = new string('0', 64), Size = 512 }
                }
            };
            File.WriteAllText(Path.Combine(feed, "releases.win.json"), JsonSerializer.Serialize(manifest));
            var locator = new TestVelopackLocator("MyMelodyPractice", "1.0.0", downloaded);
            var manager = new UpdateManager(feed, new UpdateOptions { ExplicitChannel = "win", AllowVersionDowngrade = false }, locator);
            var updater = new UpdateService(new VelopackUpdateBackend(manager));
            await updater.CheckAsync();
            Assert.Equal(UpdateState.Available, updater.State);
            Assert.Empty(Directory.GetFiles(downloaded, "*.nupkg"));
            Assert.False(await updater.DownloadAsync());
            Assert.Equal(UpdateState.Failed, updater.State);
            Assert.False(updater.CanApply);
            Assert.Throws<InvalidOperationException>(updater.ApplyAndRestart);
        }
        finally
        {
            // This directory is created by this test with an unguessable name, never a supplied path.
            Directory.Delete(directory, recursive: true);
        }
    }
}
