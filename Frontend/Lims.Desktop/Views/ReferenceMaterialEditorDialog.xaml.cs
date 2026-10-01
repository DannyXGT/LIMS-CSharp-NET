using Lims.Contracts.ReferenceMaterials;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

public sealed partial class ReferenceMaterialEditorDialog : ContentDialog
{
    private bool _isSaving;
    private readonly string _idleSaveText;

    public ReferenceMaterialEditorDialog(
        IReadOnlyList<ReferenceMethodOption> methods,
        IReadOnlyList<ReferenceUnitOption> units,
        IReadOnlyList<ReferenceLocationOption> locations)
    {
        InitializeComponent();
        MethodBox.ItemsSource = methods;
        UnitCombo.ItemsSource = units;
        StorageLocationBox.ItemsSource = locations;
        MethodBox.SelectedIndex = methods.Count > 0 ? 0 : -1;
        UnitCombo.SelectedItem = units.FirstOrDefault(unit => unit.Symbol == "g");
        StorageLocationBox.SelectedItem = locations.FirstOrDefault(
            location => string.Equals(location.Name, "Laboratorio", StringComparison.OrdinalIgnoreCase));
        _idleSaveText = "Guardar estándar";

        var today = DateTimeOffset.Now.Date;
        ReceivedDatePicker.Date = today;
        ExpirationDatePicker.Date = today.AddDays(366);
    }

    public ReferenceMaterialEditorDialog(
        ReferenceMaterialDetail detail,
        IReadOnlyList<ReferenceMethodOption> methods,
        IReadOnlyList<ReferenceUnitOption> units,
        IReadOnlyList<ReferenceLocationOption> locations) : this(methods, units, locations)
    {
        Title = "Editar estándar";
        NameBox.Text = detail.Name;
        CasBox.Text = detail.CasNumber ?? string.Empty;
        CatalogBox.Text = detail.CatalogNumber ?? string.Empty;
        MethodBox.SelectedItem = methods.FirstOrDefault(method => method.Id == detail.MethodId);
        PurityBox.Value = (double)detail.PurityPercent;
        LotBox.Text = detail.Lot;
        BrandBox.Text = detail.Brand;
        ReceivedDatePicker.Date = ToDateTimeOffset(detail.ReceivedDate);
        ExpirationDatePicker.Date = ToDateTimeOffset(detail.ExpirationDate);
        PresentationBox.Value = (double)detail.PresentationQuantity;
        PackageCountBox.Value = detail.PackageCount;
        StorageTemperatureBox.Text = detail.StorageTemperature;
        StorageLocationBox.SelectedItem = locations.FirstOrDefault(location => location.Id == detail.LocationId);
        UnitCombo.SelectedItem = units.FirstOrDefault(unit => unit.Id == detail.UnitId);
    }

    public ReferenceMaterialEditorDialog(
        ReferenceMaterialDetail detail,
        bool isReplacement,
        IReadOnlyList<ReferenceMethodOption> methods,
        IReadOnlyList<ReferenceUnitOption> units,
        IReadOnlyList<ReferenceLocationOption> locations) : this(detail, methods, units, locations)
    {
        if (isReplacement)
        {
            Title = "Reemplazar estándar";
            _idleSaveText = "Continuar";
            SaveButtonText.Text = _idleSaveText;
            LotBox.Text = string.Empty;
            ReceivedDatePicker.Date = DateTimeOffset.Now.Date;
            ExpirationDatePicker.Date = DateTimeOffset.Now.Date.AddDays(366);
        }
    }

    public Func<Task<string?>>? SaveAsync { get; set; }
    public bool WasAccepted { get; private set; }

    public CreateReferenceMaterialRequest CreateRequest() => new(
        NameBox.Text.Trim(), EmptyToNull(CasBox.Text), EmptyToNull(CatalogBox.Text), SelectedMethodId(),
        ToDecimal(PurityBox.Value), LotBox.Text.Trim(), BrandBox.Text.Trim(), ToDateOnly(ReceivedDatePicker.Date),
        ToDateOnly(ExpirationDatePicker.Date), ToDecimal(PresentationBox.Value), SelectedUnitId(),
        ToInt32(PackageCountBox.Value), StorageTemperatureBox.Text.Trim(), SelectedLocationId());

    public UpdateReferenceMaterialRequest UpdateRequest(Guid version)
    {
        var request = CreateRequest();
        return new UpdateReferenceMaterialRequest(
            request.Name, request.CasNumber, request.CatalogNumber, request.MethodId, request.PurityPercent,
            request.Lot, request.Brand, request.ReceivedDate, request.ExpirationDate,
            request.PresentationQuantity, request.UnitId, request.PackageCount,
            request.StorageTemperature, request.LocationId, version);
    }

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        if (XamlRoot is null)
        {
            return;
        }

        var dialogHeight = Math.Min(760, Math.Max(420, XamlRoot.Size.Height * 0.84));
        var contentHeight = Math.Max(340, dialogHeight - 88);

