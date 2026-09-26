namespace Lims.Desktop.Http;

internal interface ITokenRefreshCoordinator
{
    Task<bool> RefreshAsync(CancellationToken cancellationToken);
}
