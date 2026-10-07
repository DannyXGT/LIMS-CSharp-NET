using System.Runtime.CompilerServices;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace Lims.DesignSystem.Controls;

/// <summary>One anchored popup per XamlRoot. Uses only public layout, focus and Popup APIs.</summary>
public abstract class LimsPopupField : Button
{
    private sealed class ActivePopup { public LimsPopupField? Field { get; set; } }
    private static readonly ConditionalWeakTable<XamlRoot, ActivePopup> Active = new();
    private readonly Popup _popup = new() { ShouldConstrainToRootBounds = true };
    private readonly Canvas _dismissLayer = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly TextBlock _label = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly LimsIcon _icon = new() { Icon = LimsIconKind.ChevronDown, VerticalAlignment = VerticalAlignment.Center };
    private long _version;
    private bool _closing;
    protected LimsPopupSurface Surface { get; } = new();
    public bool IsPopupOpen => _popup.IsOpen && !_closing;
    public FrameworkElement? PopupBoundary { get; set; }
    public PopupPlacement Placement { get; private set; }

    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(LimsPopupField), new PropertyMetadata("Seleccione", OnLabelChanged));
    public string PlaceholderText { get => (string)GetValue(PlaceholderTextProperty); set => SetValue(PlaceholderTextProperty, value); }
    public static readonly DependencyProperty PopupMaxHeightProperty = DependencyProperty.Register(
        nameof(PopupMaxHeight), typeof(double), typeof(LimsPopupField), new PropertyMetadata(340d));
    public double PopupMaxHeight { get => (double)GetValue(PopupMaxHeightProperty); set => SetValue(PopupMaxHeightProperty, value); }

    protected LimsPopupField()
    {
        Style = (Style)Application.Current.Resources["LimsPopupFieldStyle"];
        _icon.Size = (double)Application.Current.Resources["IconSizeSmall"];
        var content = new Grid { ColumnSpacing = 10 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(_label);
        Grid.SetColumn(_icon, 1);
        content.Children.Add(_icon);
        Content = content;
        Loaded += (_, _) => UpdateLabel();
        ActualThemeChanged += (_, _) => UpdateLabel();
        _dismissLayer.Children.Add(Surface);
        _popup.Child = _dismissLayer;
        _dismissLayer.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, _dismissLayer)) ClosePopup(true);
        };
        Click += (_, _) => { if (IsPopupOpen) ClosePopup(); else OpenPopup(); };
        Unloaded += (_, _) => CloseImmediately();
        IsEnabledChanged += (_, _) => { if (!IsEnabled) CloseImmediately(); };
        SizeChanged += (_, _) => { if (IsPopupOpen) CloseImmediately(); };
        LostFocus += OnFocusLost;
        Surface.LostFocus += OnFocusLost;
        Surface.AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnPopupKeyDown), true);
        _popup.Closed += (_, _) => FinishClosed();
    }

    private static void OnLabelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((LimsPopupField)sender).UpdateLabel();
    protected abstract string? SelectedLabel { get; }
    protected abstract UIElement BuildPopup();
    protected abstract void FocusPopup();
    protected virtual double DesiredPopupWidth => ActualWidth;
    protected void SetIcon(LimsIconKind icon) => _icon.Icon = icon;
    protected void UpdateLabel()
    {
        _label.Text = SelectedLabel ?? PlaceholderText;
        // A placeholder has its own semantic brush; selected text inherits the field foreground.
        if (SelectedLabel is null)
            _label.Style = (Style)Application.Current.Resources["LimsPlaceholderTextStyle"];
        else
            _label.ClearValue(StyleProperty);
        AutomationProperties.SetHelpText(this, _label.Text);
    }

    public void OpenPopup()
    {
        if (!IsEnabled || XamlRoot is null || ActualWidth <= 0) return;
        var active = Active.GetOrCreateValue(XamlRoot);
        if (active.Field != this) active.Field?.CloseImmediately();
        active.Field = this;
        _version++;
        _closing = false;
        _dismissLayer.IsHitTestVisible = true;
        Surface.RequestedTheme = ActualTheme;
        Surface.Content = null;
        Surface.Content = BuildPopup();
        var root = XamlRoot.Content;
        var origin = TransformToVisual(root).TransformPoint(new Point(0, 0));
        var top = 8d;
        var bottom = XamlRoot.Size.Height - 8;
        if (PopupBoundary is { IsLoaded: true } boundary)
        {
            var boundaryOrigin = boundary.TransformToVisual(root).TransformPoint(new Point(0, 0));
            // The footer is protected; the calendar may use the space above the form when needed.
            bottom = Math.Min(bottom, boundaryOrigin.Y + boundary.ActualHeight);
        }
        Surface.Width = Math.Min(DesiredPopupWidth, XamlRoot.Size.Width - 16);
        Surface.MaxHeight = PopupMaxHeight;
        Surface.Measure(new Size(Surface.Width, PopupMaxHeight));
        Placement = PopupLayout.Place(origin.X, origin.Y, ActualWidth, ActualHeight,
            Surface.Width, Math.Min(Surface.DesiredSize.Height, PopupMaxHeight), XamlRoot.Size.Width, top, bottom);
        Surface.Width = Placement.Width;
        Surface.MaxHeight = Placement.Height;
        _popup.XamlRoot = XamlRoot;
        _dismissLayer.Width = XamlRoot.Size.Width;
        _dismissLayer.Height = XamlRoot.Size.Height;
        Canvas.SetLeft(Surface, Placement.X);
        Canvas.SetTop(Surface, Placement.Y);
        _popup.IsOpen = true;
        XamlRoot.Changed -= OnRootChanged;
        XamlRoot.Changed += OnRootChanged;
        var version = _version;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (version != _version || !IsPopupOpen) return;
            Motion.Enter(Surface, y: 6, milliseconds: InteractionMotion.PopupDuration(true, Motion.Enabled), scale: 0.98f);
            FocusPopup();
        });
        NotifyExpanded();
    }

    public async void ClosePopup(bool restoreFocus = false)
    {
        if (!_popup.IsOpen || _closing) return;
        _closing = true;
        _dismissLayer.IsHitTestVisible = false;
        var version = ++_version;
        if (restoreFocus) Focus(FocusState.Programmatic);
        await Motion.ExitAsync(Surface, InteractionMotion.PopupDuration(false, Motion.Enabled));
        if (version == _version) CloseImmediately();
    }

    public void CloseImmediately()
    {
        _version++;
        _popup.IsOpen = false;
        FinishClosed();
    }

    private void FinishClosed()
    {
        _closing = false;
        if (XamlRoot is not null)
        {
            XamlRoot.Changed -= OnRootChanged;
            if (Active.TryGetValue(XamlRoot, out var active) && active.Field == this) active.Field = null;
        }
        NotifyExpanded();
    }

    public static void CloseForRoot(XamlRoot? root)
    {
        if (root is not null && Active.TryGetValue(root, out var active)) active.Field?.CloseImmediately();
    }

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => CloseImmediately();
    private void OnFocusLost(object sender, RoutedEventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!IsPopupOpen || XamlRoot is null) return;
        var focus = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        if (!Contains(this, focus) && !Contains(Surface, focus)) ClosePopup();
    });

    private static bool Contains(DependencyObject root, DependencyObject? child)
    {
        while (child is not null)
        {
            if (child == root) return true;
            child = VisualTreeHelper.GetParent(child);
        }
        return false;
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Enter or VirtualKey.Space or VirtualKey.Down or VirtualKey.Up)
        {
            OpenPopup();
            e.Handled = true;
            return;
        }
        if (e.Key == VirtualKey.Escape && IsPopupOpen) { ClosePopup(true); e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    protected virtual void OnPopupKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { ClosePopup(true); e.Handled = true; }
    }

    protected void LeavePopup(bool backwards)
    {
        CloseImmediately();
        Focus(FocusState.Keyboard);
        FocusManager.TryMoveFocus(backwards ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next,
            new FindNextElementOptions { SearchRoot = XamlRoot.Content });
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PopupFieldPeer(this);
    private void NotifyExpanded()
    {
        if (FrameworkElementAutomationPeer.FromElement(this) is PopupFieldPeer peer) peer.NotifyExpanded();
    }

    private sealed class PopupFieldPeer(LimsPopupField owner) : ButtonAutomationPeer(owner), IExpandCollapseProvider, IValueProvider
    {
        protected override string GetClassNameCore() => owner.GetType().Name;
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ComboBox;
        protected override object GetPatternCore(PatternInterface pattern) =>
            pattern is PatternInterface.ExpandCollapse or PatternInterface.Value ? this : base.GetPatternCore(pattern);
        public ExpandCollapseState ExpandCollapseState => owner.IsPopupOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        public void Expand() => owner.OpenPopup();
        public void Collapse() => owner.ClosePopup(true);
        public bool IsReadOnly => true;
        public string Value => owner.SelectedLabel ?? string.Empty;
        public void SetValue(string value) => throw new InvalidOperationException("Seleccione un valor de la lista.");
        public void NotifyExpanded() => RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
            owner.IsPopupOpen ? ExpandCollapseState.Collapsed : ExpandCollapseState.Expanded, ExpandCollapseState);
    }
}
