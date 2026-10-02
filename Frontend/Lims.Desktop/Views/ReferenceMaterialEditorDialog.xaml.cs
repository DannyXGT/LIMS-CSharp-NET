using System.Globalization;
using Lims.Contracts.ReferenceMaterials;
using Lims.Desktop.Http;
using Lims.Desktop.Validation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;

namespace Lims.Desktop.Views;

public sealed partial class ReferenceMaterialEditorDialog : ContentDialog
{
    private const int OpenAnimationMilliseconds = 140;
    private const int CloseAnimationMilliseconds = 100;
    private bool _isSaving;
    private bool _allowImmediateClose;
    private bool _closeAnimationStarted;
    private string? _supportId;
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

        PurityBox.Text = "100";
        PackageCountBox.Text = "1";
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
        PurityBox.Text = ReferenceMaterialInput.FormatDecimal(detail.PurityPercent);
        LotBox.Text = detail.Lot;
        BrandBox.Text = detail.Brand;
        ReceivedDatePicker.Date = ToDateTimeOffset(detail.ReceivedDate);
        ExpirationDatePicker.Date = ToDateTimeOffset(detail.ExpirationDate);
        PresentationBox.Text = ReferenceMaterialInput.FormatDecimal(detail.PresentationQuantity);
        PackageCountBox.Text = detail.PackageCount.ToString(CultureInfo.InvariantCulture);
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

    public Func<Task<ApiOperationFailure?>>? SaveAsync { get; set; }
    public bool WasAccepted { get; private set; }

    public CreateReferenceMaterialRequest CreateRequest() => new(
        NameBox.Text.Trim(), EmptyToNull(CasBox.Text), EmptyToNull(CatalogBox.Text), SelectedMethodId(),
        ReferenceMaterialInput.ParseDecimal(PurityBox.Text), LotBox.Text.Trim(), BrandBox.Text.Trim(), ToDateOnly(ReceivedDatePicker.Date),
        ToDateOnly(ExpirationDatePicker.Date), ReferenceMaterialInput.ParseDecimal(PresentationBox.Text), SelectedUnitId(),
        ReferenceMaterialInput.ParsePackageCount(PackageCountBox.Text), StorageTemperatureBox.Text.Trim(), SelectedLocationId());

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

        var dialogHeight = Math.Min(700, Math.Max(520, XamlRoot.Size.Height * 0.82));
        var contentHeight = Math.Max(440, dialogHeight - 82);
        MaxHeight = dialogHeight;
        EditorRoot.Width = Math.Min(960, Math.Max(420, XamlRoot.Size.Width - 80));
        EditorRoot.MaxHeight = contentHeight;
        EditorScrollViewer.MaxHeight = Math.Max(360, contentHeight - 58);

