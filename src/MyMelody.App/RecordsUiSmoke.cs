using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
using MyMelody.Core;

namespace MyMelody.App;

// Uses the existing isolated UI fixture and real calendar events; it never adds practice data.
internal static class RecordsUiSmoke
{
    public static void Run(AppHost host, string output, List<string> checks)
    {
        var window = host.Main;
        var manager = host.Manager;
        var original = CaptureState(manager);
        var groups = manager.GetPracticeSessions().ToArray();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var month = groups.GroupBy(session => (session.LocalDate.Year, session.LocalDate.Month))
            .OrderByDescending(group => group.Select(session => session.LocalDate).Distinct().Count()).First();
        var dates = month.Select(session => session.LocalDate).Distinct().Order().ToArray();
        Require(dates.Length >= 4, "The records fixture needs at least four recorded dates in the same month.");
        // Keep recorded days outside both boundaries so an accidentally unfiltered list cannot pass.
        var dateA = dates[1];
        var dateB = dates[^2];

        window.Navigate("Records"); window.UpdateLayout();
        RequireFilter(window, groups, today, today);
        RequirePickerDates(window, today, today);
        ShowMonth(window, dateA);
        RequireFilter(window, groups, today, today);

        SelectDate(window, dateA);
        RequireFilter(window, groups, dateA, dateA);
        RequirePickerDates(window, dateA, dateA);
        RepeatSelectedDate(window, groups, dateA);
        SelectDate(window, dateB);
        RequireFilter(window, groups, dateB, dateB);
        RepeatSelectedDate(window, groups, dateB);
        var selectedRows = SessionRows(window);
        window.RefreshRecords(); window.UpdateLayout();
        RequireSameRows(selectedRows, SessionRows(window));
        window.PageScroll.ScrollToVerticalOffset(Math.Min(100, window.PageScroll.ScrollableHeight));
        window.UpdateLayout();
        SaveScreenshot(window, Path.Combine(output, "records-selected.png"));

        SetPickerDates(window, dateA, dateB);
        var beforeDraftRows = SessionRows(window);
        for (var i = 0; i < 3; i++)
        {
            window.RefreshRecords(); window.UpdateLayout();
            RequireFilter(window, groups, dateB, dateB);
            RequirePickerDates(window, dateA, dateB);
            RequireSameRows(beforeDraftRows, SessionRows(window));
        }
        ClickRange(window);
        RequireFilter(window, groups, dateA, dateB);
        var rangeRows = SessionRows(window);
        Require(rangeRows.Any(row => ((PracticeSessionGroup)row.Tag).LocalDate == dateA) &&
            rangeRows.Any(row => ((PracticeSessionGroup)row.Tag).LocalDate == dateB),
            "The applied date range must include sessions on both boundary dates.");
        ShowMonth(window, dateA.AddMonths(1));
        RequireFilter(window, groups, dateA, dateB);
        RequirePickerDates(window, dateA, dateB);
        RequireSameRows(rangeRows, SessionRows(window));
        ShowMonth(window, dateA);
        RequireFilter(window, groups, dateA, dateB);

        window.PageScroll.ScrollToVerticalOffset(Math.Min(180, window.PageScroll.ScrollableHeight));
        window.UpdateLayout();
        double rangeOffset = window.PageScroll.VerticalOffset;
        Require(rangeOffset > 0, "The date-range fixture must be scrollable for stable-refresh verification.");
        for (var i = 0; i < 3; i++)
        {
            window.RefreshRecords(); window.UpdateLayout();
            RequireFilter(window, groups, dateA, dateB);
            RequireSameRows(rangeRows, SessionRows(window));
            Require(Math.Abs(window.PageScroll.VerticalOffset - rangeOffset) <= 0.5,
                "Refreshing unchanged date-range records moved the scroll position.");
        }
        SaveScreenshot(window, Path.Combine(output, "records-range.png"));

        // Invalid drafts must not replace the last successfully applied query.
        var toastBorder = Element<Border>(window, "ToastBorder");
        var toastText = Element<TextBlock>(window, "ToastText");
        var originalToastVisibility = toastBorder.Visibility;
        var originalToastText = toastText.Text;
        foreach (var invalid in new (DateOnly? Start, DateOnly? End)[]
        {
            (dateB, dateA), (null, dateB), (dateA, null)
        })
        {
            SetPickerDates(window, invalid.Start, invalid.End);
            ClickRange(window);
            RequireFilter(window, groups, dateA, dateB);
            RequireSameRows(rangeRows, SessionRows(window));
            Require(toastBorder.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(toastText.Text),
                "A reversed or incomplete date range must report an error while preserving the applied filter.");
        }
        toastBorder.Visibility = originalToastVisibility;
        toastText.Text = originalToastText;
        SelectDate(window, dateA);
        RequireFilter(window, groups, dateA, dateA);
        RequirePickerDates(window, dateA, dateA);

        // Re-entering Records is an explicit fresh today query, unlike its periodic refresh.
        window.Navigate("Home"); window.Navigate("Records"); window.UpdateLayout();
        RequireFilter(window, groups, today, today);
        RequirePickerDates(window, today, today);
        Require(DayButtons(window).All(button => button.Date.Year == today.Year && button.Date.Month == today.Month),
            "Re-entering records did not return the calendar to the current month.");
        Require(CaptureState(manager) == original,
            "Calendar filtering changed practice sessions, growth, settings, or the saved SQLite application state.");

        File.WriteAllText(Path.Combine(output, "records-selection-checks.json"), JsonSerializer.Serialize(new
        {
            success = true, today, dateA, dateB,
            selectedSessionCount = selectedRows.Length, rangeSessionCount = rangeRows.Length,
            todaySessionCount = SessionRows(window).Length, repeatedSelectionStaysChecked = true,
            rangeIncludesBothBoundaries = true, draftDatesSurviveRefresh = true,
            invalidRangesPreserveAppliedFilter = true, monthNavigationPreservesQuery = true, reentryResetsToToday = true,
            rangeOffset, unchangedRefreshPreservesRows = true,
            originalPracticeSeconds = manager.TotalSeconds, originalRawSessionCount = manager.Sessions.Count,
            savedStateAndRawSessionsUnchanged = true
        }, new JsonSerializerOptions { WriteIndented = true }));
        checks.Add("Records opens and reopens on today only; clicking one calendar date replaces the query and repeated clicks keep that single date selected.");
        checks.Add("Date-range drafts survive refresh without changing the list; applying a range includes both boundary dates, highlights only its dates, and filters every read-only session row correctly.");
        checks.Add("Reversed or incomplete ranges report an error and keep the last applied query; unchanged refreshes preserve row instances and scroll position.");
        checks.Add("Calendar and range filtering leave original sessions, growth, settings, and SQLite application JSON unchanged.");
    }

