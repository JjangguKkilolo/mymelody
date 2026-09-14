using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using Microsoft.Win32;
using MyMelody.App.Services;
using MyMelody.Core;

namespace MyMelody.App;

public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private bool _loadingSettings;
    private string _page = "Home";
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateOnly? _selectedDay;
    private string _collectionSignature = "";
    private DateTime _lastRecordsRefresh = DateTime.MinValue;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private PracticeManager Manager => _host.Manager;
    public MainWindow(AppHost host)
    {
        _host = host; InitializeComponent();
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(500, workArea.Height);
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(DeviceChangeHook);
        _toastTimer.Tick += (_, _) => { ToastBorder.Visibility = Visibility.Collapsed; _toastTimer.Stop(); };
        Closing += (_, e) => { if (!_host.IsExiting) { e.Cancel = true; Hide(); } };
        Navigate("Home");
    }
    private IntPtr DeviceChangeHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0219) _host.Midi.RefreshDevices();
        return IntPtr.Zero;
    }
    public void Navigate(string page)
    {
        _page = page;
        PageScroll.ScrollToTop();
        HomePage.Visibility = page == "Home" ? Visibility.Visible : Visibility.Collapsed;
        CollectionPage.Visibility = page == "Collection" ? Visibility.Visible : Visibility.Collapsed;
        RecordsPage.Visibility = page == "Records" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new[] { HomeNav, CollectionNav, RecordsNav, SettingsNav })
            button.Background = (string)button.Tag == page ? Brush("#F8DCE7") : Brushes.Transparent;
        PageTitle.Text = page switch { "Collection" => "우리의 작은 컬렉션", "Records" => "차곡차곡, 연습의 기록", "Settings" => "나에게 맞는 연습 공간", _ => "오늘도, 조금씩 함께 자라요" };
        PageEyebrow.Text = page switch { "Collection" => "NINE LITTLE STORIES", "Records" => "EVERY LITTLE PRACTICE COUNTS", "Settings" => "MAKE YOURSELF AT HOME", _ => "OUR LITTLE PRACTICE ROOM" };
        if (page == "Collection") RefreshCollection(true);
        if (page == "Records") RefreshRecords();
    }
    public void Refresh()
    {
        var current = Manager.State.Characters.FirstOrDefault(x => x.Id == Manager.State.GrowingCharacterId);
        HeroSprite.ShowCharacter(current?.Id, current?.Stage ?? 1);
        HeroSprite.IsPlaying = Manager.IsPracticing;
        CompanionName.Text = current == null ? "어떤 친구를 만나게 될까요?" : CharacterCatalog.Get(current.Id).Name + " 마이멜로디";
        StageLabel.Text = current == null ? "첫 만남을 기다리는 중" : current.IsComplete ? "함께 완성한 이야기 ♡" : $"{current.Stage}단계 · {new[] { "첫 만남", "조금 더 가까이", "소중한 단짝" }[current.Stage - 1]}";
        CompanionMessage.Text = current == null ? "첫 마이멜로디를 뽑고\n우리의 연습 이야기를 시작해요." : current.IsComplete ? "함께한 연습이 소중한 추억이 되었어요.\n오늘의 연습도 계속 기록할게요." : "서두르지 않아도 괜찮아요.\n당신의 연습 곁에 있을게요.";
        GrowthProgress.Value = current?.ProgressToNextStage ?? 0;
        var remaining = current == null ? 12 * 3600 : current.IsComplete ? 0 : (current.Stage * 12 * 3600) - current.PracticeSeconds;
        GrowthDetail.Text = current == null ? "한 단계마다 연습 12시간" : current.IsComplete ? "36시간의 연습 · 성장 완료" : $"{(current.Stage == 3 ? "성장 완료" : "다음 단계")}까지 {Duration(remaining)}  ·  누적 {Duration(current.PracticeSeconds)}";
        DrawButton.Visibility = Manager.State.CanDraw ? Visibility.Visible : Visibility.Collapsed;
        DrawButton.Content = Manager.State.Characters.Count == 0 ? "♡  첫 친구 만나기" : "♡  새로운 친구 만나기";
        TodayTotal.Text = Duration(Manager.TodaySeconds);
        WeekTotal.Text = Duration(Manager.WeekSeconds);
        CollectedTotal.Text = $"{Manager.State.Characters.Count} / 9";
        SidebarStatus.Text = Manager.IsPracticing ? "♪ 지금 함께 연습하는 중" : Manager.IsPaused ? "잠시 쉬어 가는 중" : "작은 시작을 기다려요";
        PracticeStatus.Text = Manager.IsPaused ? "연습 기록을 잠시 멈췄어요" : Manager.IsManual ? "수동 연습을 기록하고 있어요" : Manager.IsPracticing ? "♪ 연습 시간이 쌓이고 있어요" : "연습을 기다리고 있어요";
        PracticeHint.Text = Manager.IsManual ? "악보 읽기나 음악 공부도 함께 기록해요." : Manager.IsPaused ? "재개한 뒤 새 건반 입력부터 다시 기록해요." : "건반 입력이 30초 동안 없으면 자동으로 쉬어 가요.";
        PauseButton.Content = Manager.IsPaused ? "기록 재개" : "일시정지";
        ManualButton.Content = Manager.IsManual ? "수동 연습 종료" : "수동 연습 시작";
        ConnectionBadge.Text = _host.MidiConnected ? "●  MIDI 연결됨" : "○  MIDI 연결 대기";
        MidiMessage.Text = _host.MidiStatus;
        VersionLabel.Text = $"v{_host.Updates.CurrentVersion} · 나만의 연습 공간";
        if (_page == "Collection") RefreshCollection();
        if (_page == "Records" && (DateTime.Now - _lastRecordsRefresh).TotalSeconds >= 5) RefreshRecords();
        if (!string.IsNullOrEmpty(Manager.LastStorageError)) Toast("저장 확인이 필요해요: " + Manager.LastStorageError);
    }
    public void LoadSettings()
    {
        _loadingSettings = true;
        SizeSlider.Value = Manager.Settings.CharacterSize;
        SizeLabel.Text = $"캐릭터 크기 · {Manager.Settings.CharacterSize:0}";
        TopmostCheck.IsChecked = Manager.Settings.AlwaysOnTop;
        VisibleCheck.IsChecked = Manager.Settings.CharacterVisible;
        SoundCheck.IsChecked = Manager.Settings.SoundEnabled;
        StartupCheck.IsChecked = Manager.Settings.AutoStart;
        AutoUpdateCheck.IsChecked = Manager.Settings.AutoCheckUpdates;
        LoadMidiDevices();
        _loadingSettings = false;
        RefreshUpdate();
    }
    private void LoadMidiDevices()
    {
        var items = new List<MidiChoice> { new(null, "건반을 선택해 주세요") };
        items.AddRange(MidiInputService.GetDevices().Select(x => new MidiChoice(x.Id, x.Name)));
        if (Manager.Settings.MidiDeviceId != null && items.All(x => x.Id != Manager.Settings.MidiDeviceId))
            items.Add(new MidiChoice(Manager.Settings.MidiDeviceId, "저장한 건반 · 연결되지 않음"));
        MidiDeviceBox.ItemsSource = items;
        MidiDeviceBox.SelectedItem = items.FirstOrDefault(x => x.Id == Manager.Settings.MidiDeviceId) ?? items[0];
    }
    private void RefreshCollection(bool force = false)
    {
        var signature = string.Join("|", Manager.State.Characters.Select(x => $"{x.Id}:{x.Stage}:{x.IsComplete}")) + Manager.State.DisplayCharacterId;
        if (!force && signature == _collectionSignature) return;
        _collectionSignature = signature;
        CollectionGrid.Children.Clear();
        foreach (var definition in CharacterCatalog.All)
        {
            var owned = Manager.State.Characters.FirstOrDefault(x => x.Id == definition.Id);
            var panel = new StackPanel();
            var sprite = new SpriteView { Height = 125, Width = 140, Opacity = owned == null ? 0.22 : 1 };
            sprite.ShowCharacter(owned?.Id ?? definition.Id, owned?.Stage ?? 1);
            panel.Children.Add(sprite);
            panel.Children.Add(new TextBlock { Text = definition.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 6) });
            panel.Children.Add(new TextBlock { Text = owned == null ? "아직 만나지 못했어요" : owned.IsComplete ? "성장 완료 ♡" : $"{owned.Stage}단계 · 함께 자라는 중", FontSize = 11, Foreground = Brush("#967B89"), HorizontalAlignment = HorizontalAlignment.Center });
            if (owned != null)
            {
                var button = new Button { Content = Manager.State.DisplayCharacterId == owned.Id ? "바탕화면에 함께하는 중" : "바탕화면에 표시", FontSize = 10, Padding = new Thickness(6, 7, 6, 7), Margin = new Thickness(0, 12, 0, 0) };
                button.Click += (_, _) => Run(() => { Manager.SelectDisplay(owned.Id); RefreshCollection(true); _host.Pet.Refresh(); });
                panel.Children.Add(button);
            }
            CollectionGrid.Children.Add(new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 10, 12), Padding = new Thickness(12), Child = panel });
        }
    }
    private void RefreshRecords()
    {
        _lastRecordsRefresh = DateTime.Now;
        AllTimeTotal.Text = Duration(Manager.TotalSeconds);
        MonthTitle.Text = _month.ToString("yyyy년 M월");
        CalendarGrid.Children.Clear();
        var daily = Manager.GetDailyStats().ToDictionary(x => x.Date);
        for (var i = 0; i < (int)_month.DayOfWeek; i++) CalendarGrid.Children.Add(new Border { Height = 58 });
        for (var day = 1; day <= DateTime.DaysInMonth(_month.Year, _month.Month); day++)
        {
            var date = new DateOnly(_month.Year, _month.Month, day);
            var seconds = daily.TryGetValue(date, out var stat) ? stat.PracticeSeconds : 0;
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = day.ToString(), HorizontalAlignment = HorizontalAlignment.Center, FontWeight = date == DateOnly.FromDateTime(DateTime.Today) ? FontWeights.Bold : FontWeights.Normal });
            content.Children.Add(new TextBlock { Text = seconds > 0 ? Duration(seconds) : "·", FontSize = 9, Foreground = Brush("#AC708A"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            var button = new Button { Content = content, Height = 58, Margin = new Thickness(2), Padding = new Thickness(2), Background = _selectedDay == date ? Brush("#F6CDDE") : seconds > 0 ? Brush("#FCE9F0") : Brushes.White };
            button.Click += (_, _) => { _selectedDay = _selectedDay == date ? null : date; RefreshRecords(); };
            CalendarGrid.Children.Add(button);
        }
        SessionsList.Children.Clear();
        var sessions = Manager.GetPracticeSessions().OrderByDescending(x => x.StartedAt).Where(x => _selectedDay == null || x.LocalDate == _selectedDay).ToList();
        SessionEmpty.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SessionEmpty.Text = _selectedDay != null ? "선택한 날짜에는 연습 기록이 없어요. 날짜를 다시 누르면 전체 기록을 볼 수 있어요." : "첫 연습을 시작하면 여기에 기록이 남아요.";
        foreach (var session in sessions)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            left.Children.Add(new TextBlock { Text = session.StartedAt.LocalDateTime.ToString("M월 d일  HH:mm") + " — " + session.EndedAt.LocalDateTime.ToString("HH:mm"), FontWeight = FontWeights.SemiBold });
            left.Children.Add(new TextBlock { Text = $"{(session.Mode == PracticeMode.Manual ? "수동 연습" : "MIDI 자동 기록")}  ·  {session.NoteCount:N0}개 음표", FontSize = 11, Foreground = Brush("#947B85"), Margin = new Thickness(0, 5, 0, 0) });
            row.Children.Add(left);
            var duration = new TextBlock { Text = Duration(session.PracticeSeconds), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(duration, 1); row.Children.Add(duration);
            SessionsList.Children.Add(new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 8), Child = row });
        }
    }
    public void RefreshUpdate()
    {
        if (!IsInitialized) return;
        var update = _host.Updates;
        UpdateVersion.Text = "현재 버전 " + update.CurrentVersion;
        UpdateStatus.Text = update.StatusText;
        var checkedAt = update.LastCheckedAt ?? Manager.Settings.LastUpdateCheck;
        LastCheckLabel.Text = checkedAt == null ? "아직 확인하지 않았어요." : "마지막 확인 · " + checkedAt.Value.ToLocalTime().ToString("M월 d일 HH:mm");
        CheckUpdateButton.IsEnabled = !update.IsBusy;
        ApplyUpdateButton.Visibility = update.AvailableVersion == null ? Visibility.Collapsed : Visibility.Visible;
        ApplyUpdateButton.IsEnabled = !update.IsBusy;
        UpdateProgress.Visibility = update.State == UpdateState.Downloading ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress.Value = update.Progress;
        ReleaseNotes.Text = update.ReleaseNotes;
        UpdateBanner.Visibility = update.AvailableVersion == null ? Visibility.Collapsed : Visibility.Visible;
        UpdateBannerText.Text = $"새 버전 {update.AvailableVersion}이 도착했어요.";
    }
    public void Toast(string text) { ToastText.Text = text; ToastBorder.Visibility = Visibility.Visible; _toastTimer.Stop(); _toastTimer.Start(); }
    public void Celebrate() => HeroSprite.Celebrate();
    private void NavigateClick(object sender, RoutedEventArgs e) => Navigate((string)((Button)sender).Tag);
    private void ShowUpdatesClick(object sender, RoutedEventArgs e) { Navigate("Settings"); PageScroll.ScrollToBottom(); }
    private void DrawClick(object sender, RoutedEventArgs e) => Run(() => { var character = Manager.Draw(); Toast(CharacterCatalog.Get(character.Id).Name + " 마이멜로디를 만났어요 ♡"); Refresh(); Celebrate(); _host.Pet.Refresh(); _host.Pet.Celebrate(); });
    private void PauseClick(object sender, RoutedEventArgs e) => Run(_host.TogglePause);
    private void ManualClick(object sender, RoutedEventArgs e) => Run(() => { if (Manager.IsManual) Manager.StopManual(); else Manager.StartManual(); Refresh(); });
    private void PreviousMonthClick(object sender, RoutedEventArgs e) { _month = _month.AddMonths(-1); _selectedDay = null; RefreshRecords(); }
    private void NextMonthClick(object sender, RoutedEventArgs e) { _month = _month.AddMonths(1); _selectedDay = null; RefreshRecords(); }
    private void RefreshMidiClick(object sender, RoutedEventArgs e) => Run(() => { _loadingSettings = true; _host.Midi.RefreshDevices(); LoadMidiDevices(); _loadingSettings = false; });
    private void MidiSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || MidiDeviceBox.SelectedItem is not MidiChoice choice) return;
        Run(() => { if (!Manager.IsManual) Manager.Suspend(); Manager.Settings.MidiDeviceId = choice.Id; _host.Midi.SelectDevice(choice.Id); Manager.Save(); });
    }
    private void CharacterSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SizeLabel == null) return;
        SizeLabel.Text = $"캐릭터 크기 · {e.NewValue:0}";
        if (!_loadingSettings) Run(() => { Manager.Settings.CharacterSize = e.NewValue; _host.Pet?.Refresh(); Manager.Save(); });
    }
    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        Manager.Settings.AlwaysOnTop = TopmostCheck.IsChecked == true;
        Manager.Settings.CharacterVisible = VisibleCheck.IsChecked == true;
        Manager.Settings.SoundEnabled = SoundCheck.IsChecked == true;
        Manager.Settings.AutoStart = StartupCheck.IsChecked == true;
        Manager.Settings.AutoCheckUpdates = AutoUpdateCheck.IsChecked == true;
        _host.ApplySettings(syncAutoStart: ReferenceEquals(sender, StartupCheck));
    }
    private void ResetPositionClick(object sender, RoutedEventArgs e) => Run(() => _host.Pet.ResetPosition());
    private void BackupClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "마이멜로디 백업 (*.zip)|*.zip", FileName = $"mymelody-backup-{DateTime.Now:yyyyMMdd}.zip" };
        if (dialog.ShowDialog(this) == true) Run(() => { Manager.Backup(dialog.FileName); Toast("백업을 저장했어요."); });
    }
    private void RestoreClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "마이멜로디 백업 (*.zip)|*.zip" };
        if (dialog.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, "현재 기록을 먼저 백업하고 선택한 기록으로 복원할까요?", "백업 복원", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Run(() => { Manager.Restore(dialog.FileName); _host.Midi.SelectDevice(Manager.Settings.MidiDeviceId); LoadSettings(); _host.ApplySettings(syncAutoStart: true); _collectionSignature = ""; Refresh(); _host.Pet.ApplySavedPosition(); Toast("기록과 친구들을 복원했어요."); });
    }
    private void OpenDataClick(object sender, RoutedEventArgs e) => Run(() => Process.Start(new ProcessStartInfo(Manager.DataDirectory) { UseShellExecute = true }));
    private async void CheckUpdateClick(object sender, RoutedEventArgs e) { try { await _host.Updates.CheckAsync(); RefreshUpdate(); } catch (Exception ex) { _host.ReportError(ex); } }
    private async void ApplyUpdateClick(object sender, RoutedEventArgs e) { try { await _host.ApplyUpdateAsync(); } catch (Exception ex) { _host.ReportError(ex); } }
    private void Run(Action action) { try { action(); } catch (Exception ex) { _host.ReportError(ex); } }
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    public static string Duration(double seconds)
    {
        var minutes = Math.Max(0, (int)Math.Floor(seconds / 60));
        return minutes >= 60 ? $"{minutes / 60}시간 {minutes % 60}분" : minutes == 0 && seconds > 0 ? $"{(int)seconds}초" : $"{minutes}분";
    }
    private sealed record MidiChoice(string? Id, string Name);
}
