using System.Text;
using System.Windows;
using Velopack;
using MyMelody.Core;
using Microsoft.Win32;

namespace MyMelody.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        var data = GetArgument(args, "--data-dir");
        var uiSmokePath = GetArgument(args, "--ui-smoke");
        if (uiSmokePath != null)
            data = Path.Combine(Path.GetFullPath(uiSmokePath), "data-" + Guid.NewGuid().ToString("N"));
        var resolvedData = Path.GetFullPath(data ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyMelodyPractice")).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        var identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(resolvedData)))[..12];
        using var mutex = new Mutex(true, @"Local\MyMelodyPractice-" + identity, out bool isFirst);
        if (!isFirst)
        {
            try { using var signal = EventWaitHandle.OpenExisting(@"Local\MyMelodyPractice-Show-" + identity); signal.Set(); } catch { }
            return 0;
        }
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\MyMelodyPractice-Show-" + identity);
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources = new ResourceDictionary { Source = new Uri("Themes.xaml", UriKind.Relative) };
            AppHost host;
            try { host = new AppHost(app, data, args, showSignal, uiSmokePath != null ? new UiSmokeClock() : null); }
            catch (Exception startupException) when (startupException is InvalidDataException or Microsoft.Data.Sqlite.SqliteException or System.Text.Json.JsonException)
            {
                if (uiSmokePath != null) throw;
                if (MessageBox.Show("저장 파일을 열지 못했어요. 원본을 보존하고 백업에서 복원할까요?\n\n" + startupException.Message,
                    "마이멜로디 기록 복구", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return 1;
                var dialog = new OpenFileDialog { Title = "복원할 마이멜로디 백업 선택", Filter = "마이멜로디 백업 (*.zip)|*.zip" };
                if (dialog.ShowDialog() != true) return 1;
                PracticeManager.RecoverFromBackup(resolvedData, dialog.FileName);
                host = new AppHost(app, data, args, showSignal);
            }
            app.DispatcherUnhandledException += (_, e) =>
            {
                if (uiSmokePath != null)
                {
                    Directory.CreateDirectory(uiSmokePath);
                    File.WriteAllText(Path.Combine(uiSmokePath, "failure.txt"), e.Exception.ToString());
                    e.Handled = true; app.Shutdown(1); return;
                }
                host.ReportError(e.Exception);
                e.Handled = true;
            };
            app.Startup += (_, _) => host.Start();
            app.Exit += (_, _) => host.Dispose();
            return app.Run();
        }
        catch (Exception exception)
        {
            if (uiSmokePath != null)
            {
                Directory.CreateDirectory(uiSmokePath);
                File.WriteAllText(Path.Combine(uiSmokePath, "failure.txt"), exception.ToString());
                return 1;
            }
            var folder = data ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyMelodyPractice");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "error.log"), $"{DateTimeOffset.Now:O} {exception}\n");
            MessageBox.Show("앱을 시작하지 못했어요. 저장한 기록은 유지됩니다.\n\n" + exception.Message, "마이멜로디 연습 친구");
            return 1;
        }
    }
    internal static string? GetArgument(string[] args, string key)
    {
        var index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
