using System.Net;
using System.Net.Http.Json;
using Lims.Contracts.Alerts;
using Lims.Desktop.Http;

namespace Lims.Desktop.Tests;

public sealed class AlertsApiClientTests
{
    [Fact]
    public async Task FiltersAreEscapedAndReadAllUsesUserScopedEndpoint()
    {
        using var handler = new Handler(); using var client = new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/") };
        var api = new AlertsApiClient(new Factory(client));
        await api.ListAsync(new(Search: "Lote A&B", Module: AlertModule.Intermedia, Priority: AlertPriority.Critical, From: new(2026, 10, 1), Page: 3), default);
        Assert.Contains("Search=Lote%20A%26B", handler.Uri!.OriginalString, StringComparison.Ordinal);
        Assert.Contains("Module=Intermedia", handler.Uri.OriginalString, StringComparison.Ordinal);
        Assert.Contains("From=2026-10-01", handler.Uri.OriginalString, StringComparison.Ordinal);
        await api.ReadAsync(null, default); Assert.EndsWith("/notifications/read-all", handler.Uri.AbsolutePath, StringComparison.Ordinal); Assert.Equal(HttpMethod.Post, handler.Method);
    }
    [Theory] [InlineData("")] [InlineData("<html>404</html>")]
    public async Task UndeployedOrInvalidServerResponseIsNotARealZero(string text)
    {
        using var handler = new Handler { Body = text }; using var client = new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/") };
        var result = await new AlertsApiClient(new Factory(client)).NotificationsAsync(default);
        Assert.False(result.IsSuccess); Assert.Null(result.Value); Assert.NotNull(result.Error); Assert.Equal(404, result.StatusCode); Assert.Equal("alerts.not_deployed", result.Error.Code);
    }
    [Theory] [InlineData(401)] [InlineData(403)] [InlineData(500)] [InlineData(503)]
    public async Task EmptyOrHtmlErrorKeepsTheActualHttpStatus(int status)
    {
        using var handler = new Handler { Body = "<html>IIS error</html>", Status = (HttpStatusCode)status };
        using var client = new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/") };
        var result = await new AlertsApiClient(new Factory(client)).NotificationsAsync(default);
        Assert.False(result.IsSuccess); Assert.Null(result.Value); Assert.Equal(status, result.StatusCode); Assert.NotEmpty(result.Error!.Message);
    }
    [Theory] [InlineData("")] [InlineData("null")] [InlineData("{}")] [InlineData("<html>login</html>")] [InlineData("{\"items\":[],\"preferences\":{},\"unread\":0}")]
    public async Task SuccessfulHttpWithAnInvalidContractDoesNotShowRealZeros(string body)
    {
        using var handler = new Handler { Body = body, Status = HttpStatusCode.OK };
        using var client = new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/") };
        var result = await new AlertsApiClient(new Factory(client)).NotificationsAsync(default);
        Assert.False(result.IsSuccess); Assert.Null(result.Value); Assert.Equal("alerts.invalid_contract", result.Error!.Code);
    }
    [Fact]
    public async Task ARealEmptyNotificationPageRemainsSuccessful()
    {
        using var handler = new Handler { Body = System.Text.Json.JsonSerializer.Serialize(new NotificationPage([], 0, new(), true, DateTimeOffset.UtcNow)), Status = HttpStatusCode.OK };
        using var client = new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/") };
        var result = await new AlertsApiClient(new Factory(client)).NotificationsAsync(default);
        Assert.True(result.IsSuccess); Assert.Equal(0, result.Value!.Unread); Assert.Empty(result.Value.Items);
    }
    [Fact]
    public async Task ConnectivityFailureHasNoHttpStatusAndNoData()
    {
        using var handler = new Handler { DropConnection = true };
        using var client = new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/") };
        var result = await new AlertsApiClient(new Factory(client)).NotificationsAsync(default);
        Assert.False(result.IsSuccess); Assert.Null(result.Value); Assert.Equal(0, result.StatusCode); Assert.Equal("connection_error", result.Error!.Code);
    }
    [Fact]
    public async Task CallerCancellationPropagatesWithoutAFalseConnectionError()
    {
        using var handler = new Handler(); using var client = new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/") };
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AlertsApiClient(new Factory(client)).NotificationsAsync(canceled.Token));
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class Handler : HttpMessageHandler
    {
        public string? Body { get; set; } public HttpStatusCode? Status { get; set; } public Uri? Uri { get; private set; } public HttpMethod? Method { get; private set; }
        public bool DropConnection { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); if (DropConnection) throw new HttpRequestException("Isolated connection failure");
            Uri = request.RequestUri; Method = request.Method;
            return Task.FromResult(new HttpResponseMessage(Status ?? (Body is null ? HttpStatusCode.OK : HttpStatusCode.NotFound)) { Content = Body is null ? JsonContent.Create(true) : new StringContent(Body) });
        }
    }
}
