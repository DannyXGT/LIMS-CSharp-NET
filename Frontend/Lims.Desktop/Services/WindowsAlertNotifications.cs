using System.Security;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using Lims.Contracts.Alerts;

namespace Lims.Desktop.Services;

public sealed class WindowsAlertNotifications
{
    private Window? _window;
    private bool _registered;
    public string Status { get; private set; } = "Notificaciones de Windows sin inicializar";
    public event EventHandler<(Guid AlertId, Guid NotificationId, int UserId)>? OpenRequested;
    public void Initialize(Window window)
    {
        if (_registered) return;
        _window = window;
        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnInvoked;
            AppNotificationManager.Default.Register(); _registered = true;
            Status = AppNotificationManager.Default.Setting == AppNotificationSetting.Enabled ? "Windows disponible con el cliente abierto" : "Windows deshabilitado en la configuración del sistema";
            window.Closed += (_, _) =>
            {
                AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
                if (_registered) AppNotificationManager.Default.Unregister();
                _registered = false;
            };
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Status = $"Windows no disponible para esta instalación ({error.GetType().Name}, 0x{error.HResult:X8}). El centro interno sigue disponible."; }
    }
    public bool Available
    {
        get
        {
            if (!_registered) return false;
            try { return AppNotificationManager.Default.Setting == AppNotificationSetting.Enabled; }
            catch (Exception error) when (error is not OutOfMemoryException) { Status = "Windows no disponible (" + error.GetType().Name + ")."; return false; }
        }
    }
    public bool Show(NotificationItem item, int userId)
    {
        if (!Available) return false;
        try
        {
            var title = SecurityElement.Escape("LIMS — " + item.Title);
            var message = SecurityElement.Escape(item.Message);
            var xml = $"<toast launch='alert={item.AlertId:D}&amp;notification={item.Id:D}&amp;user={userId}'><visual><binding template='ToastGeneric'><text>{title}</text><text>{message}</text></binding></visual></toast>";
            var toast = new AppNotification(xml) { Tag = item.Id.ToString("N"), Group = "LimsAlerts", ExpiresOnReboot = true };
            AppNotificationManager.Default.Show(toast); return toast.Id != 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Status = "Windows no confirmó el aviso (" + error.GetType().Name + "). Consulte la campana."; return false; }
    }
    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        var values = args.Argument.Split('&').Select(s => s.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
        if (!values.TryGetValue("alert", out var alert) || !Guid.TryParse(alert, out var id) ||
            !values.TryGetValue("notification", out var notification) || !Guid.TryParse(notification, out var notificationId) ||
            !values.TryGetValue("user", out var user) || !int.TryParse(user, out var userId)) return;
        _window?.DispatcherQueue.TryEnqueue(() => { _window.Activate(); OpenRequested?.Invoke(this, (id, notificationId, userId)); });
    }
}
