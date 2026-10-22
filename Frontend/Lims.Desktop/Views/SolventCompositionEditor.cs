using System.Collections.Specialized;
using System.ComponentModel;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.ViewModels;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace Lims.Desktop.Views;

public sealed class SolventCompositionEditor : UserControl
{
    private SolventCompositionViewModel? _draft;
    private FrameworkElement? _boundary;
    private readonly TextBlock _catalog = Caption();
    private readonly TextBlock _total = Caption();
    private readonly TextBlock _summary = Caption();
    private readonly TextBlock _error = Caption();
    private readonly Grid _bar = new() { Height = 10, ColumnSpacing = 2 };
    private readonly TextBlock _legend = Caption();
    private readonly Button _add = new() { Content = "Agregar solvente", Style = (Style)Application.Current.Resources["LimsAddActionStyle"] };
    private readonly Button _retry = new() { Content = "Reintentar catálogo", Style = (Style)Application.Current.Resources["LimsUtilityActionStyle"] };
    private bool _showValidation;
    public StackPanel RowsPanel { get; } = new() { Spacing = 6 };
    public event EventHandler? RetryRequested;
    public SolventCompositionEditor()
    {
        var surface = new StackPanel { Spacing = 6 };
        var heading = Caption(); heading.Text = "SOLVENTE / MATRIZ *"; heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        var headingGrid = new Grid { ColumnSpacing = 12 };
        headingGrid.ColumnDefinitions.Add(new()); headingGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _total.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        Grid.SetColumn(_total, 1); headingGrid.Children.Add(heading); headingGrid.Children.Add(_total);
        ToolTipService.SetToolTip(heading, StockSolventComposition.BasisLabel);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; actions.Children.Add(_add); actions.Children.Add(_retry);
        surface.Children.Add(headingGrid); surface.Children.Add(_catalog);
        surface.Children.Add(new ScrollViewer { Content = RowsPanel, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        surface.Children.Add(actions); surface.Children.Add(_bar); surface.Children.Add(_legend); surface.Children.Add(_summary); surface.Children.Add(_error);
        _error.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LimsDangerBrush"];
        _add.Click += (_, _) =>
        {
            if (_draft is null) return;
            var incomplete = _draft.Rows.FirstOrDefault(r => r.SelectedSolvent is null || !r.TryPercent(out var p) || p <= 0);
            if (incomplete is not null)
            {
                _showValidation = true; Render();
                ((Control)((Grid)RowsPanel.Children[_draft.Rows.IndexOf(incomplete)]).Children[incomplete.SelectedSolvent is null ? 0 : 1]).Focus(FocusState.Keyboard);
                return;
            }
            _draft.Add();
        };
        _retry.Click += (_, _) => RetryRequested?.Invoke(this, EventArgs.Empty);
        AutomationProperties.SetName(_add, "Agregar solvente");
        Content = surface;
        Unloaded += (_, _) => Detach();
        Loaded += (_, _) => Attach();
    }
    public void Initialize(SolventCompositionViewModel draft, FrameworkElement boundary)
    {
        Detach(); _draft = draft; _boundary = boundary; Attach(); BuildRows(); Render();
    }
    private void Attach()
    {
        if (_draft is null) return;
        Detach(); _draft.PropertyChanged += OnDraftChanged; _draft.Rows.CollectionChanged += OnRowsChanged;
    }
    private void Detach() { if (_draft is not null) { _draft.PropertyChanged -= OnDraftChanged; _draft.Rows.CollectionChanged -= OnRowsChanged; } }
    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e) => BuildRows();
    private void OnDraftChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SolventCompositionViewModel.IsReady))
            foreach (var row in RowsPanel.Children.OfType<Grid>())
            {
                var select = (LimsSearchSelect)row.Children[0];
                if (select.ItemsSource is not SolventOption[] previous || !previous.SequenceEqual(_draft!.Catalog)) select.ItemsSource = _draft!.Catalog.ToArray();
            }
        Render();
    }
    private void BuildRows()
    {
        if (_draft is null) return;
        if (XamlRoot is not null) LimsPopupField.CloseForRoot(XamlRoot);
        RowsPanel.Children.Clear();
        foreach (var row in _draft.Rows)
        {
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new GridLength(96) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var select = new LimsSearchSelect { ItemsSource = _draft.Catalog.ToArray(), PlaceholderText = "Seleccione solvente", SearchPlaceholder = "Nombre o abreviatura…", PopupBoundary = _boundary };
            select.SetBinding(LimsSelect.SelectedItemProperty, new Binding { Source = row, Path = new PropertyPath(nameof(row.SelectedSolvent)), Mode = BindingMode.TwoWay });
            var percent = new TextBox { PlaceholderText = "0", TextAlignment = TextAlignment.Right, Padding = new Thickness(8, 7, 24, 7), Style = (Style)Application.Current.Resources["LimsCompactTextBoxStyle"], InputScope = new Microsoft.UI.Xaml.Input.InputScope { Names = { new Microsoft.UI.Xaml.Input.InputScopeName { NameValue = Microsoft.UI.Xaml.Input.InputScopeNameValue.Number } } } };
            percent.SetBinding(TextBox.TextProperty, new Binding { Source = row, Path = new PropertyPath(nameof(row.Proportion)), Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            var remove = new Button { Style = (Style)Application.Current.Resources["LimsRemoveActionStyle"], VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(remove, "Eliminar solvente");
            ToolTipService.SetToolTip(percent, "Proporción de composición (%)");
            var percentSign = Caption(); percentSign.Text = "%"; percentSign.HorizontalAlignment = HorizontalAlignment.Right; percentSign.VerticalAlignment = VerticalAlignment.Center; percentSign.Margin = new Thickness(0, 0, 8, 0); percentSign.IsHitTestVisible = false;
            select.LostFocus += (_, _) => { _showValidation = true; Render(); };
            percent.LostFocus += (_, _) => { _showValidation = true; Render(); };
            var position = _draft.Rows.IndexOf(row) + 1;
            AutomationProperties.SetName(select, "Solvente " + position); AutomationProperties.SetName(percent, "Proporción (%) " + position); AutomationProperties.SetName(remove, "Quitar solvente " + position);
            remove.Click += (_, _) => { if (XamlRoot is not null) LimsPopupField.CloseForRoot(XamlRoot); _draft.Remove(row); };
            Grid.SetColumn(percent, 1); Grid.SetColumn(remove, 2); Grid.SetColumn(percentSign, 1); grid.Children.Add(select); grid.Children.Add(percent); grid.Children.Add(remove); grid.Children.Add(percentSign); RowsPanel.Children.Add(grid);
        }
        Render();
    }
    private void Render()
    {
        if (_draft is null) return;
        _catalog.Text = _draft.IsReady ? $"{_draft.Catalog.Count} solventes · busque por nombre o abreviatura" : _draft.CatalogMessage;
        _total.Text = $"Total {StockSolventComposition.Percent(_draft.Total)} %";
        _total.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[_draft.IsValid ? "LimsSuccessBrush" : "LimsTextSecondaryBrush"];
        var complete = _draft.Details.Where(r => r.SolventId > 0 && r.Percent > 0).ToArray();
        _summary.Text = complete.Length == 0 ? "Seleccione un solvente e indique su proporción." :
            string.Join("  ·  ", complete.Select(r => $"{r.Abbreviation} {r.PercentDisplay}"));
        var touched = _draft.Rows.Any(r => r.SelectedSolvent is not null || !string.IsNullOrWhiteSpace(r.Proportion));
        _error.Text = _showValidation && touched ? _draft.Error ?? "" : "";
        _error.Visibility = _error.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _retry.Visibility = _draft.IsReady ? Visibility.Collapsed : Visibility.Visible; _add.IsEnabled = _draft.IsReady;
        foreach (var grid in RowsPanel.Children.OfType<Grid>()) ((LimsSearchSelect)grid.Children[0]).IsEnabled = _draft.IsReady;
        StockCompositionVisual.Render(_bar, _legend, complete);
        _legend.Visibility = Visibility.Collapsed;
    }
    private static TextBlock Caption() => new() { Style = (Style)Application.Current.Resources["LimsCaptionStyle"], TextWrapping = TextWrapping.Wrap };
}
