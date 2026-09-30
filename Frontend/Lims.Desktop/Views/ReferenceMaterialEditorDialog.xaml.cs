using Lims.Contracts.ReferenceMaterials;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

public sealed partial class ReferenceMaterialEditorDialog : ContentDialog
{
    public ReferenceMaterialEditorDialog()
    {
        InitializeComponent();
        var today = DateTimeOffset.Now.Date;
        ReceivedDatePicker.Date = today;
        ExpirationDatePicker.Date = today.AddDays(366);
    }

    public ReferenceMaterialEditorDialog(ReferenceMaterialDetail detail) : this()
    {
        Title = "Editar estándar";
        NameBox.Text = detail.Name;
        CasBox.Text = detail.CasNumber ?? string.Empty;
        CatalogBox.Text = detail.CatalogNumber ?? string.Empty;
        MethodBox.Text = detail.Method;
        PurityBox.Value = (double)detail.PurityPercent;
        LotBox.Text = detail.Lot;
        BrandBox.Text = detail.Brand;
        ReceivedDatePicker.Date = ToDateTimeOffset(detail.ReceivedDate);
        ExpirationDatePicker.Date = ToDateTimeOffset(detail.ExpirationDate);
        PresentationBox.Value = (double)detail.PresentationQuantity;
        PackageCountBox.Value = detail.PackageCount;
        StorageConditionsBox.Text = detail.StorageConditions;
        StorageLocationBox.Text = detail.StorageLocation;
        SelectUnit(detail.Unit);
    }

    public ReferenceMaterialEditorDialog(ReferenceMaterialDetail detail, bool isReplacement) : this(detail)
    {
        if (isReplacement)
        {
            Title = "Reemplazar estándar";
            LotBox.Text = string.Empty;
            ReceivedDatePicker.Date = DateTimeOffset.Now.Date;
            ExpirationDatePicker.Date = DateTimeOffset.Now.Date.AddDays(366);
        }
    }

    public CreateReferenceMaterialRequest CreateRequest() => new(
        NameBox.Text,
        EmptyToNull(CasBox.Text),
        EmptyToNull(CatalogBox.Text),
        MethodBox.Text,
        ToDecimal(PurityBox.Value),
        LotBox.Text,
        BrandBox.Text,
        ToDateOnly(ReceivedDatePicker.Date),
        ToDateOnly(ExpirationDatePicker.Date),
        ToDecimal(PresentationBox.Value),
        SelectedUnit(),
        ToInt32(PackageCountBox.Value),
        StorageConditionsBox.Text,
        StorageLocationBox.Text);

    public UpdateReferenceMaterialRequest UpdateRequest(Guid version)
    {
        var request = CreateRequest();
        return new UpdateReferenceMaterialRequest(
            request.Name,
            request.CasNumber,
            request.CatalogNumber,
            request.Method,
            request.PurityPercent,
            request.Lot,
            request.Brand,
            request.ReceivedDate,
            request.ExpirationDate,
            request.PresentationQuantity,
            request.Unit,
            request.PackageCount,
            request.StorageConditions,
            request.StorageLocation,
            version);
    }

    private string SelectedUnit() => (UnitCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;

    private void SelectUnit(string unit)
    {
        foreach (var candidate in UnitCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(candidate.Tag as string, unit, StringComparison.Ordinal))
            {
                UnitCombo.SelectedItem = candidate;
                return;
            }
        }
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result != ContentDialogResult.Primary)
        {
            return;
        }

        var isValid = true;
        isValid &= SetRequiredError(NameError, NameBox.Text, "Ingrese el nombre del estándar.");
        isValid &= SetRequiredError(MethodError, MethodBox.Text, "Ingrese el método.");
        isValid &= SetRequiredError(LotError, LotBox.Text, "Ingrese el lote.");
        isValid &= SetRequiredError(BrandError, BrandBox.Text, "Ingrese la marca.");
        isValid &= SetRequiredError(StorageLocationError, StorageLocationBox.Text, "Ingrese la ubicación física.");
        isValid &= SetRequiredError(StorageConditionsError, StorageConditionsBox.Text, "Ingrese las condiciones de almacenamiento.");
        isValid &= SetError(
            PurityError,
            !double.IsNaN(PurityBox.Value) && PurityBox.Value > 0 && PurityBox.Value <= 100,
            "La pureza debe ser mayor que 0 y como máximo 100.");
        isValid &= SetError(
            PresentationError,
            !double.IsNaN(PresentationBox.Value) && PresentationBox.Value > 0,
            "La presentación debe ser mayor que 0.");
        isValid &= SetError(
            PackageCountError,
            !double.IsNaN(PackageCountBox.Value) && PackageCountBox.Value > 0 && PackageCountBox.Value % 1 == 0,
            "Las unidades deben ser un entero mayor que 0.");
        isValid &= SetError(
            ExpirationError,
            ExpirationDatePicker.Date >= ReceivedDatePicker.Date,
            "La expiración no puede ser anterior al ingreso.");
        isValid &= SetError(
            CasError,
            string.IsNullOrWhiteSpace(CasBox.Text) || IsValidCas(CasBox.Text.Trim()),
            "El CAS no tiene formato o dígito de control válido.");

        args.Cancel = !isValid;
    }

    private static bool SetRequiredError(TextBlock target, string value, string message) =>
        SetError(target, !string.IsNullOrWhiteSpace(value), message);

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
