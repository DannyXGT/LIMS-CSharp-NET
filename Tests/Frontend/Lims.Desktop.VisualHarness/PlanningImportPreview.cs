using System.Net;
using System.Reflection;
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
    private Action? _closeImportPreview;
    private static readonly string[] ImportSourceFields=["_summary","_report","_supplement"];
    private PlanningImportReviewWindow? _importReviewPreview;
    private async Task ShowPlanningImportAsync(JsonElement data)
    {
        _importReviewPreview?.Close();_closeImportPreview?.Invoke();
        var scenario=data.TryGetProperty("scenario",out var value)?value.GetString():"empty";
        var transport=new ImportPreviewHttp();var api=new PlanningApiClient(transport);
        var _importPreview=new PlanningImportsView(api,_=>Task.CompletedTask,()=>[PlanningPermissions.Import,PlanningPermissions.Equivalences]);
        _closeImportPreview=()=>{_importPreview.Dispose();transport.Dispose();};
        _window!.Content=new Border{Padding=new(20),Background=PlanningUi.Brush("LimsBackgroundBrush"),Child=_importPreview,RequestedTheme=ElementTheme.Dark};
        _window.AppWindow.Resize(new SizeInt32(data.TryGetProperty("width",out var w)?w.GetInt32():1366,data.TryGetProperty("height",out var h)?h.GetInt32():768));
        await _importPreview.Load();await Task.Delay(200);
        if(transport.Requests!=0)throw new InvalidOperationException("Opening imports must not wait for the API, history or queues.");
        const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
        if(scenario is "selected" or "timeout")
        {
            foreach(var (field,name) in new[]{("_summary","Summary-fixture.xlsx"),("_report","Report-fixture.xlsx"),("_supplement","Side2-fixture.xlsx")})
            {
                var path=Path.Combine(_directory,name);File.WriteAllBytes(path,[]);
                typeof(PlanningImportsView).GetField(field,fields)!.SetValue(_importPreview,path);
                var nameField=field=="_summary"?"_summaryName":field=="_report"?"_reportName":"_side2Name";
                ((TextBlock)typeof(PlanningImportsView).GetField(nameField,fields)!.GetValue(_importPreview)!).Text=name;
            }
            typeof(PlanningImportsView).GetMethod("SetStep",fields)!.Invoke(_importPreview,[0]);
            if(scenario=="timeout")await (Task)typeof(PlanningImportsView).GetMethod("Preview",fields)!.Invoke(_importPreview,null)!;
        }
        if(scenario=="review")
        {
            var preview=new PlanningImportPreview(Guid.NewGuid(),"Preview",1,new("First",new(2026,11,26)),[],[],[],[],DateTimeOffset.UtcNow,41,412,0,0);
            PlanningImportInspectionRow Row(string category,string job,string? test=null)=>new(category,job,null,test,null,null,"Express",null,null,null,null,null,null,"Datos ficticios para revisión visual",RelatedJobs:15);
            var items=new[]{Row("Emergency","GUAT-DEMO-001","DIMENSIONAL"),Row("Emergency","GUAT-DEMO-002","BALL BURST"),Row("SummaryWithoutReport","GUAT-DEMO-003"),Row("MissingEquivalence","GUAT-DEMO-004, GUAT-DEMO-005","Label Review (CAN)"),Row("MissingEquivalence","GUAT-DEMO-006","Label Review (EU)")};
            var inspection=new PlanningImportInspection(preview,items,412,4134,84,4546,2,0,0,41,412,"Sesión ficticia",null,[]);
            _importReviewPreview=new(api,inspection,true,()=>Task.FromResult<PlanningImportInspection?>(inspection),_=>Task.FromResult(false),_=>Task.CompletedTask);
            _importReviewPreview.Activate();await Task.Delay(300);
            await CaptureImportReview("review-content.png");
            var overview=(ContentControl)typeof(PlanningImportReviewWindow).GetField("_overview",fields)!.GetValue(_importReviewPreview)!;
            var scroll=(ScrollViewer)overview.Content;scroll.ChangeView(null,scroll.ScrollableHeight,null,true);await Task.Delay(200);
            await CaptureImportReview("review-equivalences.png");
        }
        await Task.Delay(300);
        var buttons=VisualDescendants<Button>(_importPreview).Where(b=>AutomationProperties.GetName(b).StartsWith("Importar Side",StringComparison.Ordinal)).ToArray();
        var status=((TextBlock)typeof(PlanningImportsView).GetField("_status",fields)!.GetValue(_importPreview)!).Text;
        var selected=ImportSourceFields.Select(f=>typeof(PlanningImportsView).GetField(f,fields)!.GetValue(_importPreview)).ToArray();
        var passed=buttons.Length==2&&buttons.All(b=>b.IsEnabled==(scenario is "selected" or "timeout"))&&
            (scenario!="timeout"||(selected.All(s=>s is string)&&status.Contains("No se confirmó",StringComparison.Ordinal)));
        File.WriteAllText(Path.Combine(_directory,"planning-import-"+scenario+".json"),JsonSerializer.Serialize(new
        {
            Boundary="Real native UI with synthetic files and HTTP failure; no server or database",Passed=passed,Scenario=scenario,
            transport.Requests,Status=status,SelectedFiles=selected,
            Buttons=buttons.Select(b=>new{Name=AutomationProperties.GetName(b),b.IsEnabled,b.ActualWidth,b.ActualHeight})
        },ReportOptions));
        if(!passed)throw new InvalidOperationException("Import UI acceptance failed: "+scenario);
    }
    private async Task CaptureImportReview(string name)
    {
        var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync((FrameworkElement)_importReviewPreview!.Content);
        var pixels=await bitmap.GetPixelsAsync();var bytes=new byte[pixels.Length];
        using(var reader=Windows.Storage.Streams.DataReader.FromBuffer(pixels))reader.ReadBytes(bytes);
        var folder=await Windows.Storage.StorageFolder.GetFolderFromPathAsync(_directory);
        var file=await folder.CreateFileAsync(name,Windows.Storage.CreationCollisionOption.ReplaceExisting);
        using var stream=await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
        var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,bytes);await encoder.FlushAsync();
    }
    private sealed class ImportPreviewHttp:HttpMessageHandler,IHttpClientFactory
    {
        public int Requests{get;private set;}
        public HttpClient CreateClient(string name)=>new(this,false){BaseAddress=new Uri("https://fixture.invalid/")};
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {Requests++;throw new TaskCanceledException("Synthetic timeout for review validation");}
    }
}
