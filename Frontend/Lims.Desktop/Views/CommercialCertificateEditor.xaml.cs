using System.Globalization;
using Lims.Contracts.ReferenceMaterials;
using Lims.Desktop.Validation;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lims.Desktop.Views;

public sealed partial class CommercialCertificateEditor : UserControl
{
    private readonly List<ComponentFields> _components = [];
    private readonly List<(TextBox Value, TextBox Unit, TextBox Uncertainty)> _expressions = [];
    private bool _mix;
    private bool _operational;
    public event EventHandler? Changed;
    public IReadOnlyList<CertifiedComponent> PreviewComponents => _components.Select(c => new CertifiedComponent(c.Id, c.Name.Text.Trim(), Optional(c.Cas.Text),
        ReferenceMaterialInput.TryParseDecimal(c.Concentration.Text, 28, out var n) ? n : 0, c.Unit.Text.Trim())).ToArray();
    public void SetSingleName(string name) { if (!_mix && _components.Count == 1 && _components[0].Name.Text != name) _components[0].Name.Text = name; }
    public void SetOperational(bool mix)
    {
        _operational = true;
        ManufacturerPanel.Visibility = ExpressionsPanel.Visibility = ValidityHint.Visibility = Visibility.Collapsed;
        TableSurface.MinWidth = mix ? 600 : 0;
        TableHeader.ColumnDefinitions[4].Width = new GridLength(0);
        TableHeader.ColumnDefinitions[5].Width = new GridLength(mix ? 65 : 0);
        CompositionTitle.Text = mix ? "COMPONENTES DEL MIX" : "SOLUCIÓN COMERCIAL";
        CompositionHint.Text = mix ? "Cada analito conserva su concentración; todos comparten un único inventario." : "Concentración documentada por el fabricante.";
        ActionLabel.Visibility = mix ? Visibility.Visible : Visibility.Collapsed;
        ComponentSearch.Visibility = mix ? Visibility.Visible : Visibility.Collapsed;
        PasteSurface.Visibility = mix ? Visibility.Visible : Visibility.Collapsed;
        if (!mix) { TableHeader.Children[0].Visibility = TableHeader.Children[1].Visibility = Visibility.Collapsed; TableHeader.ColumnDefinitions[0].Width = TableHeader.ColumnDefinitions[1].Width = new GridLength(0); }
        SetPresentation(mix, allowSingle: true);
    }
    private void OnSearchComponents(object sender, TextChangedEventArgs e)
    {
        foreach (var c in _components) c.Surface!.Visibility = $"{c.Name.Text} {c.Cas.Text}".Contains(ComponentSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
    }
    private bool _allowSingle;
    private DateOnly _manufacturerExpiration;
    public CommercialCertificateEditor()
    {
        InitializeComponent();
        ValidityHint.Visibility = Visibility.Visible;
        PeriodUnitSelect.ItemsSource = new[] { "Meses", "Días" }; PeriodUnitSelect.SelectedIndex = 0;
        PeriodUnitSelect.SelectionChanged += (_, _) => UpdateValidity();
        OpeningPicker.DateChanged += (_, _) => UpdateValidity();
        MonthsBox.TextChanged += (_, _) => UpdateValidity();
    }
    public void SetPresentation(bool mix, bool allowSingle = false)
    {
        _allowSingle = allowSingle;
        _mix = mix; AddComponentButton.Visibility = mix ? Visibility.Visible : Visibility.Collapsed;
        if (_components.Count == 0) AddComponent();
        if (!mix && _components.Count > 1)
        {
            _components.RemoveRange(1, _components.Count - 1);
            while (ComponentRows.Children.Count > 1) ComponentRows.Children.RemoveAt(1);
        }
    }
    public void SetExpiration(DateOnly expiration) { _manufacturerExpiration = expiration; UpdateValidity(); }
    public void ClearOpening() => OpeningPicker.Date = null;
    public void SetData(CommercialCertificate value)
    {
        ReferenceBox.Text = value.Reference; MatrixBox.Text = value.Matrix; TraceabilityBox.Text = value.Traceability ?? "";
        CoverageBox.Text = Format(value.CoverageFactor); NominalBox.Text = Format(value.NominalConcentration);
        NominalUnitBox.Text = value.NominalUnit ?? "";
        PeriodUnitSelect.SelectedIndex = value.UseAfterOpeningDays.HasValue ? 1 : 0;
        MonthsBox.Text = (value.UseAfterOpeningDays ?? value.UseAfterOpeningMonths)?.ToString(CultureInfo.InvariantCulture) ?? "";
        OpeningPicker.Date = value.OpenedDate?.ToDateTime(TimeOnly.MinValue);
        _components.Clear(); ComponentRows.Children.Clear();
        foreach (var c in value.Components) AddComponent(c);
        _expressions.Clear(); ExpressionRows.Children.Clear();
        foreach (var e in value.OtherExpressions ?? []) AddExpression(e);
        UpdateValidity(); Changed?.Invoke(this, EventArgs.Empty);
    }
    public CommercialCertificate Read()
    {
        var months = Number(MonthsBox);
        var maximum = PeriodUnitSelect.SelectedIndex == 1 ? 36500 : 1200;
        if (months is not null && (months <= 0 || months > maximum || decimal.Truncate(months.Value) != months))
            throw new ArgumentException("El periodo tras apertura debe ser un número entero positivo en la unidad seleccionada.");
        var rows = _components.Select(c => new CertifiedComponent(c.Id, c.Name.Text.Trim(), Optional(c.Cas.Text),
            Number(c.Concentration) ?? throw new ArgumentException("Indique la concentración certificada de cada analito."), c.Unit.Text.Trim(),
            Number(c.Uncertainty), Number(c.Purity), Optional(c.Lot.Text), Optional(c.Extra.Text))).ToArray();
        if (rows.Length < (_mix && !_allowSingle ? 2 : 1) || rows.Any(c => c.Name.Length == 0 || c.CertifiedConcentration <= 0 || c.ConcentrationUnit.Length == 0 || c.ExpandedUncertainty < 0 || c.PurityPercent is <= 0 or > 100))
            throw new ArgumentException("Complete los analitos con nombre, concentración certificada y unidad; un Mix requiere al menos dos.");
        return new(ReferenceBox.Text.Trim(), MatrixBox.Text.Trim(), Optional(TraceabilityBox.Text), Number(NominalBox),
            Optional(NominalUnitBox.Text), Number(CoverageBox), OpeningPicker.Date is { } d ? DateOnly.FromDateTime(d.Date) : null,
            months is null || PeriodUnitSelect.SelectedIndex == 1 ? null : (int)months.Value, rows,
            _expressions.Select(e => new CertifiedExpression(Number(e.Value) ?? throw new ArgumentException("Complete la expresión certificada."), e.Unit.Text.Trim(), Number(e.Uncertainty))).ToArray(),
            months is null || PeriodUnitSelect.SelectedIndex != 1 ? null : (int)months.Value);
    }
    private void UpdateValidity()
    {
        var effective = _manufacturerExpiration;
        if (int.TryParse(MonthsBox.Text, out var months) && months > 0 && months <= (PeriodUnitSelect.SelectedIndex == 1 ? 36500 : 1200))
        {
            if (OpeningPicker.Date is not { } date) { ValidityHint.Text = "Falta fecha de apertura: el frasco no podrá seleccionarse para preparar."; return; }
            var limit = PeriodUnitSelect.SelectedIndex == 1 ? DateOnly.FromDateTime(date.Date).AddDays(months) : DateOnly.FromDateTime(date.Date).AddMonths(months);
            if (limit < effective) effective = limit;
        }
        ValidityHint.Text = $"Vencimiento efectivo: {ReferenceMaterialPresentation.Date(effective)} · nunca posterior al fabricante.";
    }
    private void OnAddComponent(object sender, RoutedEventArgs e) => AddComponent();
    private async void OnPasteComponents(object sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (!clipboard.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)) throw new ArgumentException("Copie una tabla de cuatro columnas desde Excel.");
            var rows = Lims.Contracts.ReferencePreparations.CommercialComponentPaste.Parse(await clipboard.GetTextAsync());
            if (!_mix && rows.Count != 1) throw new ArgumentException("Stock comercial individual admite un compuesto.");
            if (!_mix || _components.Count == 1 && string.IsNullOrWhiteSpace(_components[0].Name.Text) && string.IsNullOrWhiteSpace(_components[0].Concentration.Text)) { _components.Clear(); ComponentRows.Children.Clear(); }
            foreach (var row in rows) AddComponent(row);
            PasteError.Text = $"Se agregaron {rows.Count} componentes. Revise los valores antes de guardar.";
        }
        catch (Exception error) when (error is ArgumentException or System.Runtime.InteropServices.COMException) { PasteError.Text = error.Message; }
    }
    private void AddComponent(CertifiedComponent? value = null)
    {
        var c = new ComponentFields(value?.Id ?? Guid.NewGuid());
        c.Name.Text = value?.Name ?? ""; c.Cas.Text = value?.CasNumber ?? "";
        c.Concentration.Text = Format(value?.CertifiedConcentration); c.Unit.Text = value?.ConcentrationUnit ?? "µg/mL";
        c.Uncertainty.Text = Format(value?.ExpandedUncertainty); c.Purity.Text = Format(value?.PurityPercent);
        c.Lot.Text = value?.Lot ?? ""; c.Extra.Text = value?.AdditionalData ?? "";
        var body = new StackPanel { Spacing = 6 };
        var main = new Grid { ColumnSpacing = 8 };
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        foreach (var _ in Enumerable.Range(0, 3)) main.ColumnDefinitions.Add(new ColumnDefinition());
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_operational ? 0 : 60) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_operational ? (_mix ? 65 : 0) : 35) });
        var fields = new[] { c.Name, c.Cas, c.Concentration, c.Unit };
        for (var i = 0; i < fields.Length; i++)
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(fields[i], fields[i].Header?.ToString() ?? "Componente");
            fields[i].Header = null; Grid.SetColumn(fields[i], i); main.Children.Add(fields[i]);
        }
        if (_operational && !_mix)
        {
            c.Name.Visibility = c.Cas.Visibility = Visibility.Collapsed;
            main.ColumnDefinitions[0].Width = main.ColumnDefinitions[1].Width = new GridLength(0);
        }
        body.Children.Add(main);
        var optional = new StackPanel { Spacing = 6, Visibility = Visibility.Collapsed };
        optional.Children.Add(Row(c.Uncertainty, c.Purity)); optional.Children.Add(Row(c.Lot, c.Extra));
        body.Children.Add(optional);
        var more = new Button { Content = "Datos", Padding = new Thickness(6, 4, 6, 4), Style = (Style)Application.Current.Resources["LimsSecondaryButtonStyle"] };
        more.Click += (_, _) => optional.Visibility = optional.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        more.Visibility = _operational ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(more, 4); main.Children.Add(more);
        var border = new Border { Padding = new Thickness(0, 2, 0, 2), Child = body };
        if (_mix)
        {
            var remove = new Button { Content = _operational ? "Quitar" : "×", Padding = new Thickness(8, 4, 8, 4), Style = (Style)Application.Current.Resources["LimsSecondaryButtonStyle"] };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, "Quitar " + (value?.Name ?? "componente"));
            remove.Click += (_, _) => { _components.Remove(c); ComponentRows.Children.Remove(border); Changed?.Invoke(this, EventArgs.Empty); };
            Grid.SetColumn(remove, 5); main.Children.Add(remove);
        }
        c.Surface = border;
        foreach (var field in fields) field.TextChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _components.Add(c); ComponentRows.Children.Add(border); Changed?.Invoke(this, EventArgs.Empty);
    }
    private void OnAddExpression(object sender, RoutedEventArgs e) => AddExpression();
    private void AddExpression(CertifiedExpression? data = null)
    {
        var value = Field("Valor certificado"); value.Text = Format(data?.Value);
        var unit = Field("Unidad (ej. µg/g)"); unit.Text = data?.Unit ?? "";
        var uncertainty = Field("Incertidumbre expandida"); uncertainty.Text = Format(data?.ExpandedUncertainty);
        var entry = (value, unit, uncertainty); var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(Row(value, unit)); panel.Children.Add(uncertainty);
        var remove = new Button { Content = "Quitar expresión", Style = (Style)Application.Current.Resources["LimsSecondaryButtonStyle"] };
        remove.Click += (_, _) => { _expressions.Remove(entry); ExpressionRows.Children.Remove(panel); };
        panel.Children.Add(remove); _expressions.Add(entry); ExpressionRows.Children.Add(panel);
    }
    private static Grid Row(FrameworkElement left, FrameworkElement right)
    {
        var grid = new Grid { ColumnSpacing = 12 }; grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new());
        Grid.SetColumn(right, 1); grid.Children.Add(left); grid.Children.Add(right); return grid;
    }
    private static TextBox Field(string header) => new() { Header = header, Style = (Style)Application.Current.Resources["LimsCompactTextBoxStyle"] };
    private static decimal? Number(TextBox field) => string.IsNullOrWhiteSpace(field.Text) ? null :
        ReferenceMaterialInput.TryParseDecimal(field.Text, 28, out var number) ? number : throw new ArgumentException($"Revise {field.Header}: use un valor decimal válido.");
    private static string Format(decimal? value) => value?.ToString("G29", CultureInfo.InvariantCulture) ?? "";
    private static string? Optional(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private sealed class ComponentFields(Guid id)
    {
        public Guid Id { get; } = id;
        public Border? Surface { get; set; }
        public TextBox Name { get; } = Field("Compuesto *"); public TextBox Cas { get; } = Field("CAS");
        public TextBox Concentration { get; } = Field("Concentración certificada *"); public TextBox Unit { get; } = Field("Unidad certificada *");
        public TextBox Uncertainty { get; } = Field("Incertidumbre expandida (si consta)"); public TextBox Purity { get; } = Field("Pureza % (si consta)");
        public TextBox Lot { get; } = Field("Lote del componente (si consta)"); public TextBox Extra { get; } = Field("Datos complementarios certificados");
    }
}
