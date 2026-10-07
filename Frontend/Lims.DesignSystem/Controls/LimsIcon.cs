using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Lims.DesignSystem.Controls;

/// <summary>A decorative vector with a square optical box and colors from the design system.</summary>
public sealed class LimsIcon : UserControl
{
    private readonly Grid _box = new();
    private IconElement? _element;
    private readonly IconInteraction _interaction;
    private static readonly ConditionalWeakTable<DependencyObject, IconInteraction> Slots = new();

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(LimsIconKind), typeof(LimsIcon), new PropertyMetadata(LimsIconKind.None, OnArtworkChanged));
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(LimsIcon), new PropertyMetadata(20d, OnArtworkChanged));
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected), typeof(bool), typeof(LimsIcon), new PropertyMetadata(false, OnStateChanged));
    public static readonly DependencyProperty IsDangerProperty = DependencyProperty.Register(
        nameof(IsDanger), typeof(bool), typeof(LimsIcon), new PropertyMetadata(false, OnStateChanged));

    // WinUI icon slots require IconElement rather than UserControl. This bridge uses the same
    // catalog and state handling without replacing NavigationViewItem's native presenter.
    public static readonly DependencyProperty NavigationIconProperty = DependencyProperty.RegisterAttached(
        "NavigationIcon", typeof(LimsIconKind), typeof(LimsIcon), new PropertyMetadata(LimsIconKind.None, OnSlotChanged));
    public static LimsIconKind GetNavigationIcon(DependencyObject target) => (LimsIconKind)target.GetValue(NavigationIconProperty);
    public static void SetNavigationIcon(DependencyObject target, LimsIconKind value) => target.SetValue(NavigationIconProperty, value);

    public LimsIconKind Icon { get => (LimsIconKind)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public bool IsDanger { get => (bool)GetValue(IsDangerProperty); set => SetValue(IsDangerProperty, value); }

    public LimsIcon()
    {
        Content = _box;
        IsTabStop = false;
        IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        _interaction = new IconInteraction(this, brush => { if (_element is not null) _element.Foreground = brush; },
            () => IsEnabled, () => IsSelected, () => IsDanger,
            () => ReadLocalValue(ForegroundProperty) == DependencyProperty.UnsetValue ? null : Foreground);
        RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => _interaction.Refresh());
        IsEnabledChanged += (_, _) => _interaction.Refresh();
        UpdateArtwork();
    }

    private static void OnArtworkChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((LimsIcon)sender).UpdateArtwork();
    private static void OnStateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((LimsIcon)sender)._interaction.Refresh();

    private void UpdateArtwork()
    {
        if (!double.IsFinite(Size) || Size <= 0) throw new ArgumentOutOfRangeException(nameof(Size));
        Width = Height = Size;
        _box.Children.Clear();
        _element = LimsIconCatalog.CreateElement(Icon, Size);
        _box.Children.Add(_element);
        _interaction?.Refresh();
    }

    private static void OnSlotChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not Control control || target is not (NavigationViewItem or MenuFlyoutItem or AutoSuggestBox))
            throw new ArgumentException("NavigationIcon requires a native WinUI icon slot.", nameof(target));
        if (Slots.TryGetValue(target, out var previous)) { previous.Release(); Slots.Remove(target); }
        var size = (double)Application.Current.Resources["IconSizeMedium"];
        var element = LimsIconCatalog.CreateElement((LimsIconKind)args.NewValue, size);
        if (target is NavigationViewItem navigation) navigation.Icon = element;
        else if (target is MenuFlyoutItem menu) menu.Icon = element;
        else ((AutoSuggestBox)target).QueryIcon = element;
        Slots.Add(target, new IconInteraction(control, brush => element.Foreground = brush));
    }

    /// <summary>Observe public parent interaction properties; detach on unload, including flyouts.</summary>
    private sealed class IconInteraction
    {
        private readonly FrameworkElement _target;
        private readonly Action<Brush> _apply;
        private readonly Func<bool>? _enabled;
        private readonly Func<bool>? _selected;
        private readonly Func<bool>? _danger;
        private readonly Func<Brush?>? _foreground;
        private readonly PointerEventHandler _enteredHandler;
        private readonly PointerEventHandler _exitedHandler;
        private Control? _owner;
        private bool _hovered;
        private long _selectedToken;
        private long _foregroundToken;

        public IconInteraction(FrameworkElement target, Action<Brush> apply, Func<bool>? enabled = null,
            Func<bool>? selected = null, Func<bool>? danger = null, Func<Brush?>? foreground = null)
        {
            _target = target; _apply = apply; _enabled = enabled; _selected = selected; _danger = danger; _foreground = foreground;
            _enteredHandler = OnEntered;
            _exitedHandler = OnExited;
            target.Loaded += OnLoaded;
            target.Unloaded += OnUnloaded;
            target.ActualThemeChanged += OnThemeChanged;
            if (target.IsLoaded) Attach();
        }

        private void OnLoaded(object sender, RoutedEventArgs args) => Attach();
        private void OnUnloaded(object sender, RoutedEventArgs args) => Detach();
        private void OnThemeChanged(FrameworkElement sender, object args) => Refresh();
        private void Attach()
        {
            Detach();
            DependencyObject? candidate = _target is LimsIcon ? VisualTreeHelper.GetParent(_target) : _target;
            while (candidate is not null)
            {
                if (candidate is ButtonBase or NavigationViewItem or MenuFlyoutItem) { _owner = (Control)candidate; break; }
                candidate = VisualTreeHelper.GetParent(candidate);
            }
            if (_owner is not null)
            {
                // Native buttons may consume pointer events before ordinary subscriptions.
                _owner.AddHandler(UIElement.PointerEnteredEvent, _enteredHandler, true);
                _owner.AddHandler(UIElement.PointerExitedEvent, _exitedHandler, true);
                _owner.AddHandler(UIElement.PointerCanceledEvent, _exitedHandler, true);
                _owner.IsEnabledChanged += OnEnabledChanged;
                _foregroundToken = _owner.RegisterPropertyChangedCallback(Control.ForegroundProperty, (_, _) => Refresh());
                if (_owner is NavigationViewItem navigation)
                    _selectedToken = navigation.RegisterPropertyChangedCallback(NavigationViewItem.IsSelectedProperty, (_, _) => Refresh());
            }
            Refresh();
        }

        private void OnEntered(object sender, PointerRoutedEventArgs args) { _hovered = true; Refresh(); }
        private void OnExited(object sender, PointerRoutedEventArgs args) { _hovered = false; Refresh(); }
        private void OnEnabledChanged(object sender, DependencyPropertyChangedEventArgs args) => Refresh();

        public void Refresh()
        {
            var disabled = _enabled?.Invoke() == false || _owner?.IsEnabled == false;
            var selected = _selected?.Invoke() == true || _owner is NavigationViewItem { IsSelected: true };
            var danger = _danger?.Invoke() == true;
            var key = disabled ? "LimsIconDisabledBrush" : danger ? "LimsIconDangerBrush" : selected ? "LimsIconSelectedBrush"
                : _hovered ? "LimsIconHoverBrush" : "LimsIconNormalBrush";
            // An explicit foreground preserves e.g. on-accent contrast; semantic/disabled states win.
            var brush = !disabled && !selected && !danger ? _foreground?.Invoke() : null;
            _apply(brush ?? FindBrush(Application.Current.Resources, key, _target.ActualTheme));
        }

        private static Brush FindBrush(ResourceDictionary resources, string key, ElementTheme theme)
        {
            var themeKey = theme == ElementTheme.Light ? "Light" : "Dark";
            if (resources.ThemeDictionaries.TryGetValue(themeKey, out var themed) && themed is ResourceDictionary dictionary && dictionary.TryGetValue(key, out var value))
                return (Brush)value;
            foreach (var merged in resources.MergedDictionaries.Reverse())
            {
                if (merged.ContainsKey(key) || merged.ThemeDictionaries.Count > 0)
                {
                    try { return FindBrush(merged, key, theme); } catch (KeyNotFoundException) { }
                }
            }
            if (resources.TryGetValue(key, out var direct)) return (Brush)direct;
            throw new KeyNotFoundException($"Missing design-system resource: {key}");
        }

        private void Detach()
        {
            if (_owner is null) return;
            _owner.RemoveHandler(UIElement.PointerEnteredEvent, _enteredHandler);
            _owner.RemoveHandler(UIElement.PointerExitedEvent, _exitedHandler);
            _owner.RemoveHandler(UIElement.PointerCanceledEvent, _exitedHandler);
            _owner.IsEnabledChanged -= OnEnabledChanged;
            _owner.UnregisterPropertyChangedCallback(Control.ForegroundProperty, _foregroundToken);
            if (_owner is NavigationViewItem navigation) navigation.UnregisterPropertyChangedCallback(NavigationViewItem.IsSelectedProperty, _selectedToken);
            _owner = null; _hovered = false;
        }

        public void Release()
        {
            Detach();
            _target.Loaded -= OnLoaded;
            _target.Unloaded -= OnUnloaded;
            _target.ActualThemeChanged -= OnThemeChanged;
        }
    }
}
