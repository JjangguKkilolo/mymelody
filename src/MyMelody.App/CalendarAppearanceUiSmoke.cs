using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NativeCalendar = System.Windows.Controls.Calendar;

namespace MyMelody.App;

// Renders real controls offscreen. It never opens a DatePicker popup or changes practice data.
internal static class CalendarAppearanceUiSmoke
{
    public static void Run(AppHost host, string output, List<string> checks)
    {
        var window = host.Main;
        string originalData = JsonSerializer.Serialize(new { host.Manager.State, host.Manager.Settings, host.Manager.Sessions });
        var intensity = VerifyIntensity(output);
        window.Navigate("Records"); window.UpdateLayout();
        VerifyDatePicker(Element<DatePicker>(window, "StartDatePicker"), output, true);
        VerifyDatePicker(Element<DatePicker>(window, "EndDatePicker"), output, false);

        double previousWidth = window.Width, previousMinimum = window.MinWidth;
        var layouts = new List<object>();
        try
        {
            window.MinWidth = Math.Min(previousMinimum, 760);
            foreach (double width in new[] { previousWidth, 900d, 760d }.Distinct())
            {
                window.Width = width; window.Navigate("Records"); window.UpdateLayout();
                VerifyRecordsLayout(window);
                foreach (double scale in new[] { 1d, 1.5, 2 })
                {
                    var content = window.Content as FrameworkElement ?? throw new InvalidDataException("Window content is missing.");
                    SaveVisual(content, Path.Combine(output, $"calendar-layout-{width:0}-{scale * 100:0}.png"), scale);
                }
                layouts.Add(new
                {
                    windowWidth = window.ActualWidth,
                    calendarWidth = Element<Border>(window, "RecordsCalendarCard").ActualWidth,
                    rangeWidth = Element<Panel>(window, "RecordRangeControls").ActualWidth,
                    startPickerWidth = Element<DatePicker>(window, "StartDatePicker").ActualWidth,
                    endPickerWidth = Element<DatePicker>(window, "EndDatePicker").ActualWidth
                });
            }
        }
        finally
        {
            window.MinWidth = previousMinimum; window.Width = previousWidth;
            window.Navigate("Records"); window.UpdateLayout();
        }
        Require(JsonSerializer.Serialize(new { host.Manager.State, host.Manager.Settings, host.Manager.Sessions }) == originalData,
            "Calendar appearance checks changed practice, growth, display settings, or saved sessions.");
        File.WriteAllText(Path.Combine(output, "calendar-appearance-checks.json"), JsonSerializer.Serialize(new
        {
            success = true, intensity, layouts,
            dpiScales = new[] { 1d, 1.5, 2 },
            datePickerPartsPreserved = true, nativeCalendarModesAndNavigationPreserved = true,
            popupNeverOpened = true, practiceDataUnchanged = true
        }, new JsonSerializerOptions { WriteIndented = true }));
        checks.Add("Calendar activity colors become progressively darker at 0/15/30 minutes and 1/2/4 hours, cap at four hours, and remain unchanged by selection or range borders.");
        checks.Add("The themed DatePickers retain their native text, button, popup, and Calendar parts; month/year/decade navigation renders offscreen without opening a popup.");
        checks.Add("The records calendar header and date fields fit at normal, 900-DIP, and 760-DIP widths; appearance screenshots cover 100/150/200% rendering.");
    }

    private static object[] VerifyIntensity(string output)
    {
        double[] samples = [0, 15 * 60, 30 * 60, 3600, 7200, 14400];
        string[] labels = ["연습 없음", "15분", "30분", "1시간", "2시간", "4시간 이상"];
        var colors = samples.Select(CalendarDayButton.ActivityColor).ToArray();
        Require(colors[0] == Colors.White && colors.Distinct().Count() == samples.Length,
            "Activity colors must distinguish zero, short practice, and each longer duration sample.");
        for (int i = 1; i < colors.Length; i++)
            Require(Brightness(colors[i]) < Brightness(colors[i - 1]), "Longer practice must result in darker calendar colors.");
        Require(CalendarDayButton.ActivityColor(14400) == CalendarDayButton.ActivityColor(28800),
            "Calendar intensity must cap at four hours.");

