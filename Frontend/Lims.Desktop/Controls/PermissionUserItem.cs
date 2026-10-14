using System.ComponentModel;
using Lims.Desktop.Presentation;
using Lims.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lims.Desktop.Controls;

/// <summary>Flat user row with independent animated hover and a persistent selection accent.</summary>
public sealed class PermissionUserItem : UserControl
{
    private readonly Border _hover = new() { CornerRadius = new(8) };
    private readonly Border _selected = new() { CornerRadius = new(8) };
    private readonly Border _indicator = new() { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new(2), Margin = new(0, 12, 0, 12) };
    private readonly Border _avatar = new() { Width = 34, Height = 34, CornerRadius = new(17), VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _initials = new() { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _name = new() { FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _email = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _role = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _summary = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
    private ListViewItem? _owner;
    private long _selectionToken;
    private PermissionUserRow? _observed;
    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(nameof(Row), typeof(PermissionUserRow), typeof(PermissionUserItem), new PropertyMetadata(null, OnRowChanged));
    public PermissionUserRow? Row { get => (PermissionUserRow?)GetValue(RowProperty); set => SetValue(RowProperty, value); }
    public PermissionUserItem()
    {
        IsTabStop = false;
        var root = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Margin = new(0, 2, 0, 2) };
        root.Children.Add(_hover); root.Children.Add(_selected); root.Children.Add(_indicator);
        var body = new Grid { Padding = new(12, 11, 10, 11), ColumnSpacing = 10 };
        body.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); body.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        _avatar.Child = _initials; body.Children.Add(_avatar);
        var identity = new StackPanel { Spacing = 3 }; identity.Children.Add(_name); identity.Children.Add(_email); identity.Children.Add(_role); identity.Children.Add(_summary);
        Grid.SetColumn(identity, 1); body.Children.Add(identity); root.Children.Add(body); Content = root;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        ActualThemeChanged += (_, _) => Theme();
        PointerEntered += (_, _) => PermissionsMotion.Opacity(_hover, 1, 120);
        PointerExited += (_, _) => PermissionsMotion.Opacity(_hover, 0, 120);
    }
    private static void OnRowChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var item = (PermissionUserItem)sender;
        item.Unobserve(); item.Update();
        if (item.IsLoaded) item.Observe();
    }
    private void Attach()
    {
        Detach(); Observe(); Theme(); Update();
        DependencyObject? parent = this;
        while (parent is not null && parent is not ListViewItem) parent = VisualTreeHelper.GetParent(parent);
        _owner = parent as ListViewItem;
        if (_owner is not null) _selectionToken = _owner.RegisterPropertyChangedCallback(ListViewItem.IsSelectedProperty, (_, _) => UpdateSelection(true));
        UpdateSelection(false);
    }
    private void Observe() { _observed = Row; if (_observed is not null) _observed.PropertyChanged += OnSummaryChanged; }
    private void Unobserve() { if (_observed is not null) _observed.PropertyChanged -= OnSummaryChanged; _observed = null; }
    private void OnSummaryChanged(object? sender, PropertyChangedEventArgs e) => Update();
    private void Detach()
    {
        Unobserve();
        if (_owner is not null) _owner.UnregisterPropertyChangedCallback(ListViewItem.IsSelectedProperty, _selectionToken);
        _owner = null;
    }
    private void Theme()
    {
        Brush Resource(string name) => (Brush)Application.Current.Resources[name];
        _hover.Background = Resource("LimsSurfaceSecondaryBrush"); _selected.Background = Resource("LimsAccentSoftBrush"); _indicator.Background = Resource("LimsAccentBrush");
        _avatar.Background = Resource("LimsSurfaceSecondaryBrush"); _initials.Foreground = Resource("LimsTextSecondaryBrush"); _name.Foreground = Resource("LimsTextPrimaryBrush");
        _email.Foreground = Resource("LimsTextTertiaryBrush"); _role.Foreground = Resource("LimsTextSecondaryBrush"); _summary.Foreground = Resource("LimsTextTertiaryBrush");
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(_hover).Opacity = 0;
    }
    private void Update()
    {
        _initials.Text = Row?.Initials ?? string.Empty; _name.Text = Row?.User.Name ?? string.Empty; _email.Text = Row?.User.Email ?? string.Empty;
        _role.Text = Row?.User.Role ?? string.Empty; _summary.Text = Row?.Summary ?? string.Empty;
        ToolTipService.SetToolTip(this, Row is null ? null : $"{Row.User.Name}\n{Row.User.Email}\n{Row.User.Department}\n{Row.Summary}");
    }
    private void UpdateSelection(bool animate)
    {
        var selected = _owner?.IsSelected == true;
        if (animate) PermissionsMotion.Opacity(_selected, selected ? 1 : 0, 120);
        else Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(_selected).Opacity = selected ? 1 : 0;
        _indicator.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        _initials.Foreground = (Brush)Application.Current.Resources[selected ? "LimsAccentBrush" : "LimsTextSecondaryBrush"];
    }
}
