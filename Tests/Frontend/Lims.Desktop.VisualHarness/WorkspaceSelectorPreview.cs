using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lims.Contracts.Planning;
using Lims.Desktop.Planning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Lims.Desktop.VisualHarness;

public partial class App
{
    private Action? _closeWorkspaceSelectorPreview;

    private async Task ShowWorkspaceSelectorAsync(JsonElement data)
    {
        if (_closeWorkspaceSelectorPreview is null)
            _window!.Closed += (_, _) => _closeWorkspaceSelectorPreview?.Invoke();
        _closeWorkspaceSelectorPreview?.Invoke();
        var scenario = data.TryGetProperty("scenario", out var mode) ? mode.GetString() ?? "all" : "all";
        var transport = new WorkspaceSelectorHttp(scenario);
        var opened = new List<string>();
        var logouts = 0;
        var page = new WorkspaceSelectorPage(new(transport), new FixtureSession(), target =>
        {
            opened.Add(target);
            File.WriteAllText(Path.Combine(_directory, "workspace-navigation.json"), JsonSerializer.Serialize(opened, ReportOptions));
        }, () =>
        {
            logouts++;
            File.WriteAllText(Path.Combine(_directory, "workspace-logout.json"), JsonSerializer.Serialize(new { Logouts = logouts }));
            return Task.CompletedTask;
        });
        _closeWorkspaceSelectorPreview = () => { page.Dispose(); transport.Dispose(); };
        var root = new Grid { RequestedTheme = ElementTheme.Dark, Background = PlanningUi.Brush("LimsBackgroundBrush") };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        root.Children.Add(new TextBlock
        {
            Text = "LIMS  /  VISTA LOCAL · SESIÓN Y ACCESOS FICTICIOS", FontSize = 11,
            Foreground = PlanningUi.Brush("LimsTextSecondaryBrush"), Margin = new Thickness(24, 12, 150, 12)
        });
        Grid.SetRow(page, 1);
        root.Children.Add(page);
        _window!.Content = root;
        _window.AppWindow.Resize(new SizeInt32(
            data.TryGetProperty("width", out var width) ? width.GetInt32() : 1164,
            data.TryGetProperty("height", out var height) ? height.GetInt32() : 712));
        await Task.Delay(600);
        var buttons = VisualDescendants<Button>(page).Where(b => AutomationProperties.GetAutomationId(b).StartsWith("Workspace", StringComparison.Ordinal)).ToArray();
        var scroll = (ScrollViewer)page.Content;
        File.WriteAllText(Path.Combine(_directory, "workspace-selector-" + scenario + ".json"), JsonSerializer.Serialize(new
        {
            Boundary = "Real WinUI page with synthetic session and in-memory HTTP; no server or database",
            Scenario = scenario, transport.Requests, Width = page.ActualWidth, Height = page.ActualHeight,
            HorizontalOverflow = scroll.ScrollableWidth, VerticalOverflow = scroll.ScrollableHeight,
            Actions = VisualDescendants<Button>(page).Where(b => b.Content is string).Select(b => new
            {
                Name = b.Content, b.IsEnabled,
                Y = b.TransformToVisual(page).TransformPoint(new()).Y, Height = b.ActualHeight
            }),
            Cards = buttons.Select(b => new
            {
                Name = AutomationProperties.GetName(b), Target = b.Tag,
                X = b.TransformToVisual(page).TransformPoint(new()).X,
                Y = b.TransformToVisual(page).TransformPoint(new()).Y,
                Width = b.ActualWidth, Height = b.ActualHeight
            }),
            Messages = VisualDescendants<TextBlock>(page).Select(t => t.Text)
        }, ReportOptions));

        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await bitmap.RenderAsync(root);
        var pixels = await bitmap.GetPixelsAsync();
        var bytes = new byte[pixels.Length];
        using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(pixels)) reader.ReadBytes(bytes);
        var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(_directory);
        var file = await folder.CreateFileAsync("workspace-selector-" + scenario + ".png", Windows.Storage.CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
    }

    private sealed class WorkspaceSelectorHttp(string scenario) : HttpMessageHandler, IHttpClientFactory
    {
        public int Requests { get; private set; }
        public HttpClient CreateClient(string name) => new(this, false) { BaseAddress = new Uri("http://fixture.invalid/") };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (request.Method != HttpMethod.Get || request.RequestUri!.AbsolutePath != "/api/workspaces")
                throw new InvalidOperationException("The selector preview only supports reading workspace access.");
            await Task.Delay(scenario == "loading" ? 60000 : 30, cancellationToken);
            if (scenario == "error") return new(HttpStatusCode.ServiceUnavailable) { Content = JsonContent.Create(new { detail = "No se pudo verificar el acceso. Intenta nuevamente." }) };
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new WorkspaceAccess(
                    scenario is "all" or "notice", scenario != "none", scenario == "all", [],
                    scenario == "notice" ? "Aviso de acceso de prueba: los espacios disponibles dependen de los permisos asignados a tu cuenta. Si necesitas otro acceso, contacta al administrador." : null))
            };
        }
    }
}
