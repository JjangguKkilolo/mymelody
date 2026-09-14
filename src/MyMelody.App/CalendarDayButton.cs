using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace MyMelody.App;

internal sealed class CalendarDayButton : RadioButton
{
    private readonly TextBlock _day;
    private readonly TextBlock _duration;
    public DateOnly Date { get; }
    public bool IsInRange { get; private set; }

    public CalendarDayButton(DateOnly date)
    {
        Date = date;
        GroupName = "PracticeCalendarDate";
        Style = (Style)FindResource("CalendarDayStyle");
        AutomationProperties.SetAutomationId(this, "practice-date-" + date.ToString("yyyy-MM-dd"));
        var content = new StackPanel();
        _day = new TextBlock { Text = date.Day.ToString(), HorizontalAlignment = HorizontalAlignment.Center };
        _duration = new TextBlock
        {
            FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(172, 112, 138)),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0)
        };
        content.Children.Add(_day); content.Children.Add(_duration); Content = content;
    }

    public void Update(double seconds, bool selected, bool inRange)
    {
        IsInRange = inRange;
        Background = inRange ? (Brush)FindResource("Pale") : Brushes.White;
        _day.FontWeight = Date == DateOnly.FromDateTime(DateTime.Today) ? FontWeights.Bold : FontWeights.Normal;
        _duration.Text = seconds > 0 ? MainWindow.Duration(seconds) : "·";
        IsChecked = selected;
        AutomationProperties.SetName(this, Date.ToString("yyyy년 M월 d일") +
            (seconds > 0 ? " · 연습 " + MainWindow.Duration(seconds) : " · 연습 기록 없음") +
            (inRange ? " · 조회 기간" : ""));
    }
}