    private static void SelectDate(MainWindow window, DateOnly date)
    {
        var button = DayButtons(window).Single(day => day.Date == date);
        Require(button.GroupName == "PracticeCalendarDate", "Calendar dates must share one radio-button selection group.");
        button.IsChecked = true; // Runs RadioButton's actual Checked handler.
        window.UpdateLayout();
    }

    private static void RepeatSelectedDate(MainWindow window, PracticeSessionGroup[] groups, DateOnly date)
    {
        for (var i = 0; i < 3; i++)
        {
            var button = DayButtons(window).Single(day => day.Date == date);
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            button.IsChecked = true;
            window.UpdateLayout();
            RequireFilter(window, groups, date, date);
        }
    }

    private static void SetPickerDates(MainWindow window, DateOnly? start, DateOnly? end)
    {
        Element<DatePicker>(window, "StartDatePicker").SelectedDate = start?.ToDateTime(TimeOnly.MinValue);
        Element<DatePicker>(window, "EndDatePicker").SelectedDate = end?.ToDateTime(TimeOnly.MinValue);
    }

    private static void RequirePickerDates(MainWindow window, DateOnly start, DateOnly end)
        => Require(Element<DatePicker>(window, "StartDatePicker").SelectedDate?.Date == start.ToDateTime(TimeOnly.MinValue) &&
            Element<DatePicker>(window, "EndDatePicker").SelectedDate?.Date == end.ToDateTime(TimeOnly.MinValue),
            "Refreshing records discarded date-picker drafts or entering records did not reset them to today.");

    private static void ClickRange(MainWindow window)
    {
        var button = Element<Button>(window, "ApplyRangeButton");
        Require(button.IsEnabled, "The date-range apply control must be enabled.");
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
        window.UpdateLayout();
    }

