using Lims.Application.Common;
using Lims.Contracts.Alerts;

namespace Lims.Application.Alerts;

public interface IAlertService
{
    Task<OperationResult<AlertPage>> ListAsync(int actor, AlertFilter filter, CancellationToken ct);
    Task<OperationResult<AlertDetail>> DetailAsync(int actor, Guid id, CancellationToken ct);
    Task<OperationResult<AlertDetail>> AcknowledgeAsync(int actor, Guid id, string? note, CancellationToken ct);
    Task<OperationResult<NotificationPage>> NotificationsAsync(int actor, CancellationToken ct);
    Task<OperationResult<bool>> ReadAsync(int actor, Guid? id, CancellationToken ct);
    Task<OperationResult<bool>> ClaimWindowsAsync(int actor, Guid id, CancellationToken ct);
    Task<OperationResult<NotificationPreferences>> PreferencesAsync(int actor, NotificationPreferences preferences, CancellationToken ct);
    Task<OperationResult<AlertAdministration>> AdministrationAsync(int actor, CancellationToken ct);
    Task<OperationResult<AlertAdministration>> ConfigureAsync(int actor, AlertSettings settings, CancellationToken ct);
    Task<OperationResult<DeliveryPage>> DeliveriesAsync(int actor, int page, CancellationToken ct);
    Task<OperationResult<DeliveryItem>> TestEmailAsync(int actor, Guid requestId, CancellationToken ct);
}