        MaxHeight = dialogHeight;
        EditorRoot.Width = Math.Min(960, Math.Max(360, XamlRoot.Size.Width - 80));
        EditorRoot.Height = contentHeight;
        EditorRoot.MaxHeight = contentHeight;
        EditorScrollViewer.MaxHeight = Math.Max(270, contentHeight - 58);
        DispatcherQueue.TryEnqueue(() =>
        {
            NameBox.Focus(FocusState.Programmatic);
            EditorScrollViewer.ChangeView(null, 0, null, true);
        });
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_isSaving)
        {
            return;
        }

        if (!ValidateForm())
        {
            EditorScrollViewer.ChangeView(null, 0, null, false);
            NameBox.Focus(FocusState.Programmatic);
            return;
        }

        SaveError.Visibility = Visibility.Collapsed;
        if (SaveAsync is null)
        {
            WasAccepted = true;
            Hide();
            return;
        }

        SetSavingState(true);
        try
        {
            var error = await SaveAsync();
            if (string.IsNullOrWhiteSpace(error))
            {
                WasAccepted = true;
                _isSaving = false;
                Hide();
                return;
            }

            SaveError.Text = error;
            SaveError.Visibility = Visibility.Visible;
        }
        catch (Exception)
        {
            SaveError.Text = "No se pudo guardar el estándar. Intente nuevamente.";
            SaveError.Visibility = Visibility.Visible;
        }
        finally
        {
            if (!WasAccepted)
            {
                SetSavingState(false);
            }
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (!_isSaving)
        {
            Hide();
        }
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (_isSaving)
        {
            args.Cancel = true;
        }
    }

    private bool ValidateForm()
    {
        var isValid = true;
        isValid &= SetRequiredError(NameError, NameBox.Text, "El nombre es obligatorio.");
        isValid &= SetError(MethodError, SelectedMethodId() > 0, "Seleccione un método.");
        isValid &= SetRequiredError(LotError, LotBox.Text, "El lote es obligatorio.");
        isValid &= SetRequiredError(BrandError, BrandBox.Text, "La marca es obligatoria.");
        isValid &= SetError(StorageLocationError, SelectedLocationId() > 0, "Seleccione una ubicación física.");
        isValid &= SetRequiredError(StorageTemperatureError, StorageTemperatureBox.Text, "La temperatura de almacenamiento es obligatoria.");
        isValid &= SetError(PurityError, !double.IsNaN(PurityBox.Value) && PurityBox.Value > 0 && PurityBox.Value <= 100, "La pureza debe estar entre 0 y 100.");
        isValid &= SetError(PresentationError, !double.IsNaN(PresentationBox.Value) && PresentationBox.Value > 0, "La presentación debe ser mayor que 0.");
        isValid &= SetError(UnitError, SelectedUnitId() > 0, "Seleccione una unidad.");
        isValid &= SetError(PackageCountError, !double.IsNaN(PackageCountBox.Value) && PackageCountBox.Value >= 1 && PackageCountBox.Value % 1 == 0, "El número de unidades debe ser un entero mayor o igual que 1.");
        isValid &= SetError(ReceivedDateError, true, string.Empty);
        isValid &= SetError(ExpirationError, ExpirationDatePicker.Date > ReceivedDatePicker.Date, "La fecha de expiración debe ser posterior a la fecha de ingreso.");
        isValid &= SetError(CasError, string.IsNullOrWhiteSpace(CasBox.Text) || IsValidCas(CasBox.Text.Trim()), "El CAS no tiene formato o dígito de control válido.");
        return isValid;
    }

    private void SetSavingState(bool isSaving)
    {
        _isSaving = isSaving;
        SaveButton.IsEnabled = !isSaving;
        CancelButton.IsEnabled = !isSaving;
        SaveProgressRing.IsActive = isSaving;
        SaveProgressRing.Visibility = isSaving ? Visibility.Visible : Visibility.Collapsed;
        SaveButtonText.Text = isSaving ? "Guardando..." : _idleSaveText;
    }

    private int SelectedMethodId() => (MethodBox.SelectedItem as ReferenceMethodOption)?.Id ?? 0;

    private int SelectedUnitId() => (UnitCombo.SelectedItem as ReferenceUnitOption)?.Id ?? 0;

    private int SelectedLocationId() => (StorageLocationBox.SelectedItem as ReferenceLocationOption)?.Id ?? 0;

    private static bool SetRequiredError(TextBlock target, string value, string message) => SetError(target, !string.IsNullOrWhiteSpace(value), message);

    private static bool SetError(TextBlock target, bool isValid, string message)
    {
        target.Text = isValid ? string.Empty : message;
        target.Visibility = isValid ? Visibility.Collapsed : Visibility.Visible;
        return isValid;
    }

    private static bool IsValidCas(string value)
    {
        var parts = value.Split('-');
        if (parts.Length != 3 || parts[0].Length is < 2 or > 7 || parts[1].Length != 2 || parts[2].Length != 1)
        {
            return false;
        }

        var digits = string.Concat(parts[0], parts[1]);
        if (!digits.All(char.IsAsciiDigit) || !char.IsAsciiDigit(parts[2][0]))
        {
            return false;
        }

        var sum = 0;
        for (var index = 0; index < digits.Length; index++)
        {
            sum += (digits[digits.Length - 1 - index] - '0') * (index + 1);
        }

        return sum % 10 == parts[2][0] - '0';
    }

    private static decimal ToDecimal(double value) => double.IsNaN(value) ? 0 : (decimal)value;
    private static int ToInt32(double value) => double.IsNaN(value) ? 0 : checked((int)value);
    private static DateOnly ToDateOnly(DateTimeOffset? value) => DateOnly.FromDateTime((value ?? DateTimeOffset.Now).Date);
    private static DateTimeOffset ToDateTimeOffset(DateOnly value) => new(value.ToDateTime(TimeOnly.MinValue));
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
