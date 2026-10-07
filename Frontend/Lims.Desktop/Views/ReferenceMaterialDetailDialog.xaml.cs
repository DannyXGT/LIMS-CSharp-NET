using Lims.Contracts.ReferenceMaterials;
using Lims.Desktop.Presentation;
using Lims.Desktop.ViewModels;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;

namespace Lims.Desktop.Views;

public sealed partial class ReferenceMaterialDetailDialog : UserControl
{
    private readonly ReferenceMaterialsViewModel _viewModel;
    private readonly Popup _popup = new() { IsLightDismissEnabled = false };
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private XamlRoot? _owner;
    private bool _closing;
    private bool _actionRunning;
    private bool _finished;
    private bool _availabilityInitialized;
    private double _availabilityTarget;
    private Storyboard? _availabilityAnimation;

    public ReferenceMaterialDetailDialog(ReferenceMaterialsViewModel viewModel)
    {
        _viewModel = viewModel;
        Detail = viewModel.SelectedDetail ?? throw new ArgumentException("Seleccione un estándar antes de abrir su ficha.", nameof(viewModel));
        InitializeComponent();
        _popup.Child = this;
        Refresh();
    }

    public ReferenceMaterialDetail Detail { get; private set; }
    public string CatalogValue => ReferenceMaterialPresentation.Optional(Detail.CatalogNumber);
    public string CasValue => ReferenceMaterialPresentation.Optional(Detail.CasNumber);
    public string PresentationValue => ReferenceMaterialPresentation.Quantity(Detail.PresentationQuantity, Detail.Unit);

    public Func<Task>? EditAsync { get; set; }
    public Func<Task>? ReplaceAsync { get; set; }
    public Func<Task>? ArchiveAsync { get; set; }

    public Task ShowAsync(XamlRoot owner)
    {
        _owner = owner;
        _popup.XamlRoot = owner;
        owner.Changed += OnRootChanged;
        Resize();
        _popup.IsOpen = true;
        UpdateLayout();
        Resize();
        UpdateLayout();
        Motion.Enter(Scrim, milliseconds: 200);
        Motion.Enter(DetailSurface, y: 10, milliseconds: 200, scale: 0.975f);
        CloseIconButton.Focus(FocusState.Programmatic);
        return _closed.Task;
    }

    public void ScrollTo(double offset) => DetailScroll.ChangeView(null, offset, null, true);

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Resize();
    private void Resize()
    {
        if (_owner is null) return;
        Width = _owner.Size.Width;
        Height = _owner.Size.Height;
        // Size and center against the usable client area in DIPs at every DPI.
        DetailSurface.Width = Width * 0.90;
        DetailSurface.Height = Height * 0.85;
        BreadcrumbName.MaxWidth = Math.Max(0, DetailSurface.Width - 200);
        var compactHeader = DetailSurface.Width < 900;
        HeroGrid.RowSpacing = compactHeader ? 12 : 0;
        Grid.SetRow(HeaderActions, compactHeader ? 1 : 0);
        Grid.SetColumn(HeaderActions, compactHeader ? 1 : 2);
        Grid.SetColumnSpan(IdentityHeader, compactHeader ? 2 : 1);
        HeaderActions.Margin = compactHeader ? new Thickness(0) : new Thickness(0, 8, 0, 0);
        IdentityChips.MaxWidth = Math.Max(0, DetailSurface.Width - 240 - (compactHeader ? 0 : 370));

        var bodyWidth = Math.Max(0, DetailSurface.Width - 50);
        var stacked = bodyWidth < 900;
        GeneralColumn.Width = new GridLength(stacked ? 1 : 38, GridUnitType.Star);
        WorkspaceColumn.Width = stacked ? new GridLength(0) : new GridLength(62, GridUnitType.Star);
        BodyColumns.ColumnSpacing = stacked ? 0 : 24;
        Grid.SetRow(RightWorkspace, stacked ? 1 : 0);
        Grid.SetColumn(RightWorkspace, stacked ? 0 : 1);
        var workspaceWidth = stacked ? bodyWidth : (bodyWidth - 24) * 0.62;
        var narrowKpis = workspaceWidth < 720;
        AvailabilityColumn.Width = new GridLength(narrowKpis ? 1 : 1.3, GridUnitType.Star);
        ExpiryColumn.Width = new GridLength(narrowKpis ? 1 : 1.1, GridUnitType.Star);
        StateColumn.Width = narrowKpis ? new GridLength(0) : new GridLength(0.8, GridUnitType.Star);
        KpiGrid.RowSpacing = narrowKpis ? 16 : 0;
        Grid.SetRowSpan(AvailabilityCard, narrowKpis ? 2 : 1);
        Grid.SetRow(StateCard, narrowKpis ? 1 : 0);
        Grid.SetColumn(StateCard, narrowKpis ? 1 : 2);
    }

