using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Lims.DesignSystem.Controls;

/// <summary>A transparent navigation tab with selection semantics, never an action button.</summary>
public sealed class LimsTabItem : UserControl
{
    private readonly TextBlock _label = new();
    private readonly TextBlock _count = new() { FontSize = 12 };
    private readonly LimsIcon _icon = new() { Size = 14 };
    private readonly Border _focus = new() { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), IsHitTestVisible = false };
    private readonly Grid _surface = new() { Padding = new Thickness(12, 0, 12, 0), MinHeight = 42, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private bool _hover;
    internal LimsTabStrip? Strip { get; set; }
    public bool IsSelected => Strip?.SelectedItem == this;

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(LimsTabItem), new PropertyMetadata(string.Empty, OnContentChanged));
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public static readonly DependencyProperty CountProperty = DependencyProperty.Register(nameof(Count), typeof(int?), typeof(LimsTabItem), new PropertyMetadata(null, OnContentChanged));
    public int? Count { get => (int?)GetValue(CountProperty); set => SetValue(CountProperty, value); }
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(LimsIconKind?), typeof(LimsTabItem), new PropertyMetadata(null, OnContentChanged));
    public LimsIconKind? Icon { get => (LimsIconKind?)GetValue(IconProperty); set => SetValue(IconProperty, value); }

    public LimsTabItem()
    {
        Style = (Style)Application.Current.Resources["LimsTabRootStyle"];
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(_icon); content.Children.Add(_label); content.Children.Add(_count);
        _surface.Children.Add(content);
        _focus.Margin = new Thickness(-5, -4, -5, -4);
        _surface.Children.Add(_focus);
        Content = _surface;
        Loaded += (_, _) => Refresh();
        ActualThemeChanged += (_, _) => Refresh();
        _surface.AddHandler(PointerEnteredEvent, new PointerEventHandler((_, _) => SetHovered(true)), true);
        _surface.AddHandler(PointerMovedEvent, new PointerEventHandler((_, _) => SetHovered(true)), true);
        _surface.PointerExited += (_, e) =>
        {
            var point = e.GetCurrentPoint(_surface).Position;
            if (point.X < 0 || point.X >= _surface.ActualWidth || point.Y < 0 || point.Y >= _surface.ActualHeight)
            { SetHovered(false); _surface.Opacity = 1; }
        };
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            CapturePointer(e.Pointer); _surface.Opacity = 0.82; e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            var point = e.GetCurrentPoint(this).Position;
            if (_surface.Opacity < 1 && point.X >= 0 && point.X <= ActualWidth && point.Y >= 0 && point.Y <= ActualHeight)
            { Select(); Focus(FocusState.Pointer); }
            _surface.Opacity = 1; ReleasePointerCapture(e.Pointer); e.Handled = true;
        };
        PointerCanceled += (_, _) => _surface.Opacity = 1;
        PointerCaptureLost += (_, _) => _surface.Opacity = 1;
        GotFocus += (_, _) => Refresh();
        LostFocus += (_, _) => { _surface.Opacity = 1; Refresh(); };
        KeyDown += (_, e) =>
        {
            if (e.Key is VirtualKey.Enter or VirtualKey.Space) { Select(); e.Handled = true; }
        };
    }

    private static void OnContentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((LimsTabItem)sender).Refresh();
    private void SetHovered(bool value) { if (_hover == value) return; _hover = value; Refresh(); }
    // The first label shares the page guide; its symmetric hit area extends into
    // the leading gutter by exactly the same padding used by every sibling.
    internal void AlignLeadingContent(bool first) =>
        Margin = first ? new Thickness(-_surface.Padding.Left, 0, 0, 0) : new Thickness(0);
    internal void Select() { if (Strip is not null && IsEnabled) Strip.SelectedIndex = Strip.Items.IndexOf(this); }
    internal void Refresh()
    {
        _label.Text = Label;
        _label.Style = (Style)Application.Current.Resources[IsSelected || _hover ? "LimsTabActiveTextStyle" : "LimsTabTextStyle"];
        _label.FontWeight = IsSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        _count.Text = Count?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
        _count.Visibility = Count.HasValue ? Visibility.Visible : Visibility.Collapsed;
        _count.Style = (Style)Application.Current.Resources["LimsTabTextStyle"];
        _icon.Visibility = Icon.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (Icon is { } icon) _icon.Icon = icon;
        _icon.IsSelected = IsSelected;
        _focus.Style = (Style)Application.Current.Resources["LimsTabFocusStyle"];
        _focus.Visibility = FocusState == FocusState.Keyboard ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(this, Label);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TabPeer(this);
    internal void AnnounceSelection(bool previous)
    {
        Refresh();
        if (FrameworkElementAutomationPeer.FromElement(this) is TabPeer peer)
            peer.RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, previous, IsSelected);
    }

    private sealed class TabPeer(LimsTabItem owner) : FrameworkElementAutomationPeer(owner), ISelectionItemProvider
    {
        protected override string GetClassNameCore() => nameof(LimsTabItem);
        protected override string GetNameCore() => owner.Label;
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TabItem;
        protected override object GetPatternCore(PatternInterface patternInterface) => patternInterface == PatternInterface.SelectionItem ? this : base.GetPatternCore(patternInterface);
        public bool IsSelected => owner.IsSelected;
        public IRawElementProviderSimple SelectionContainer => ProviderFromPeer(FrameworkElementAutomationPeer.CreatePeerForElement(owner.Strip!));
        public void Select() => owner.Select();
        public void AddToSelection() => Select();
        public void RemoveFromSelection() { /* Exactly one sibling stays selected. */ }
    }
}
