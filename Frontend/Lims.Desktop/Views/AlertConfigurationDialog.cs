using System.Globalization;
using Lims.Contracts.Alerts;
using Lims.Desktop.Http;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

public sealed class AlertConfigurationDialog : ContentDialog
{
    private readonly IAlertsApiClient _api;
    private readonly AlertAdministration _data;
    private readonly TextBox _days;
    private readonly NumberBox _low, _critical, _hour, _minute;
    private readonly ToggleSwitch _windows, _email;
    private readonly Dictionary<AlertModule, ComboBox> _scopes = [];
    private readonly Dictionary<int, (CheckBox Selected, CheckBox Immediate, CheckBox Daily, ComboBox Department)> _recipients = [];
    private readonly InfoBar _message = new() { IsClosable = false };
    private readonly TextBox _zone;
    public bool Saved { get; private set; }
    public AlertConfigurationDialog(IAlertsApiClient api, AlertAdministration data)
    {
        _api = api; _data = data; Title = "Configuración de Alertas"; PrimaryButtonText = "Guardar configuración"; CloseButtonText = "Cerrar";
        DefaultButton = ContentDialogButton.Primary;
        var settings = data.Settings;
        var root = new Grid { Width = 820, ColumnSpacing = 22, RowSpacing = 12 };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new() { Width = new GridLength(172) }); root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumnSpan(_message, 2); root.Children.Add(_message);
        var navigation = new ListView { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = false };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(navigation, "Secciones de configuración de alertas");
        Grid.SetRow(navigation, 1); root.Children.Add(navigation);
        var viewport = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(viewport, 1); Grid.SetRow(viewport, 1); root.Children.Add(viewport);
        var rules = new StackPanel { Spacing = 16 }; var expiration = new StackPanel { Spacing = 16 }; var channels = new StackPanel { Spacing = 16 };
        var schedulePanel = new StackPanel { Spacing = 16 }; var service = new StackPanel { Spacing = 16 }; var people = new StackPanel { Spacing = 12 };
        (string Name, StackPanel Panel)[] sections = [("Disponibilidad", rules), ("Vencimientos", expiration), ("Canales", channels), ("Destinatarios", people), ("Programación", schedulePanel), ("Estado del servicio", service)];
        foreach (var section in sections) { navigation.Items.Add(section.Name); section.Panel.Children.Add(AlertUi.Text(section.Name, 20, true)); }
        navigation.SelectionChanged += (_, _) => { viewport.Content = sections[Math.Max(0, navigation.SelectedIndex)].Panel; viewport.ChangeView(null, 0, null); };
        navigation.SelectedIndex = 0;
        _days = new() { Header = "Avisar antes del vencimiento (días separados por coma)", Text = string.Join(", ", settings.ExpirationDays), MaxLength = 100 };
        expiration.Children.Add(_days); expiration.Children.Add(AlertUi.Text("Se genera una alerta por material físico, con actualización de prioridad al acercarse la fecha. Un material vencido conserva su incidencia hasta que cambie su condición real.", 12));
        var thresholds = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        _low = Number("Disponibilidad baja (%)", (double)settings.LowPercent, 0.01, 100); _critical = Number("Disponibilidad crítica (%)", (double)settings.CriticalPercent, 0.01, 99.99);
        thresholds.Children.Add(_low); thresholds.Children.Add(_critical); rules.Children.Add(thresholds);
        rules.Children.Add(AlertUi.Text("El agotamiento (0 %) y las condiciones críticas tienen precedencia. La resolución requiere que cambie la condición real."));
        channels.Children.Add(AlertUi.Text("Centro interno: siempre habilitado", 14, true));
        channels.Children.Add(AlertUi.Text("Las preferencias personales limitan los avisos de Windows y el resumen diario. Las incidencias internas permanecen disponibles según los permisos vigentes.", 12));
        _windows = new() { Header = "Canal Windows", IsOn = settings.WindowsEnabled, OnContent = "Según preferencia personal", OffContent = "Deshabilitado globalmente" }; channels.Children.Add(_windows);
        _email = new() { Header = "Correo automático", IsOn = settings.EmailEnabled, OnContent = "Cola de entregas habilitada", OffContent = "Entregas automáticas deshabilitadas" }; channels.Children.Add(_email);
        var schedule = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        _hour = Number("Hora del resumen", settings.DailyHour, 0, 23); _minute = Number("Minuto", settings.DailyMinute, 0, 59); schedule.Children.Add(_hour); schedule.Children.Add(_minute); schedulePanel.Children.Add(schedule);
        _zone = new() { Header = "Zona horaria del servidor de alertas", Text = settings.TimeZoneId, MaxLength = 100 }; schedulePanel.Children.Add(_zone);
        schedulePanel.Children.Add(AlertUi.Text("El trabajo programado del servidor evalúa las condiciones y procesa los correos. Abrir este módulo o guardar preferencias no ejecuta ese trabajo.", 12));
        service.Children.Add(AlertUi.Text(data.Mail.Status, 14, true));
        service.Children.Add(AlertUi.Text(data.Mail.Configured ? $"{data.Mail.Host}:{data.Mail.Port}\nRemitente: {data.Mail.Sender}\nTransporte: {data.Mail.TransportSecurity} · Autenticación: {data.Mail.Authentication}" : "SMTP pendiente de configuración en el servidor. No hay entrega de correo confirmada.", 12));
        service.Children.Add(AlertUi.Text("El administrador del servidor configura SMTP y sus secretos mediante Alerts:Smtp. El transporte admite STARTTLS y autenticación Password, Windows o relay sin credenciales.", 12));
        var test = AlertUi.Button("Encolar prueba a mi correo", async () =>
        {
            var result = await _api.TestEmailAsync(Guid.NewGuid(), CancellationToken.None);
            Show(result.IsSuccess ? "Prueba encolada. El trabajo del servidor debe procesarla; consulte Envíos para confirmar su entrega." : result.Error?.Message ?? "No se pudo encolar la prueba.", result.IsSuccess);
        }, icon: Lims.DesignSystem.Controls.LimsIconKind.Mail); test.IsEnabled = data.Mail.Enabled && data.Mail.Configured; service.Children.Add(test);
        people.Children.Add(AlertUi.Text("Titularidad por departamento", 14, true));
        people.Children.Add(AlertUi.Text("Cada ámbito pertenece al departamento seleccionado. Un ámbito sin asignación no genera alertas ni correos; repartirlo entre departamentos requiere titularidad por material.", 12));
        foreach (var module in Enum.GetValues<AlertModule>().Where(m => m != AlertModule.None))
        {
            var combo = DepartmentBox(data.Departments, settings.Scopes.FirstOrDefault(s => s.Module == module)?.DepartmentId);
            combo.Header = module == AlertModule.Standards ? "Estándares" : module.ToString(); _scopes.Add(module, combo); people.Children.Add(combo);
        }
        people.Children.Add(AlertUi.Text("Destinatarios por departamento", 18, true));
        people.Children.Add(AlertUi.Text("El backend verifica usuario activo, ámbito y permiso de consulta. Las alertas internas se generan para todos los usuarios autorizados; esta selección controla el correo automático."));
        foreach (var user in data.Users)
        {
            var recipient = settings.Recipients.SingleOrDefault(r => r.UserId == user.Id);
            var selected = new CheckBox { Content = $"{user.Name} · {user.Email}", IsChecked = recipient is not null };
            var immediate = new CheckBox { Content = "Críticas inmediatas", IsChecked = recipient?.Immediate ?? true };
            var daily = new CheckBox { Content = "Resumen diario", IsChecked = recipient?.Daily ?? true };
            var dept = DepartmentBox(data.Departments, recipient?.DepartmentId ?? user.DepartmentId);
            var line = new StackPanel { Spacing = 4 }; line.Children.Add(selected);
            line.Children.Add(dept); var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }; options.Children.Add(immediate); options.Children.Add(daily); line.Children.Add(options);
            people.Children.Add(line); _recipients.Add(user.Id, (selected, immediate, daily, dept));
        }
        Content = root;
        Opened += (_, _) => { root.Width = Math.Max(300, Math.Min(820, XamlRoot.Size.Width - 100)); root.Height = Math.Max(200, Math.Min(400, XamlRoot.Size.Height - 220)); };
        PrimaryButtonClick += SaveAsync;
    }
    private static NumberBox Number(string header, double value, double min, double max) => new() { Header = header, Value = value, Minimum = min, Maximum = max, Width = 195, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private static ComboBox DepartmentBox(IReadOnlyList<AlertDepartment> departments, int? id)
    {
        var combo = new ComboBox { MinWidth = 210, HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.Items.Add(new ComboBoxItem { Content = "Sin asignar", Tag = 0 });
        foreach (var department in departments) combo.Items.Add(new ComboBoxItem { Content = department.Name, Tag = department.Id });
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == id) ?? combo.Items[0]; return combo;
    }
    private static int Department(ComboBox combo) => (int)((ComboBoxItem)combo.SelectedItem).Tag;
    private void Show(string message, bool success = false) { _message.Message = message; _message.Severity = success ? InfoBarSeverity.Success : InfoBarSeverity.Error; _message.IsOpen = true; }
    private async void SaveAsync(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var deferral = args.GetDeferral(); IsPrimaryButtonEnabled = false;
        try
        {
            var days = _days.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (days.Length == 0 || days.Any(d => !int.TryParse(d, NumberStyles.None, CultureInfo.InvariantCulture, out _)) ||
                new[] { _low.Value, _critical.Value, _hour.Value, _minute.Value }.Any(v => !double.IsFinite(v)))
            { Show("Revise los días y los valores numéricos."); return; }
            var scopes = _scopes.Where(s => Department(s.Value) > 0).Select(s => new AlertScope(s.Key, Department(s.Value))).ToArray();
            var recipients = _recipients.Where(p => p.Value.Selected.IsChecked == true).Select(p => new AlertRecipient(p.Key, Department(p.Value.Department), p.Value.Immediate.IsChecked == true, p.Value.Daily.IsChecked == true)).ToArray();
            var value = _data.Settings with { ExpirationDays = days.Select(d => int.Parse(d, CultureInfo.InvariantCulture)).ToArray(), LowPercent = (decimal)_low.Value, CriticalPercent = (decimal)_critical.Value, WindowsEnabled = _windows.IsOn, EmailEnabled = _email.IsOn, DailyHour = (int)_hour.Value, DailyMinute = (int)_minute.Value, TimeZoneId = _zone.Text.Trim(), Scopes = scopes, Recipients = recipients };
            var result = await _api.ConfigureAsync(value, CancellationToken.None);
            if (result.IsSuccess) { Saved = true; Hide(); } else Show(result.Error?.Message ?? "No se pudo guardar la configuración.");
        }
        finally { IsPrimaryButtonEnabled = true; deferral.Complete(); }
    }
}
