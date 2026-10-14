using CommunityToolkit.Mvvm.ComponentModel;
using Lims.Contracts.Dashboard;
using Lims.Desktop.Http;
namespace Lims.Desktop.ViewModels;
public sealed partial class DashboardViewModel(IDashboardApiClient api) : ObservableObject
{
    [ObservableProperty] public partial DashboardResponse? Data { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string Error { get; set; } = string.Empty;
    private CancellationTokenSource? _request;
    private int _revision;
    public void Clear()
    {
        ++_revision; _request?.Cancel(); Data = null; Error = string.Empty; IsBusy = false;
    }
    public async Task LoadAsync()
    {
        _request?.Cancel();
        using var request = new CancellationTokenSource(); _request = request;
        var revision = ++_revision;
        Data = null; Error = string.Empty; IsBusy = true;
        try
        {
            var result = await api.GetAsync(request.Token);
            if (revision != _revision) return;
            if (result.IsSuccess && result.Value is {} data) Data = data;
            else Error = result.StatusCode is 401 or 403 ? "La sesión o sus permisos han cambiado. Vuelva a iniciar sesión." :
                result.Error?.Message ?? "No se pudo consultar el dashboard. Reintente para recuperar los datos.";
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        { if (revision == _revision) Error = "El servidor no está disponible. Reintente para recuperar los datos."; }
        finally { if (revision == _revision) IsBusy = false; if (ReferenceEquals(_request, request)) _request = null; }
    }
}