        PlayOpenAnimation();
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
            FocusFirstInvalidField();
            return;
        }

        ClearApiError();
        if (SaveAsync is null)
        {
            WasAccepted = true;
            await CloseWithAnimationAsync();
            return;
        }

        SetSavingState(true);
        try
        {
            var error = await SaveAsync();
            if (error is null)
            {
                WasAccepted = true;
                _isSaving = false;
                await CloseWithAnimationAsync();
                return;
            }

            ShowApiError(error);
        }
        catch (Exception)
        {
            ShowApiError(new ApiOperationFailure("No se pudo guardar el estándar."));
        }
        finally
        {
            if (!WasAccepted)
            {
                SetSavingState(false);
            }
        }
    }

    private async void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (!_isSaving)
        {
            await CloseWithAnimationAsync();
        }
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (_isSaving)
        {
            args.Cancel = true;
            return;
        }

        if (!_allowImmediateClose)
        {
            args.Cancel = true;
            _ = CloseWithAnimationAsync();
        }
    }

    private async Task CloseWithAnimationAsync()
    {
        if (_closeAnimationStarted)
        {
            return;
        }

        _closeAnimationStarted = true;
        await AnimateAsync(EditorSurface, nameof(Opacity), 0, CloseAnimationMilliseconds);
        _allowImmediateClose = true;
        Hide();
    }

    private void PlayOpenAnimation()
    {
        EditorSurface.Opacity = 0;
        if (EditorSurface.RenderTransform is CompositeTransform transform)
        {
            transform.ScaleX = 0.985;
            transform.ScaleY = 0.985;
            _ = AnimateAsync(transform, nameof(CompositeTransform.ScaleX), 1, OpenAnimationMilliseconds);
            _ = AnimateAsync(transform, nameof(CompositeTransform.ScaleY), 1, OpenAnimationMilliseconds);
        }

        _ = AnimateAsync(EditorSurface, nameof(Opacity), 1, OpenAnimationMilliseconds);
    }

    private static Task AnimateAsync(
        DependencyObject target,
        string property,
        double value,
        int durationMilliseconds)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var animation = new DoubleAnimation
        {
            To = value,
            Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) => completion.TrySetResult();
        storyboard.Begin();
        return completion.Task;
    }

    private void OnActionButtonPointerPressed(object sender, PointerRoutedEventArgs e) =>
        AnimateButton(sender as Button, 0.985);

    private void OnActionButtonPointerReleased(object sender, PointerRoutedEventArgs e) =>
        AnimateButton(sender as Button, 1);

    private static void AnimateButton(Button? button, double scale)
    {
        if (button?.RenderTransform is not CompositeTransform transform)
        {
            return;
        }

        _ = AnimateAsync(transform, nameof(CompositeTransform.ScaleX), scale, 90);
        _ = AnimateAsync(transform, nameof(CompositeTransform.ScaleY), scale, 90);
    }

    private bool ValidateForm()
    {
        var isValid = true;
        isValid &= SetRequiredError(NameError, NameBox.Text, "El nombre es obligatorio.");
        isValid &= SetError(MethodError, SelectedMethodId() > 0, "Seleccione un método.");
        isValid &= SetRequiredError(LotError, LotBox.Text, "El lote es obligatorio.");
        isValid &= SetRequiredError(BrandError, BrandBox.Text, "La marca es obligatoria.");
        isValid &= ValidatePurity();
        isValid &= ValidateDates();
        isValid &= ValidatePresentation();
        isValid &= SetError(UnitError, SelectedUnitId() > 0, "Seleccione una unidad.");
        isValid &= ValidatePackageCount();
        isValid &= SetError(StorageLocationError, SelectedLocationId() > 0, "Seleccione una ubicación física.");
        isValid &= SetRequiredError(StorageTemperatureError, StorageTemperatureBox.Text, "La temperatura de almacenamiento es obligatoria.");
        isValid &= ValidateCas();
        return isValid;
    }

    private bool ValidatePurity()
    {
        if (string.IsNullOrWhiteSpace(PurityBox.Text))
        {
            return SetError(PurityError, false, "La pureza es obligatoria.");
        }

        return SetError(
            PurityError,
            ReferenceMaterialInput.TryParseDecimal(PurityBox.Text, 4, out var purity) && purity > 0 && purity <= 100,
            "La pureza debe ser un decimal mayor que 0 y menor o igual que 100, con hasta 4 decimales.");
    }

    private bool ValidatePresentation()
    {
        if (string.IsNullOrWhiteSpace(PresentationBox.Text))
        {
            return SetError(PresentationError, false, "La presentación es obligatoria.");
        }

        return SetError(
            PresentationError,
            ReferenceMaterialInput.TryParseDecimal(PresentationBox.Text, 6, out var presentation) && presentation > 0,
            "La presentación debe ser un decimal mayor que 0, con hasta 6 decimales.");
    }

    private bool ValidatePackageCount() => SetError(
        PackageCountError,
        ReferenceMaterialInput.TryParsePackageCount(PackageCountBox.Text, out _),
        "El número de unidades debe ser un entero mayor o igual que 1.");

    private bool ValidateDates()
    {
        var receivedIsValid = SetError(
            ReceivedDateError,
            ReceivedDatePicker.Date.HasValue,
            "La fecha de ingreso es obligatoria.");
        var expirationIsValid = SetError(
            ExpirationError,
            ExpirationDatePicker.Date.HasValue,
            "La fecha de expiración es obligatoria.");
        if (receivedIsValid && expirationIsValid)
        {
            expirationIsValid = SetError(
                ExpirationError,
                ExpirationDatePicker.Date!.Value.Date >= ReceivedDatePicker.Date!.Value.Date,
                "La fecha de expiración debe ser igual o posterior a la fecha de ingreso.");
        }

        return receivedIsValid && expirationIsValid;
    }

    private bool ValidateCas() => SetError(
        CasError,
        string.IsNullOrWhiteSpace(CasBox.Text) || IsValidCas(CasBox.Text.Trim()),
        "El CAS no tiene formato o dígito de control válido.");

    private void OnPurityLostFocus(object sender, RoutedEventArgs e) => ValidatePurity();
    private void OnPresentationLostFocus(object sender, RoutedEventArgs e) => ValidatePresentation();
    private void OnPackageCountLostFocus(object sender, RoutedEventArgs e) => ValidatePackageCount();
    private void OnCasLostFocus(object sender, RoutedEventArgs e) => ValidateCas();
    private void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => ValidateDates();

    private void FocusFirstInvalidField()
    {
        var fields = new (TextBlock Error, Control Field)[]
        {
            (NameError, NameBox),
            (CasError, CasBox),
            (MethodError, MethodBox),
            (LotError, LotBox),
            (BrandError, BrandBox),
            (PurityError, PurityBox),
            (ReceivedDateError, ReceivedDatePicker),
            (ExpirationError, ExpirationDatePicker),
            (PresentationError, PresentationBox),
            (UnitError, UnitCombo),
            (PackageCountError, PackageCountBox),
            (StorageLocationError, StorageLocationBox),
            (StorageTemperatureError, StorageTemperatureBox),
        };
        var first = fields.FirstOrDefault(item => item.Error.Visibility == Visibility.Visible);
        first.Field?.Focus(FocusState.Programmatic);
    }

    private void SetSavingState(bool isSaving)
    {
        _isSaving = isSaving;
        SaveButton.IsEnabled = !isSaving;
        CancelButton.IsEnabled = !isSaving;
        SaveProgressRing.IsActive = isSaving;
        SaveProgressRing.Visibility = isSaving ? Visibility.Visible : Visibility.Collapsed;
        SaveIcon.Visibility = isSaving ? Visibility.Collapsed : Visibility.Visible;
        SaveButtonText.Text = isSaving ? "Guardando..." : _idleSaveText;
    }

    private void ShowApiError(ApiOperationFailure error)
    {
        SaveError.Text = error.Message;
        SaveError.Visibility = Visibility.Visible;
        _supportId = string.IsNullOrWhiteSpace(error.SupportId) ? null : error.SupportId.Trim();
        SupportIdText.Text = _supportId is null ? string.Empty : $"ID de soporte: {_supportId}";
        CopySupportIdButton.Content = "Copiar ID";
        SupportIdPanel.Visibility = _supportId is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClearApiError()
    {
        SaveError.Text = string.Empty;
        SaveError.Visibility = Visibility.Collapsed;
        _supportId = null;
        SupportIdText.Text = string.Empty;
        SupportIdPanel.Visibility = Visibility.Collapsed;
    }

    private void OnCopySupportIdClick(object sender, RoutedEventArgs e)
    {
        if (_supportId is null)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(_supportId);
        Clipboard.SetContent(package);
        CopySupportIdButton.Content = "Copiado";
    }

    private int SelectedMethodId() => (MethodBox.SelectedItem as ReferenceMethodOption)?.Id ?? 0;
    private int SelectedUnitId() => (UnitCombo.SelectedItem as ReferenceUnitOption)?.Id ?? 0;
    private int SelectedLocationId() => (StorageLocationBox.SelectedItem as ReferenceLocationOption)?.Id ?? 0;

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

    private static DateOnly ToDateOnly(DateTimeOffset? value) => DateOnly.FromDateTime(value!.Value.Date);
    private static DateTimeOffset ToDateTimeOffset(DateOnly value) => new(value.ToDateTime(TimeOnly.MinValue));
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
