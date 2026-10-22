using Lims.Contracts.Alerts;

namespace Lims.Desktop.Http;

public interface IAlertsApiClient
{
    Task<ApiCallResult<AlertPage>> ListAsync(AlertFilter filter, CancellationToken ct);
    Task<ApiCallResult<AlertDetail>> DetailAsync(Guid id, CancellationToken ct);
    Task<ApiCallResult<AlertDetail>> AcknowledgeAsync(Guid id, CancellationToken ct);
    Task<ApiCallResult<NotificationPage>> NotificationsAsync(CancellationToken ct);
    Task<ApiCallResult<bool>> ReadAsync(Guid? id, CancellationToken ct);
    Task<ApiCallResult<bool>> ClaimWindowsAsync(Guid id, CancellationToken ct);
    Task<ApiCallResult<NotificationPreferences>> PreferencesAsync(NotificationPreferences preferences, CancellationToken ct);
    Task<ApiCallResult<AlertAdministration>> AdministrationAsync(CancellationToken ct);
    Task<ApiCallResult<AlertAdministration>> ConfigureAsync(AlertSettings settings, CancellationToken ct);
    Task<ApiCallResult<DeliveryPage>> DeliveriesAsync(int page, CancellationToken ct);
    Task<ApiCallResult<DeliveryItem>> TestEmailAsync(Guid requestId, CancellationToken ct);
}