    private static void RequireFilter(MainWindow window, PracticeSessionGroup[] groups, DateOnly start, DateOnly end)
    {
        bool single = start == end;
        var buttons = DayButtons(window);
        var checkedDates = buttons.Where(button => button.IsChecked == true).Select(button => button.Date).ToArray();
        bool singleVisible = single && buttons.Any(button => button.Date == start);
        Require(singleVisible ? checkedDates.Length == 1 && checkedDates[0] == start : checkedDates.Length == 0,
            "The calendar must check one visible single-date query, and no individual date for a multi-day query.");
        foreach (var button in buttons)
        {
            button.ApplyTemplate();
            var marker = button.Template.FindName("SelectedMark", button) as TextBlock;
            var chrome = button.Template.FindName("Chrome", button) as Border;
            bool selected = single && button.Date == start;
            bool inRange = button.Date >= start && button.Date <= end;
            Require(button.IsInRange == inRange,
                "The calendar range highlight is inconsistent with the applied query.");
            Require(marker != null && (marker.Visibility == Visibility.Visible) == selected,
                "The check marker must identify a single selected date, not multiple days of a range.");
            Require(chrome?.Background is SolidColorBrush fill && (fill.Color == Colors.White) != (selected || inRange),
                "Only selected or in-range dates may have a colored calendar background.");
        }
        var label = Element<TextBlock>(window, "RecordsFilterLabel").Text;
        var expectedDateLabel = single ? start.ToString("yyyy년 M월 d일")
            : start.ToString("yyyy년 M월 d일") + " – " + end.ToString("yyyy년 M월 d일");
        Require(label.Contains(expectedDateLabel, StringComparison.Ordinal),
            "The visible records filter label does not identify the applied day or full date range.");
        var rows = SessionRows(window);
        Require(rows.All(row => row is Border && !row.Focusable && row.Tag is PracticeSessionGroup),
            "Practice session rows must remain read-only display rows with their matching session metadata.");
        var displayed = rows.Select(row => (PracticeSessionGroup)row.Tag).ToArray();
        var expected = groups.Where(session => session.LocalDate >= start && session.LocalDate <= end)
            .OrderByDescending(session => session.StartedAt).ToArray();
        Require(displayed.SequenceEqual(expected), "Displayed session rows do not exactly match the applied inclusive date range.");
        Require(label.Contains($"세션 {expected.Length}개", StringComparison.Ordinal),
            "The visible filter label reports a session count inconsistent with the displayed rows.");
        Require((Element<TextBlock>(window, "SessionEmpty").Visibility == Visibility.Visible) == (expected.Length == 0),
            "The empty-records message does not match the current date query.");
    }

    private static void RequireSameRows(FrameworkElement[] before, FrameworkElement[] after)
        => Require(before.Length == after.Length && before.Zip(after).All(pair => ReferenceEquals(pair.First, pair.Second)),
            "Refreshing unchanged practice records replaced their row controls.");

    private static CalendarDayButton[] DayButtons(MainWindow window)
        => Element<Panel>(window, "CalendarGrid").Children.OfType<CalendarDayButton>().ToArray();

    private static FrameworkElement[] SessionRows(MainWindow window)
        => Element<StackPanel>(window, "SessionsList").Children.Cast<FrameworkElement>().ToArray();

    private static void ShowMonth(MainWindow window, DateOnly target)
    {
        for (var attempt = 0; attempt < 24; attempt++)
        {
            var first = DayButtons(window).FirstOrDefault()
                ?? throw new InvalidDataException("The records calendar contains no date controls.");
            int difference = (target.Year - first.Date.Year) * 12 + target.Month - first.Date.Month;
            if (difference == 0) return;
            var controls = Element<TextBlock>(window, "MonthTitle").Parent as Panel
                ?? throw new InvalidDataException("The month navigation controls are missing.");
            var button = controls.Children.OfType<Button>().Single(item => Equals(item.Content, difference < 0 ? "‹" : "›"));
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            window.UpdateLayout();
        }
        throw new InvalidDataException("Could not navigate to the records fixture month.");
    }

    private sealed record SavedSnapshot(string State, string Settings, string Sessions, string SqliteJson);

    private static SavedSnapshot CaptureState(PracticeManager manager)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(manager.DataDirectory, "practice.sqlite"), Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM app_state WHERE id=1;";
        var json = command.ExecuteScalar() as string ?? throw new InvalidDataException("Saved application state is missing.");
        return new(JsonSerializer.Serialize(manager.State), JsonSerializer.Serialize(manager.Settings), JsonSerializer.Serialize(manager.Sessions), json);
    }

    private static T Element<T>(MainWindow window, string name) where T : FrameworkElement
        => window.FindName(name) as T ?? throw new InvalidDataException($"Records control {name} is missing.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static void SaveScreenshot(MainWindow window, string path)
    {
        window.UpdateLayout();
        var content = window.Content as FrameworkElement ?? throw new InvalidDataException("Window content is missing.");
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
