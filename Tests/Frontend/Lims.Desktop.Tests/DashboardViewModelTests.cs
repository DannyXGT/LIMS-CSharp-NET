using Lims.Contracts.Dashboard;
using Lims.Desktop.Http;
using Lims.Desktop.ViewModels;
namespace Lims.Desktop.Tests;
public sealed class DashboardViewModelTests
{
    private static DashboardResponse Empty => new("Laboratory", "Prueba", "Analista", "Laboratorio Analítico", DateTimeOffset.UtcNow, null, [], new(0,0,0,0,null,0,[]), null, null, [], [], []);
    [Fact] public async Task RealZeroIsDifferentFromApiFailure()
    {
        var api = new Fake(); var model = new DashboardViewModel(api);
        var load = model.LoadAsync(); Assert.True(model.IsBusy); Assert.Null(model.Data);
        api.Requests[0].SetResult(new(true, Empty, null, 200)); await load;
        Assert.Equal(0, model.Data!.Standards!.Registered); Assert.Empty(model.Error);
        var fail = model.LoadAsync(); Assert.Null(model.Data);
        api.Requests[1].SetResult(new(false, null, null, 503)); await fail;
        Assert.Null(model.Data); Assert.NotEmpty(model.Error); Assert.False(model.IsBusy);
    }
    [Fact] public async Task RevocationOrLeavingHomeCannotRestoreStaleResponse()
    {
        var api = new Fake(); var model = new DashboardViewModel(api);
        var old = model.LoadAsync(); model.Clear(); var current = model.LoadAsync();
        api.Requests[1].SetResult(new(false, null, null, 403)); await current;
        api.Requests[0].SetResult(new(true, Empty, null, 200)); await old;
        Assert.Null(model.Data); Assert.NotEmpty(model.Error);
    }
    [Fact] public async Task NetworkFailureRemainsUnavailableAndCanRecover()
    {
        var api = new Fake(); var model = new DashboardViewModel(api);
        var fail = model.LoadAsync(); api.Requests[0].SetException(new HttpRequestException()); await fail;
        Assert.Null(model.Data); Assert.NotEmpty(model.Error);
        var success = model.LoadAsync(); api.Requests[1].SetResult(new(true, Empty, null, 200)); await success;
        Assert.NotNull(model.Data); Assert.Empty(model.Error);
    }
    private sealed class Fake : IDashboardApiClient
    {
        public List<TaskCompletionSource<ApiCallResult<DashboardResponse>>> Requests { get; } = [];
        public Task<ApiCallResult<DashboardResponse>> GetAsync(CancellationToken ct)
        { var request = new TaskCompletionSource<ApiCallResult<DashboardResponse>>(); Requests.Add(request); return request.Task; }
    }
}