        var root = new StackPanel { Width = 720, Background = new SolidColorBrush(Color.FromRgb(255, 251, 253)) };
        root.Children.Add(new TextBlock
        {
            Text = "차곡차곡 쌓이는 연습의 색", FontFamily = new FontFamily("맑은 고딕"), FontSize = 20,
            FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(98, 67, 79)), Margin = new Thickness(24, 20, 24, 7)
        });
        root.Children.Add(new TextBlock
        {
            Text = "오래 연습한 날일수록 진한 분홍색으로 남아요.", FontFamily = new FontFamily("맑은 고딕"), FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(158, 113, 132)), Margin = new Thickness(24, 0, 24, 15)
        });
        var row = new UniformGrid { Columns = samples.Length, Margin = new Thickness(18, 0, 18, 20) };
        var reports = new List<object>();
        for (int i = 0; i < samples.Length; i++)
        {
            var cell = new StackPanel { Margin = new Thickness(5, 0, 5, 0) };
            var button = new CalendarDayButton(new DateOnly(2026, 9, 1).AddDays(i))
                { Width = 92, GroupName = "CalendarIntensitySample" + i };
            foreach (var state in new (bool Selected, bool InRange)[] { (false, false), (true, true), (false, true) })
            {
                button.Update(samples[i], state.Selected, state.InRange);
                LayoutStandalone(button);
                Require(Part<Border>(button, "Chrome").Background is SolidColorBrush fill && fill.Color == colors[i],
                    "Selecting a sample or adding it to a range changed its practice-intensity fill.");
            }
            button.Update(samples[i], false, false);
            cell.Children.Add(button);
            cell.Children.Add(new TextBlock
            {
                Text = labels[i], FontFamily = new FontFamily("맑은 고딕"), FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(113, 82, 95)), Margin = new Thickness(0, 7, 0, 0)
            });
            row.Children.Add(cell);
            reports.Add(new { seconds = samples[i], color = colors[i].ToString(), brightness = Brightness(colors[i]) });
        }
        root.Children.Add(row);
        LayoutStandalone(root);
        SaveVisual(root, Path.Combine(output, "calendar-intensity.png"), 1);
        return reports.ToArray();
    }

    private static double Brightness(Color color) => 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;

    private static void VerifyDatePicker(DatePicker picker, string output, bool render)
    {
        picker.ApplyTemplate();
        Require(picker.Style == Application.Current.FindResource("PracticeDatePicker") && Math.Abs(picker.ActualHeight - 40) <= 0.5,
            "The records date field must use the forty-DIP themed DatePicker.");
        Part<Grid>(picker, "PART_Root");
        var textBox = Part<DatePickerTextBox>(picker, "PART_TextBox");
        var button = Part<Button>(picker, "PART_Button");
        var popup = Part<Popup>(picker, "PART_Popup");
        textBox.ApplyTemplate();
        Part<ScrollViewer>(textBox, "PART_ContentHost");
        Part<ContentControl>(textBox, "PART_Watermark");
        Require(!popup.IsOpen && !picker.IsDropDownOpen, "Appearance checks must not open a popup on the user's desktop.");
        var calendar = popup.Child as NativeCalendar
            ?? throw new InvalidDataException("The native DatePicker did not insert its Calendar into PART_Popup.");
        Require(calendar.Style == picker.CalendarStyle && calendar.CalendarItemStyle != null &&
            calendar.CalendarDayButtonStyle != null && calendar.CalendarButtonStyle != null,
            "The DatePicker's native Calendar must receive the complete themed calendar styles.");
        var originalMode = calendar.DisplayMode;
        var originalDate = calendar.DisplayDate;
        var originalSelected = picker.SelectedDate;
        try
        {
            calendar.DisplayMode = CalendarMode.Month;
            LayoutStandalone(calendar);
            Part<Panel>(calendar, "PART_Root");
            var item = Part<CalendarItem>(calendar, "PART_CalendarItem");
            item.ApplyTemplate();
            var header = Part<Button>(item, "PART_HeaderButton");
            var previous = Part<Button>(item, "PART_PreviousButton");
            var next = Part<Button>(item, "PART_NextButton");
            var month = Part<Grid>(item, "PART_MonthView");
            var year = Part<Grid>(item, "PART_YearView");
            Part<FrameworkElement>(item, "PART_DisabledVisual");
            Require(month.RowDefinitions.Count == 7 && month.ColumnDefinitions.Count == 7 &&
                year.RowDefinitions.Count == 3 && year.ColumnDefinitions.Count == 4,
                "The native Calendar requires its seven-by-seven day grid and four-by-three period grid.");
            Require(month.Children.OfType<System.Windows.Controls.Primitives.CalendarDayButton>().Count() == 42 &&
                year.Children.OfType<CalendarButton>().Count() == 12,
                "The native Calendar did not populate all day and period controls.");
            foreach (var mode in new[] { CalendarMode.Month, CalendarMode.Year, CalendarMode.Decade })
            {
                if (mode != CalendarMode.Month) header.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, header));
                LayoutStandalone(calendar);
                Require(calendar.DisplayMode == mode && (month.Visibility == Visibility.Visible) == (mode == CalendarMode.Month) &&
                    (year.Visibility == Visibility.Visible) == (mode != CalendarMode.Month),
                    "Calendar header navigation or month/period template visibility is broken.");
                int period = Period(calendar.DisplayDate, mode);
                next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, next)); LayoutStandalone(calendar);
                Require(Period(calendar.DisplayDate, mode) == period + 1, "The Calendar next-period button lost its native behavior.");
                previous.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, previous)); LayoutStandalone(calendar);
                Require(Period(calendar.DisplayDate, mode) == period, "The Calendar previous-period button lost its native behavior.");
                if (render)
                    foreach (double scale in new[] { 1d, 1.5, 2 })
                        SaveVisual(calendar, Path.Combine(output, $"date-picker-{mode.ToString().ToLowerInvariant()}-{scale * 100:0}.png"), scale);
            }
        }
        finally
        {
            calendar.DisplayDate = originalDate; calendar.DisplayMode = originalMode;
        }
        Require(picker.SelectedDate == originalSelected && !popup.IsOpen && !picker.IsDropDownOpen,
            "Offscreen Calendar navigation changed the selected date or opened a desktop popup.");
        RequireInside(textBox, picker, "DatePicker text field");
        RequireInside(button, picker, "DatePicker calendar button");
    }

    private static int Period(DateTime date, CalendarMode mode) => mode switch
    {
        CalendarMode.Month => date.Year * 12 + date.Month,
        CalendarMode.Year => date.Year,
        _ => date.Year / 10
    };

    private static void VerifyRecordsLayout(MainWindow window)
    {
        var card = Element<Border>(window, "RecordsCalendarCard");
        var title = Element<TextBlock>(window, "MonthTitle");
        var header = title.Parent as Panel ?? throw new InvalidDataException("Month header is missing.");
        RequireInside(header, card, "Calendar month header");
        RequireTextFits(title, "Calendar month title");
        foreach (var navigation in header.Children.OfType<Button>())
        {
            RequireInside(navigation, card, "Calendar navigation button");
            RequireControlTextFits(navigation, navigation.Content?.ToString() ?? "", "Calendar navigation text");
        }
        var total = Element<TextBlock>(window, "AllTimeTotal");
        RequireInside(total, card, "Practice total"); RequireTextFits(total, "Practice total text");
        var headerBounds = BoundsIn(header, card);
        var totalBounds = BoundsIn(total, card);
        Require(!headerBounds.IntersectsWith(totalBounds), "The month header overlaps the practice total at this window width.");
        foreach (var name in new[] { "StartDatePicker", "EndDatePicker" })
        {
            var picker = Element<DatePicker>(window, name);
            picker.ApplyTemplate();
            var field = Part<DatePickerTextBox>(picker, "PART_TextBox");
            RequireInside(picker, card, name); RequireInside(field, picker, name + " text");
            RequireInside(Part<Button>(picker, "PART_Button"), picker, name + " icon");
            RequireControlTextFits(field, field.Text, name + " date value");
            Require(Math.Abs(picker.ActualHeight - 40) <= 0.5, "Date-picker height changed in the narrow layout.");
        }
        var apply = Element<Button>(window, "ApplyRangeButton");
        RequireInside(apply, card, "Date-range apply button");
        RequireControlTextFits(apply, apply.Content?.ToString() ?? "", "Date-range apply text");
        foreach (var day in Element<Panel>(window, "CalendarGrid").Children.OfType<CalendarDayButton>())
        {
            RequireInside(day, card, "Calendar day");
            foreach (var label in Descendants(day).OfType<TextBlock>().Where(label => label.Visibility == Visibility.Visible && label.ActualHeight > 0))
            {
                RequireInside(label, day, "Calendar day/today/duration label");
                RequireTextFits(label, "Calendar day/today/duration text");
            }
        }
    }

    private static void RequireTextFits(TextBlock element, string description)
    {
        var text = new FormattedText(element.Text, CultureInfo.CurrentUICulture, element.FlowDirection,
            new Typeface(element.FontFamily, element.FontStyle, element.FontWeight, element.FontStretch),
            element.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(element).PixelsPerDip);
        Require(text.WidthIncludingTrailingWhitespace <= element.ActualWidth + 0.75 && text.Height <= element.ActualHeight + 0.75,
            description + " is clipped or unexpectedly wraps.");
    }

    private static void RequireControlTextFits(Control control, string content, string description)
    {
        var text = new FormattedText(content, CultureInfo.CurrentUICulture, control.FlowDirection,
            new Typeface(control.FontFamily, control.FontStyle, control.FontWeight, control.FontStretch),
            control.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(control).PixelsPerDip);
        double width = control.ActualWidth - control.Padding.Left - control.Padding.Right - control.BorderThickness.Left - control.BorderThickness.Right;
        Require(text.WidthIncludingTrailingWhitespace <= width + 0.75, description + " is clipped.");
    }

    private static Rect BoundsIn(FrameworkElement child, FrameworkElement parent)
        => new(child.TranslatePoint(new Point(), parent), new Size(child.ActualWidth, child.ActualHeight));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static void RequireInside(FrameworkElement child, FrameworkElement parent, string description)
    {
        var bounds = BoundsIn(child, parent);
        Require(bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -0.75 && bounds.Top >= -0.75 &&
            bounds.Right <= parent.ActualWidth + 0.75 && bounds.Bottom <= parent.ActualHeight + 0.75,
            description + " is outside its available bounds.");
    }

    private static T Part<T>(Control control, string name) where T : DependencyObject
    {
        control.ApplyTemplate();
        return control.Template.FindName(name, control) as T ?? throw new InvalidDataException($"Required {control.GetType().Name} template part {name} is missing.");
    }

    private static T Element<T>(MainWindow window, string name) where T : FrameworkElement
        => window.FindName(name) as T ?? throw new InvalidDataException($"Calendar control {name} is missing.");

    private static void LayoutStandalone(FrameworkElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(new Point(), element.DesiredSize)); element.UpdateLayout();
    }

    private static void SaveVisual(FrameworkElement element, string path, double scale)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * scale), (int)Math.Ceiling(element.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
