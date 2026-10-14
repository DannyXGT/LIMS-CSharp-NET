using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Numerics;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Markup;
using Windows.Foundation;
using Windows.System;

namespace Lims.DesignSystem.Controls;

/// <summary>Content-sized sibling navigation with one animated accent underline and a single Tab stop.</summary>
[ContentProperty(Name = nameof(Items))]
public sealed class LimsTabStrip : UserControl
{
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 24 };
    private readonly Border _indicator = new() { Width = 1, Height = 3, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
    private bool _positioned;
    private bool _changingSelection;
    public ObservableCollection<LimsTabItem> Items { get; } = [];
    public LimsTabItem? SelectedItem { get => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null; set => SelectedIndex = value is null ? -1 : Items.IndexOf(value); }
    public event EventHandler? SelectionChanged;
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(LimsTabStrip), new PropertyMetadata(0, OnSelectionChanged));
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }

    public LimsTabStrip()
    {
        Style = (Style)Application.Current.Resources["LimsTabRootStyle"];
        IsTabStop = false;
        var root = new Grid { MinHeight = 42 };
        root.Children.Add(_tabs); root.Children.Add(_indicator);
        var separator = new Border { Height = 1, Opacity = 0.35, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        separator.Style = (Style)Application.Current.Resources["LimsTabSeparatorStyle"];
        root.Children.Add(separator);
        Canvas.SetZIndex(_indicator, 1);
        Content = root;
        _indicator.Style = (Style)Application.Current.Resources["LimsTabIndicatorStyle"];
        ElementCompositionPreview.SetIsTranslationEnabled(_indicator, true);
        Items.CollectionChanged += OnItemsChanged;
        Loaded += (_, _) => UpdateIndicator(false);
        SizeChanged += (_, _) => { if (!_changingSelection) UpdateIndicator(false); };
        _tabs.SizeChanged += (_, _) => { if (!_changingSelection) UpdateIndicator(false); };
        Unloaded += (_, _) => _positioned = false;
        PreviewKeyDown += (_, e) =>
        {
            if (Items.Count == 0 || e.Key is not (VirtualKey.Left or VirtualKey.Right or VirtualKey.Home or VirtualKey.End)) return;
            var direction = (e.Key == VirtualKey.Right ? 1 : -1) * (FlowDirection == FlowDirection.RightToLeft ? -1 : 1);
            var index = e.Key == VirtualKey.Home ? 0 : e.Key == VirtualKey.End ? Items.Count - 1 : (SelectedIndex + direction + Items.Count) % Items.Count;
            for (var attempt = 0; attempt < Items.Count && !Items[index].IsEnabled; attempt++) index = (index + direction + Items.Count) % Items.Count;
            if (Items[index].IsEnabled) { SelectedIndex = index; Items[index].Focus(FocusState.Keyboard); }
            e.Handled = true;
        };
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        foreach (var child in _tabs.Children.OfType<LimsTabItem>()) child.Strip = null;
        _tabs.Children.Clear();
        foreach (var item in Items)
        {
            item.Strip = this;
            item.AlignLeadingContent(_tabs.Children.Count == 0);
            _tabs.Children.Add(item);
        }
        RefreshSelection(-1);
    }
    private static void OnSelectionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var strip = (LimsTabStrip)sender;
        strip._changingSelection = true;
        strip.RefreshSelection((int)args.OldValue);
        // Resolve text weight/width before targeting the shared indicator. Layout must not
        // snap the compositor to the destination midway through a selection animation.
        strip.UpdateLayout();
        strip._changingSelection = false;
        strip.UpdateIndicator(true);
        strip.SelectionChanged?.Invoke(strip, EventArgs.Empty);
    }
    private void RefreshSelection(int previous)
    {
        for (var index = 0; index < Items.Count; index++)
        {
            Items[index].IsTabStop = index == SelectedIndex;
            Items[index].AnnounceSelection(index == previous);
        }
    }
    private void UpdateIndicator(bool animate)
    {
        var item = SelectedItem;
        _indicator.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        if (item is null || item.ActualWidth <= 0 || _indicator.ActualWidth <= 0) return;
        var visual = ElementCompositionPreview.GetElementVisual(_indicator);
        var x = (float)item.TransformToVisual((Grid)Content).TransformPoint(new Point()).X;
        // WinUI can round the nominal 1 DIP base to 0.8 DIP at 125% DPI.
        // Scale from the rendered base so the line spans the actual hit area.
        var scaleX = (float)(item.ActualWidth / _indicator.ActualWidth);
        if (animate && _positioned && Motion.Enabled)
        {
            var easing = visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0), new Vector2(0.2f, 1));
            Animate(visual, "Translation.X", x, easing);
            Animate(visual, "Scale.X", scaleX, easing);
        }
        else
        {
            visual.StopAnimation("Translation.X"); visual.StopAnimation("Scale.X");
            visual.Properties.InsertVector3("Translation", new Vector3(x, 0, 0));
            visual.Scale = new Vector3(scaleX, 1, 1);
        }
        _positioned = true;
    }
    private static void Animate(Visual visual, string property, float target, CompositionEasingFunction easing)
    {
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1, target, easing); animation.Duration = TimeSpan.FromMilliseconds(180);
        visual.StartAnimation(property, animation);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new StripPeer(this);
    private sealed class StripPeer(LimsTabStrip owner) : FrameworkElementAutomationPeer(owner), ISelectionProvider
    {
        protected override string GetClassNameCore() => nameof(LimsTabStrip);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Tab;
        protected override object GetPatternCore(PatternInterface patternInterface) => patternInterface == PatternInterface.Selection ? this : base.GetPatternCore(patternInterface);
        public bool CanSelectMultiple => false;
        public bool IsSelectionRequired => true;
        public IRawElementProviderSimple[] GetSelection() => owner.SelectedItem is { } item ? [ProviderFromPeer(FrameworkElementAutomationPeer.CreatePeerForElement(item))] : [];
    }
}
