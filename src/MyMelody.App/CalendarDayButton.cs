using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace MyMelody.App;

internal sealed class CalendarDayButton : RadioButton
{
    private readonly TextBlock _day;
    private readonly TextBlock _duration;
    private readonly TextBlock _today;
    public DateOnly Date { get; }
    public bool IsInRange { get; private set; }

    public CalendarDayButton(DateOnly date)
    {
        Date = date;
        GroupName = "PracticeCalendarDate";
        Style = (Style)FindResource("CalendarDayStyle");
        AutomationProperties.SetAutomationId(this, "practice-date-" + date.ToString("yyyy-MM-dd"));
        var content = new StackPanel();
        var dayLine = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        _day = new TextBlock { Text = date.Day.ToString(), FontSize = 13, Foreground = new SolidColorBrush(date.DayOfWeek switch
            { DayOfWeek.Sunday => Color.FromRgb(188, 119, 148), DayOfWeek.Saturday => Color.FromRgb(147, 132, 175), _ => Color.FromRgb(92, 71, 81) }) };
        _today = new TextBlock { Text = "오늘", FontSize = 8, Foreground = new SolidColorBrush(Color.FromRgb(180, 111, 140)),
            Margin = new Thickness(4, 3, 0, 0), Visibility = Visibility.Collapsed };
        dayLine.Children.Add(_day); dayLine.Children.Add(_today);
        _duration = new TextBlock
        {
            FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(177, 137, 153)),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0)
        };
        content.Children.Add(dayLine); content.Children.Add(_duration); Content = content;
    }

    public void Update(double seconds, bool selected, bool inRange)
    {
        IsInRange = inRange;
        Background = new SolidColorBrush(ActivityColor(seconds));
        BorderBrush = inRange ? new SolidColorBrush(Color.FromRgb(210, 143, 174)) : Brushes.Transparent;
        _day.Foreground = new SolidColorBrush(seconds > 0 ? Color.FromRgb(80, 59, 69) : Date.DayOfWeek switch
            { DayOfWeek.Sunday => Color.FromRgb(188, 119, 148), DayOfWeek.Saturday => Color.FromRgb(147, 132, 175), _ => Color.FromRgb(92, 71, 81) });
        _duration.Foreground = new SolidColorBrush(Color.FromRgb(112, 72, 90));
        bool isToday = Date == DateOnly.FromDateTime(DateTime.Today);
        _day.FontWeight = isToday || selected ? FontWeights.SemiBold : FontWeights.Normal;
        _today.Visibility = isToday ? Visibility.Visible : Visibility.Collapsed;
        _duration.Text = seconds > 0 ? CompactDuration(seconds) : "";
        _duration.Visibility = seconds > 0 ? Visibility.Visible : Visibility.Collapsed;
        IsChecked = selected;
        AutomationProperties.SetName(this, Date.ToString("yyyy년 M월 d일") +
            (seconds > 0 ? " · 연습 " + MainWindow.Duration(seconds) : " · 연습 기록 없음") +
            (inRange ? " · 조회 기간" : ""));
        ToolTip = Date.ToString("M월 d일") + (isToday ? " · 오늘" : "") +
            (seconds > 0 ? "\n연습 " + MainWindow.Duration(seconds) : "\n아직 연습 기록이 없어요.");
    }

    // A fixed scale keeps the same practice time the same color across months.
    internal static Color ActivityColor(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return Colors.White;
        double amount = Math.Pow(Math.Clamp(seconds / (4 * 3600), 0, 1), 0.65);
        byte Mix(byte pale, byte deep) => (byte)Math.Round(pale + (deep - pale) * amount);
        return Color.FromRgb(Mix(255, 226), Mix(243, 149), Mix(247, 180));
    }

    private static string CompactDuration(double seconds)
    {
        int minutes = Math.Max(0, (int)(seconds / 60));
        return minutes >= 60 && minutes % 60 == 0 ? $"{minutes / 60}시간" : MainWindow.Duration(seconds);
    }
}