    private void Refresh()
    {
        Bindings.Update();
        AutomationProperties.SetName(this, $"Ficha técnica de {Detail.NameDisplay}");
        IdentityCatalog.Text = $"Catálogo {Detail.CatalogNumber}";
        IdentityCatalog.Visibility = Detail.HasCatalog ? Visibility.Visible : Visibility.Collapsed;
        ExpiryHeadline.Text = ReferenceMaterialPresentation.Date(Detail.ExpirationDate);
        ReceivedValue.Text = ReferenceMaterialPresentation.Date(Detail.ReceivedDate);
        AvailabilityQuantity.Text = Detail.AvailableQuantity.HasValue ? Detail.AvailableDisplay : "No informada";
        AvailabilityQualifier.Text = Detail.AvailableQuantity.HasValue ? "disponibles" : string.Empty;
        AvailabilityTotals.Text = Detail.AvailableQuantity.HasValue ? $"{Detail.AvailableDisplay} disponibles de {Detail.TotalDisplay}" : $"Total inicial: {Detail.TotalDisplay}";
        AvailabilityPresentation.Text = $"Presentación: {Detail.PresentationDisplay}";
        AvailabilityQualifier.Visibility = Detail.AvailableQuantity.HasValue ? Visibility.Visible : Visibility.Collapsed;
        AvailabilityPercent.Text = Detail.AvailabilityPercentDisplay.Replace(" disponible", string.Empty, StringComparison.Ordinal);
        var percent = ReferenceMaterialVisuals.Availability(Detail.AvailableQuantity, Detail.PresentationQuantity * Detail.PackageCount);
        AvailabilityMeter.Visibility = percent.HasValue ? Visibility.Visible : Visibility.Collapsed;
        var availabilityBrush = Detail.StatusTone == "Danger" ? "LimsDangerBrush"
            : Detail.Status == "Depleted" ? "LimsWarningBrush"
            : Detail.AvailableQuantity > 0 ? "LimsSuccessBrush" : "LimsTextTertiaryBrush";
        AvailabilityProgress.Foreground = (Brush)Application.Current.Resources[availabilityBrush];
        UpdateAvailabilityProgress(percent ?? 0);
        EditButton.IsEnabled = _viewModel.CanEditSelected;
        ReplaceButton.IsEnabled = _viewModel.CanReplaceSelected;
        ArchiveButton.IsEnabled = _viewModel.CanArchiveSelected;
    }

    private void OnAvailabilitySizeChanged(object sender, SizeChangedEventArgs args) =>
        AvailabilityVial.Visibility = args.NewSize.Width < 280 ? Visibility.Collapsed : Visibility.Visible;

    private void UpdateAvailabilityProgress(double value)
    {
        // Preserve the previous visual value while the existing editor suspends the popup.
        _availabilityTarget = value;
        if (!_availabilityInitialized || !Motion.Enabled)
        {
            _availabilityAnimation?.Stop();
            _availabilityAnimation = null;
            AvailabilityProgress.Value = value;
            _availabilityInitialized = true;
        }
        else if (AvailabilityProgress.IsLoaded) AnimateAvailabilityProgress();
    }

