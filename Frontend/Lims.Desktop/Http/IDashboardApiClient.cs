using Lims.Contracts.Dashboard;
namespace Lims.Desktop.Http;
public interface IDashboardApiClient
{
    Task<ApiCallResult<DashboardResponse>> GetAsync(CancellationToken ct);
}
