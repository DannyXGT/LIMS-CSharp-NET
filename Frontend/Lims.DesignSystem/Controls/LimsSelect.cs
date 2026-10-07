using System.Collections;
using System.Collections.Specialized;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Lims.DesignSystem.Controls;

public class LimsSelect : LimsPopupField
{
    private readonly LocalSelectFilter _filter = new();
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true, MaxHeight = 288 };
    private readonly TextBlock _empty = new() { Text = "Sin resultados", Margin = new Thickness(10, 12, 10, 12) };
    private TextBox? _search;
    private Grid? _panel;
    private bool _syncing;
    public event EventHandler<EventArgs>? SelectionChanged;
    public event EventHandler<string>? SearchTextChanged;
    protected virtual bool SearchEnabled => false;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(LimsSelect), new PropertyMetadata(null, OnItemsChanged));
    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem), typeof(object), typeof(LimsSelect), new PropertyMetadata(null, OnSelectionChanged));
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public static readonly DependencyProperty SelectedValueProperty = DependencyProperty.Register(
        nameof(SelectedValue), typeof(object), typeof(LimsSelect), new PropertyMetadata(null, OnValueChanged));
    public object? SelectedValue { get => GetValue(SelectedValueProperty); set => SetValue(SelectedValueProperty, value); }
    public static readonly DependencyProperty SelectedValuePathProperty = DependencyProperty.Register(
        nameof(SelectedValuePath), typeof(string), typeof(LimsSelect), new PropertyMetadata("Id", OnPathChanged));
    public string SelectedValuePath { get => (string)GetValue(SelectedValuePathProperty); set => SetValue(SelectedValuePathProperty, value); }
    public static readonly DependencyProperty DisplayMemberPathProperty = DependencyProperty.Register(
        nameof(DisplayMemberPath), typeof(string), typeof(LimsSelect), new PropertyMetadata("", OnPathChanged));
    public string DisplayMemberPath { get => (string)GetValue(DisplayMemberPathProperty); set => SetValue(DisplayMemberPathProperty, value); }
    public static readonly DependencyProperty SearchPlaceholderProperty = DependencyProperty.Register(
        nameof(SearchPlaceholder), typeof(string), typeof(LimsSelect), new PropertyMetadata("Buscar..."));
    public string SearchPlaceholder { get => (string)GetValue(SearchPlaceholderProperty); set => SetValue(SearchPlaceholderProperty, value); }
    public int SelectedIndex
    {
        get => ItemsSource?.Cast<object>().ToList().IndexOf(SelectedItem!) ?? -1;
        set => SelectedItem = value < 0 ? null : ItemsSource?.Cast<object>().ElementAtOrDefault(value);
    }
    public bool IsDropDownOpen { get => IsPopupOpen; set { if (value) OpenPopup(); else CloseImmediately(); } }

    public LimsSelect()
    {
        _list.Style = (Style)Application.Current.Resources["LimsPopupListStyle"];
        _list.ItemsSource = _filter.Visible;
        _list.ItemClick += (_, e) => Commit(e.ClickedItem);
        Surface.AddHandler(CharacterReceivedEvent, new TypedEventHandler<UIElement, CharacterReceivedRoutedEventArgs>((_, e) =>
        {
            if (!SearchEnabled || _search is null || _search.FocusState != FocusState.Unfocused || e.Character < 32) return;
            _search.Focus(FocusState.Keyboard);
            _search.Text += e.Character;
            _search.SelectionStart = _search.Text.Length;
            e.Handled = true;
        }), true);
        UpdateLabel();
    }

    private string Label(object item) => Read(item, DisplayMemberPath)?.ToString() ?? string.Empty;
    private static object? Read(object? item, string path) => string.IsNullOrEmpty(path) ? item : item?.GetType().GetProperty(path)?.GetValue(item);
    protected override string? SelectedLabel => SelectedItem is null ? null : Label(SelectedItem);

    private static void OnItemsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var select = (LimsSelect)sender;
        if (args.OldValue is INotifyCollectionChanged oldCollection) oldCollection.CollectionChanged -= select.OnCollectionChanged;
        if (args.NewValue is INotifyCollectionChanged newCollection) newCollection.CollectionChanged += select.OnCollectionChanged;
        select.Reload();
    }
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Reload();
    private static void OnPathChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((LimsSelect)sender).Reload();
    private void Reload()
    {
        _list.DisplayMemberPath = DisplayMemberPath;
        _filter.Load(ItemsSource, Label);
        if (SelectedValue is not null && !_syncing) SelectValue();
        UpdateLabel();
    }
    private static void OnSelectionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var select = (LimsSelect)sender;
        if (!select._syncing)
        {
            select._syncing = true;
            select.SelectedValue = Read(args.NewValue, select.SelectedValuePath);
            select._syncing = false;
        }
        select.UpdateLabel();
        select.SelectionChanged?.Invoke(select, EventArgs.Empty);
    }
    private static void OnValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var select = (LimsSelect)sender;
        if (!select._syncing) select.SelectValue();
    }
    private void SelectValue()
    {
        _syncing = true;
        SelectedItem = ItemsSource?.Cast<object>().FirstOrDefault(item => Equals(Read(item, SelectedValuePath), SelectedValue));
        _syncing = false;
    }

    protected override UIElement BuildPopup()
    {
        _filter.Apply(string.Empty);
        _list.SelectedItem = SelectedItem;
        AutomationProperties.SetName(_list, AutomationProperties.GetName(this) + ": opciones");
        if (_panel is not null)
        {
            if (_search is not null) _search.Text = string.Empty;
            UpdateEmpty();
            return _panel;
        }
        var panel = new Grid { RowSpacing = 6 };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        if (SearchEnabled)
        {
            _search ??= CreateSearch();
            _search.PlaceholderText = SearchPlaceholder;
            _search.Text = string.Empty;
            AutomationProperties.SetName(_search, SearchPlaceholder);
            panel.Children.Add(_search);
        }
        Grid.SetRow(_list, 1);
        panel.Children.Add(_list);
        Grid.SetRow(_empty, 1);
        panel.Children.Add(_empty);
        UpdateEmpty();
        _panel = panel;
        return panel;
    }

    private TextBox CreateSearch()
    {
        var box = new TextBox { Style = (Style)Application.Current.Resources["LimsCompactTextBoxStyle"] };
        box.TextChanged += (_, _) =>
        {
            _filter.Apply(box.Text);
            _list.SelectedItem = _filter.Visible.Contains(SelectedItem!) ? SelectedItem : null;
            UpdateEmpty();
            SearchTextChanged?.Invoke(this, box.Text);
        };
        return box;
    }
    private void UpdateEmpty()
    {
        _empty.Visibility = _filter.Visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _list.Visibility = _filter.Visible.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    protected override void FocusPopup()
    {
        if (SearchEnabled) _search?.Focus(FocusState.Keyboard);
        else FocusList();
    }
    private void FocusList()
    {
        if (_list.Items.Count == 0) return;
        if (_list.SelectedIndex < 0) _list.SelectedIndex = 0;
        _list.ScrollIntoView(_list.SelectedItem);
        _list.UpdateLayout();
        if (_list.ContainerFromItem(_list.SelectedItem) is ListViewItem item) item.Focus(FocusState.Keyboard);
        else _list.Focus(FocusState.Keyboard);
    }
    private void Commit(object? item)
    {
        if (item is null) return;
        SelectedItem = item;
        ClosePopup(true);
    }

    protected override void OnPopupKeyDown(object sender, KeyRoutedEventArgs e)
    {
        base.OnPopupKeyDown(sender, e);
        if (e.Key == VirtualKey.Escape) return;
        var shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;
        var searchFocused = _search is not null && _search.FocusState != FocusState.Unfocused;
        if (e.Key == VirtualKey.Tab)
        {
            if (SearchEnabled && ((searchFocused && !shift) || (!searchFocused && shift)))
            {
                if (searchFocused && _list.Items.Count == 0) LeavePopup(false);
                else if (searchFocused) FocusList(); else _search!.Focus(FocusState.Keyboard);
            }
            else LeavePopup(shift);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Enter)
        {
            Commit(_list.SelectedItem ?? _filter.Visible.FirstOrDefault());
            e.Handled = true;
        }
        else if (searchFocused && e.Key is VirtualKey.Down or VirtualKey.Up)
        {
            _list.SelectedIndex = e.Key == VirtualKey.Up ? _list.Items.Count - 1 : 0;
            FocusList();
            e.Handled = true;
        }
        else if (!searchFocused && e.Key is VirtualKey.Home or VirtualKey.End)
        {
            _list.SelectedIndex = e.Key == VirtualKey.Home ? 0 : _list.Items.Count - 1;
            FocusList();
            e.Handled = true;
        }
    }
}