    private void OnAvailabilityLoaded(object sender, RoutedEventArgs args) => AnimateAvailabilityProgress();

    private void AnimateAvailabilityProgress()
    {
        var previous = AvailabilityProgress.Value;
        _availabilityAnimation?.Stop();
        _availabilityAnimation = null;
        AvailabilityProgress.Value = _availabilityTarget;
        if (!Motion.Enabled || Math.Abs(previous - _availabilityTarget) < 0.001) return;
        var animation = new DoubleAnimation
        {
            From = previous, To = _availabilityTarget, Duration = TimeSpan.FromMilliseconds(200),
            EnableDependentAnimation = true, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, AvailabilityProgress);
        Storyboard.SetTargetProperty(animation, nameof(ProgressBar.Value));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) =>
        {
            if (_availabilityAnimation != storyboard) return;
            AvailabilityProgress.Value = _availabilityTarget;
            storyboard.Stop();
            _availabilityAnimation = null;
        };
        _availabilityAnimation = storyboard;
        storyboard.Begin();
    }

    private async void OnEdit(object sender, RoutedEventArgs e) => await RunActionAsync(EditAsync, EditButton);
    private async void OnReplace(object sender, RoutedEventArgs e) => await RunActionAsync(ReplaceAsync, ReplaceButton);
    private async void OnArchive(object sender, RoutedEventArgs e) => await RunActionAsync(ArchiveAsync, ArchiveButton);

    private async Task RunActionAsync(Func<Task>? action, Button returnFocus)
    {
        if (_actionRunning || _closing || action is null) return;
        _actionRunning = true;
        var id = Detail.Id;
        var offset = DetailScroll.VerticalOffset;
        // The existing ContentDialog editor/confirmation keeps its own focus scope.
        // Suspend this popup to avoid stacked input scopes, then resume the same sheet.
        _popup.IsOpen = false;
        try
        {
            await action();
            if (_finished) return;
            var row = _viewModel.Items.FirstOrDefault(item => item.Id == id);
            if (row is null) { CloseImmediately(); return; }
            if (_viewModel.SelectedDetail?.Id != id)
                await _viewModel.SelectAsync(row, CancellationToken.None);
            if (_viewModel.SelectedDetail is not { } updated) { CloseImmediately(); return; }
            Detail = updated;
            _viewModel.SelectedMaterial = row;
            Refresh();
            ActionError.Message = _viewModel.Message;
            ActionError.IsOpen = _viewModel.HasMessage;
            ActionError.Visibility = _viewModel.HasMessage ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ActionError.Message = "No se pudo conectar con el servicio LIMS. Intente nuevamente.";
            ActionError.IsOpen = true;
            ActionError.Visibility = Visibility.Visible;
        }
        finally
        {
            _actionRunning = false;
            if (!_finished)
            {
                Resize();
                _popup.IsOpen = true;
                UpdateLayout();
                ScrollTo(offset);
                if (returnFocus.IsEnabled) returnFocus.Focus(FocusState.Programmatic);
                else CloseIconButton.Focus(FocusState.Programmatic);
            }
        }
    }

    private async void OnClose(object sender, RoutedEventArgs e) => await CloseAsync();
    private async void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        e.Handled = true;
        await CloseAsync();
    }

    public async Task CloseAsync()
    {
        if (_closing || _actionRunning || _finished) return;
        _closing = true;
        DetailSurface.IsHitTestVisible = false;
        await Task.WhenAll(Motion.ExitAsync(Scrim, milliseconds: 120, scale: 1),
            Motion.ExitAsync(DetailSurface, milliseconds: 120, scale: 0.985f));
        CloseImmediately();
    }

    public void CloseImmediately()
    {
        if (_finished) return;
        _finished = true;
        _popup.IsOpen = false;
        if (_owner is not null) _owner.Changed -= OnRootChanged;
        _closed.TrySetResult();
    }
}
