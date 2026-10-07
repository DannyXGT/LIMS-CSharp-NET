using System.Globalization;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Lims.DesignSystem.Controls;

/// <summary>Preserves the editor's nullable local DateTimeOffset; never converts business dates to UTC.</summary>
public sealed class LimsDatePicker : LimsPopupField
{
    private readonly CalendarView _calendar = new()
    {
        Language = "es-GT", CalendarIdentifier = "GregorianCalendar", FirstDayOfWeek = Windows.Globalization.DayOfWeek.Monday,
        SelectionMode = CalendarViewSelectionMode.Single, NumberOfWeeksInView = 6, MinWidth = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private bool _syncing;
    private CalendarViewDayItem? _dayToFocus;
    public event EventHandler<EventArgs>? DateChanged;
    public static readonly DependencyProperty DateProperty = DependencyProperty.Register(
        nameof(Date), typeof(DateTimeOffset?), typeof(LimsDatePicker), new PropertyMetadata(null, OnDateChanged));
    public DateTimeOffset? Date { get => (DateTimeOffset?)GetValue(DateProperty); set => SetValue(DateProperty, value); }
    public bool IsCalendarOpen { get => IsPopupOpen; set { if (value) OpenPopup(); else CloseImmediately(); } }
    protected override string? SelectedLabel => Date?.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("es-GT"));
    protected override double DesiredPopupWidth => 296;

    public LimsDatePicker()
    {
        Language = "es-GT";
        PlaceholderText = "dd/MM/yyyy";
        SetIcon(LimsIconKind.Calendar);
        _calendar.Style = (Style)Application.Current.Resources["LimsPopupCalendarStyle"];
        _calendar.CalendarViewDayItemChanging += (_, e) =>
        {
            if (e.Item.Date.Date != (Date ?? DateTimeOffset.Now).Date) return;
            _dayToFocus = e.Item;
            DispatcherQueue.TryEnqueue(() => { if (IsPopupOpen) _dayToFocus?.Focus(FocusState.Keyboard); });
        };
        _calendar.SelectedDatesChanged += (_, e) =>
        {
            if (_syncing || e.AddedDates.Count == 0) return;
            Date = e.AddedDates[0];
            ClosePopup(true);
        };
        UpdateLabel();
    }
    private static void OnDateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var picker = (LimsDatePicker)sender;
        picker.UpdateLabel();
        picker.DateChanged?.Invoke(picker, EventArgs.Empty);
    }
    protected override UIElement BuildPopup()
    {
        _syncing = true;
        _calendar.SelectedDates.Clear();
        if (Date is { } date) { _calendar.SelectedDates.Add(date); _calendar.SetDisplayDate(date); }
        _syncing = false;
        AutomationProperties.SetName(_calendar, AutomationProperties.GetName(this));
        return _calendar;
    }
    protected override void FocusPopup()
    {
        _calendar.UpdateLayout();
        if (_dayToFocus is not null) _dayToFocus.Focus(FocusState.Keyboard);
        else _calendar.Focus(FocusState.Keyboard);
    }
    protected override void OnPopupKeyDown(object sender, KeyRoutedEventArgs e)
    {
        base.OnPopupKeyDown(sender, e);
        if (e.Key == VirtualKey.Tab)
        {
            LeavePopup((InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0);
            e.Handled = true;
        }
    }
}
