using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using MyMelody.App.Services;
using MyMelody.Core;
using Velopack;
using Velopack.Sources;
using Forms = System.Windows.Forms;

namespace MyMelody.App;

public sealed class AppHost : IDisposable
{
    private readonly Application _application;
    private readonly string[] _args;
    private readonly EventWaitHandle _showSignal;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private string? _growthSignature;
    private string? _growthCharacterId;
    private DateTimeOffset _nextUpdateCheck = DateTimeOffset.MinValue;
    private bool _disposed;
    private bool _applyingUpdate;
    private bool _systemSuspended;
    private bool _sessionLocked;
    private bool _powerSuspended;
    private readonly TimeProvider? _clock;
    public PracticeManager Manager { get; }
    public MidiInputService Midi { get; }
    public UpdateService Updates { get; }
    public MainWindow Main { get; private set; } = null!;
    public PetWindow Pet { get; private set; } = null!;
    public bool IsExiting { get; private set; }
    public bool MidiConnected { get; private set; }
    public string MidiStatus { get; private set; } = "건반을 선택해 주세요.";

    public AppHost(Application application, string? data, string[] args, EventWaitHandle showSignal, TimeProvider? clock = null)
    {
        _application = application; _args = args; _showSignal = showSignal;
        _clock = clock;
        Manager = new PracticeManager(data, clock);
        Midi = new MidiInputService(new DispatcherSynchronizationContext(application.Dispatcher));
        var localFeed = Program.GetArgument(args, "--update-source");
        Updates = new UpdateService(new VelopackUpdateBackend(
            new UpdateManager(localFeed != null
                ? new SimpleFileSource(new DirectoryInfo(Path.GetFullPath(localFeed)))
                : new GithubSource(UpdateService.RepositoryUrl, null, prerelease: false),
                new UpdateOptions { AllowVersionDowngrade = false, ExplicitChannel = "win" }),
            args));
        Midi.NoteOn += (_, _) => { if (!_systemSuspended && !IsExiting) Manager.NoteOn(); };
        Midi.ConnectionChanged += (_, e) =>
        {
            if (IsExiting) return;
            MidiConnected = e.IsConnected; MidiStatus = e.Message;
            if (!e.IsConnected && !Manager.IsManual) Manager.Suspend();
        };
        Updates.Changed += (_, _) => application.Dispatcher.InvokeAsync(() =>
        {
            if (IsExiting) return;
            if (Updates.LastCheckedAt != null) Manager.Settings.LastUpdateCheck = Updates.LastCheckedAt;
            Main?.RefreshUpdate();
        });
    }
    public void Start()
    {
        Main = new MainWindow(this); Pet = new PetWindow(this);
        _application.MainWindow = Main;
        var smokePath = Program.GetArgument(_args, "--ui-smoke");
        if (smokePath != null)
        {
            Manager.Settings.AutoCheckUpdates = false;
            Main.LoadSettings();
            Main.ShowInTaskbar = false;
            Main.Left = SystemParameters.VirtualScreenLeft - Main.Width - 100;
            Main.Top = SystemParameters.VirtualScreenTop;
            Main.WindowStartupLocation = WindowStartupLocation.Manual;
            Main.Show();
            _application.Dispatcher.InvokeAsync(() =>
            {
                var exitCode = 0;
                try { UiSmoke.Run(this, (UiSmokeClock)_clock!, smokePath); }
                catch (Exception ex) { exitCode = 1; Directory.CreateDirectory(smokePath); File.WriteAllText(Path.Combine(smokePath, "failure.txt"), ex.ToString()); }
                finally { IsExiting = true; _application.Shutdown(exitCode); }
            }, DispatcherPriority.ApplicationIdle);
            return;
        }
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("연습 친구 열기", null, (_, _) => Dispatch(() => ShowMain()));
        menu.Items.Add("캐릭터 표시 / 숨기기", null, (_, _) => Dispatch(() => SetPetVisibility(!Manager.Settings.CharacterVisible)));
        menu.Items.Add("연습 일시정지 / 재개", null, (_, _) => Dispatch(TogglePause));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => Dispatch(Exit));
        _trayIcon = AppIcon.CreateTrayIcon();
        _tray = new Forms.NotifyIcon { Text = "마이멜로디 연습 친구", Icon = _trayIcon, Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => Dispatch(() => ShowMain());
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _application.SessionEnding += (_, _) => { SuspendSystem(); Manager.Save(); };
        Midi.SelectDevice(Manager.Settings.MidiDeviceId);
        Main.LoadSettings();
        ApplySettings(syncAutoStart: Manager.Settings.AutoStart);
        Pet.Refresh();
        if (!_args.Contains("--minimized")) Main.Show();
        _timer.Tick += Tick;
        _timer.Start();
        Tick(this, EventArgs.Empty);
    }
    private void Tick(object? sender, EventArgs e)
    {
        if (IsExiting) return;
        try
        {
            if (_showSignal.WaitOne(0)) ShowMain();
            Manager.Tick();
            Main.Refresh();
            Pet.Refresh();
            var growing = Manager.State.Characters.FirstOrDefault(x => x.Id == Manager.State.GrowingCharacterId);
            var signature = growing == null ? "" : $"{growing.Id}:{growing.Stage}:{growing.IsComplete}";
            if (_growthSignature != null && signature != _growthSignature && growing != null && _growthCharacterId == growing.Id)
            {
                Main.Toast(growing.IsComplete
                    ? Manager.State.CanDraw ? "36시간을 함께했어요! 다음 친구를 만날 수 있어요 ♡" : "아홉 친구와 모든 이야기를 완성했어요 ♡"
                    : $"새로운 모습으로 자랐어요! {growing.Stage}단계 ♡");
                Main.Celebrate(); Pet.Celebrate();
                if (Manager.Settings.SoundEnabled) System.Media.SystemSounds.Asterisk.Play();
            }
            _growthSignature = signature;
            _growthCharacterId = growing?.Id;
            if (Manager.Settings.AutoCheckUpdates && DateTimeOffset.UtcNow >= _nextUpdateCheck)
            {
                _nextUpdateCheck = DateTimeOffset.UtcNow.AddHours(6);
                _ = Updates.CheckAsync();
            }
        }
        catch (Exception exception) { ReportError(exception); }
    }
    public void ShowMain(string? page = null)
    {
        if (IsExiting) return;
        Main.Show(); if (Main.WindowState == WindowState.Minimized) Main.WindowState = WindowState.Normal;
        if (page != null) Main.Navigate(page);
        Main.Activate();
    }
    public void TogglePause()
    {
        if (Manager.IsPaused) Manager.Resume(); else Manager.Pause();
        Main.Refresh(); Manager.Save();
    }
    public void SetPetVisibility(bool visible)
    {
        Manager.Settings.CharacterVisible = visible; Manager.Save(); Pet.Refresh(); Main.LoadSettings();
    }
    public void ApplySettings(bool syncAutoStart = false)
    {
        Manager.Settings.CharacterSize = Math.Clamp(Manager.Settings.CharacterSize, 48, 384);
        try
        {
            if (syncAutoStart && !_args.Contains("--ui-smoke"))
            {
                using var runKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                var explicitData = Program.GetArgument(_args, "--data-dir");
                var dataArgument = explicitData == null ? "" : $" --data-dir \"{Path.GetFullPath(explicitData)}\"";
                if (Manager.Settings.AutoStart)
                    runKey.SetValue("MyMelodyPractice", $"\"{Environment.ProcessPath}\" --minimized{dataArgument}");
                else runKey.DeleteValue("MyMelodyPractice", false);
            }
            Manager.Save(); Pet.Refresh();
        }
        catch (Exception ex) { ReportError(ex); }
    }
    public async Task ApplyUpdateAsync()
    {
        if (_applyingUpdate || IsExiting) return;
        _applyingUpdate = true;
        try
        {
            if (!await Updates.DownloadAsync() || IsExiting) return;
            try
            {
                Manager.Suspend(); Manager.Save();
                Manager.Backup(Path.Combine(Manager.DataDirectory, "backups", "before-update-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip"));
            }
            catch (Exception exception) { ReportError(exception); return; }
            IsExiting = true;
            try
            {
                _timer.Stop(); Midi.Dispose(); Manager.Dispose();
                DisposeTray();
                Updates.ApplyAndRestart();
            }
            catch (Exception exception)
            {
                MessageBox.Show(Main, "업데이트를 적용하지 못했어요. 기록은 저장되었습니다.\n앱을 다시 열어 주세요.\n\n" + exception.Message, "업데이트");
                _application.Shutdown(1);
            }
        }
        finally { _applyingUpdate = false; }
    }
    public void Exit()
    {
        if (IsExiting) return;
        try { Manager.Suspend(); Manager.Save(); }
        catch (Exception ex) { ReportError(ex); return; }
        IsExiting = true; _application.Shutdown();
    }
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e) => DispatchCritical(() =>
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.ConsoleDisconnect)
        { _sessionLocked = true; SuspendSystem(); }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.RemoteConnect or SessionSwitchReason.ConsoleConnect)
        { _sessionLocked = false; ResumeSystem(); }
    });
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) => DispatchCritical(() =>
    {
        if (e.Mode == PowerModes.Suspend) { _powerSuspended = true; SuspendSystem(); }
        else if (e.Mode == PowerModes.Resume) { _powerSuspended = false; ResumeSystem(); }
    });
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatch(() => Pet.ApplySavedPosition());
    private void SuspendSystem() { _systemSuspended = true; Manager.Suspend(); Manager.Save(); Midi.Suspend(); }
    private void ResumeSystem() { _systemSuspended = _sessionLocked || _powerSuspended; if (!_systemSuspended) Midi.Resume(); }
    private void DispatchCritical(Action action) { if (!IsExiting) _application.Dispatcher.Invoke(() => { if (!IsExiting) action(); }); }
    private void Dispatch(Action action) { if (!IsExiting) _application.Dispatcher.InvokeAsync(() => { if (!IsExiting) action(); }); }
    public void ReportError(Exception exception)
    {
        try { File.AppendAllText(Path.Combine(Manager.DataDirectory, "error.log"), $"{DateTimeOffset.Now:O} {exception}\n"); } catch { }
        Main?.Toast("문제가 발생했어요: " + exception.Message);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; IsExiting = true;
        _timer.Stop();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Midi.Dispose(); Manager.Dispose();
        DisposeTray();
    }
    private void DisposeTray()
    {
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        _trayIcon?.Dispose(); _trayIcon = null;
    }
}
