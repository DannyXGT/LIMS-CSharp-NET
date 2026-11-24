using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Lims.Contracts.Planning;
using Lims.Desktop.Planning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Lims.Desktop.VisualHarness;

public partial class App
{
    private static readonly string[] DirectReportViewports=["1366x768","1280x800"];
    private async Task ValidateReportListDirectAsync()
    {
        _reportDirectView?.Dispose();_reportDirectTransport?.Dispose();
        var fixture=new ReportDirectHttp();var scanDevice=new ReportValidationScanner();
        var view=new PlanningReportListView(new(fixture),_=>Task.CompletedTask,()=>[PlanningPermissions.View,PlanningPermissions.ReportMark,PlanningPermissions.ReportReview],()=>false,scanner:scanDevice);
        var root=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star));root.Margin=new(12);root.Background=PlanningUi.Brush("LimsBackgroundBrush");root.RequestedTheme=ElementTheme.Dark;
        PlanningUi.Place(root,PlanningUi.Text("VALIDACIÓN LOCAL · datos ficticios · cliente real con transporte HTTP en memoria",11,true),0);PlanningUi.Place(root,view,1);
        _window!.Content=root;if(_window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)presenter.Restore();_window.AppWindow.Resize(new SizeInt32(1366,768));await Task.Delay(250);await view.Load();await Task.Delay(250);
        PlanningUi.Track(_window);
        CheckBox? FindCheck(string test,string key)
        {
            var table=VisualDescendants<ReportListGrid>(view).Single();var source=VisualDescendants<ListView>(table).Single().ItemsSource as IEnumerable<ReportListItem>;
            return VisualDescendants<Grid>(table).Where(g=>g.Tag is ReportListItem i&&i.Execution.PlanningTest==test&&source?.Any(current=>ReferenceEquals(current,i))==true).SelectMany(g=>g.Children.OfType<Border>()).Select(b=>b.Child).OfType<CheckBox>().FirstOrDefault(c=>c.Tag as string==key);
        }
        CheckBox Check(string test,string key)=>FindCheck(test,key)??throw new InvalidOperationException("The current execution row was not rendered.");
        static void Toggle(CheckBox check)=>((IToggleProvider)new CheckBoxAutomationPeer(check).GetPattern(PatternInterface.Toggle)).Toggle();
        static void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        async Task Wait(Func<bool> done)
        {for(var i=0;i<120&&!done();i++)await Task.Delay(30);if(!done())throw new InvalidOperationException("The Report List operation did not finish.");await Task.Delay(120);}
        var search=VisualDescendants<TextBox>(view).Single(t=>AutomationProperties.GetName(t)=="Buscar JOB en Report List");
        search.Text="FIBRA";await Wait(()=>fixture.Queries.Last().Search=="FIBRA"&&FindCheck("FIBRA","Check") is not null);
        scanDevice.Emit("AR1G0");await Wait(()=>fixture.Scans==1&&VisualDescendants<TextBlock>(view).Any(t=>t.Text.Contains("FIXTURE-JOB · APEOS · marcado",StringComparison.Ordinal)));
        if(fixture.Checks!=0||fixture.Times.Count!=1||Math.Abs((fixture.Times[0]-DateTimeOffset.UtcNow).TotalMinutes)>2)throw new InvalidOperationException("Scanner required a manual clock step or marked the wrong execution.");
        if(fixture.Queries.Any(q=>q.Search=="AR1G0"))throw new InvalidOperationException("A scanner code became a search query.");
        var invalidMessage=ReportScannerMessageProbe.AcceptAsync(_window,"Código de barras no válido");scanDevice.Emit("INVALID");await invalidMessage;await Wait(()=>fixture.InvalidScans==1);
        scanDevice.Emit("AR1G0");await Wait(()=>fixture.Scans==2);
        if(fixture.Times.Count!=1)throw new InvalidOperationException("A repeated code changed a completed test.");
        search.Text="";await Wait(()=>FindCheck("PH","Check") is not null);
        var clock=VisualDescendants<Button>(view).Single(b=>AutomationProperties.GetName(b)=="Hora de digitación");Invoke(clock);await Task.Delay(150);
        var popup=VisualTreeHelper.GetOpenPopupsForXamlRoot(view.XamlRoot).Single(p=>VisualDescendants<TextBox>(p.Child).Any(t=>AutomationProperties.GetName(t)=="Hora de digitación")).Child;
        var time=VisualDescendants<TextBox>(popup).Single(t=>AutomationProperties.GetName(t)=="Hora de digitación");time.Text="08:45";
        Invoke(VisualDescendants<Button>(popup).Single(b=>b.Content as string=="Aplicar hora fija"));await Task.Delay(150);
        Toggle(Check("PH","Keep"));await Wait(()=>fixture.Keeps==1&&FindCheck("PH","Keep")?.IsEnabled==true);

        Toggle(Check("PH","Check"));await Wait(()=>fixture.Checks==1);
        Toggle(Check("CROCKING","Check"));await Wait(()=>fixture.Checks==2);
        if(fixture.Times.Skip(1).Distinct().Count()!=1||fixture.Times[1].ToOffset(TimeSpan.FromHours(-6)).ToString("HH:mm",System.Globalization.CultureInfo.InvariantCulture)!="08:45")throw new InvalidOperationException("Direct checks did not preserve the same fixed hour.");
        if(VisualDescendants<TextBox>(view).Any(t=>AutomationProperties.GetName(t)=="Código AR de Report List")||VisualDescendants<Button>(view).Any(b=>b.Content as string=="Leer AR"))throw new InvalidOperationException("Manual barcode controls are still displayed.");
        var picker=VisualDescendants<ComboBox>(view).Single(c=>AutomationProperties.GetName(c)=="Vista");picker.SelectedIndex=1;
        await Wait(()=>VisualDescendants<CheckBox>(view).Count(c=>c.Tag as string=="Check"&&c.IsChecked==true)>=3);
        Toggle(Check("PH","Check"));await Wait(()=>fixture.Corrections==1);
        picker.SelectedIndex=2;await Task.Delay(300);
        await Wait(()=>FindCheck("CROCKING","Check")?.IsChecked==true&&FindCheck("PH","Check")?.IsChecked==false&&FindCheck("APEOS","Check")?.IsChecked==true);
        var grid=VisualDescendants<ReportListGrid>(view).Single();
        var scroll=VisualDescendants<ScrollViewer>(VisualDescendants<ListView>(grid).Single()).First();
        foreach(var size in new[]{new SizeInt32(1366,768),new SizeInt32(1280,800)})
        {
            _window.AppWindow.Resize(size);await Task.Delay(250);
            if(Math.Abs(_window.AppWindow.Size.Width-size.Width)>2||Math.Abs(_window.AppWindow.Size.Height-size.Height)>2)throw new InvalidOperationException("The requested review viewport was not applied.");
            if(scroll.ScrollableWidth>1)throw new InvalidOperationException($"The default columns overflow horizontally at {size.Width}x{size.Height}.");
            if(VisualDescendants<TextBlock>(grid).Any(t=>(t.Text is "Laboratorio" or "Hora")&&(t.IsTextTrimmed||t.ActualHeight>21)))throw new InvalidOperationException("A compact header breaks or truncates its word.");
            foreach(var control in new FrameworkElement[]{clock,VisualDescendants<Button>(view).Single(b=>AutomationProperties.GetName(b)=="Estado del lector"),grid})
            {var p=control.TransformToVisual(root).TransformPoint(new());if(p.X<0||p.Y<0||p.X+control.ActualWidth>root.ActualWidth+1||p.Y+control.ActualHeight>root.ActualHeight+1)throw new InvalidOperationException("A primary control falls outside the page.");}
        }
        File.WriteAllText(Path.Combine(_directory,"report-list-direct.json"),JsonSerializer.Serialize(new{Passed=true,Boundary="Real WinUI page and API client with in-memory HTTP responses; no server, database, login or physical reader",fixture.Checks,fixture.Keeps,fixture.Scans,fixture.InvalidScans,fixture.Corrections,FixedTimes=fixture.Times,HorizontalOverflow=scroll.ScrollableWidth,Viewports=DirectReportViewports,AutomaticWithoutClock=true,ScanOutsideFilter=true,DuplicatePreservesTime=true,NoManualBarcodeControls=true},ReportOptions));
        var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(root);
        var pixels=await bitmap.GetPixelsAsync();var bytes=new byte[pixels.Length];using(var reader=Windows.Storage.Streams.DataReader.FromBuffer(pixels))reader.ReadBytes(bytes);
        var folder=await Windows.Storage.StorageFolder.GetFolderFromPathAsync(_directory);
        var file=await folder.CreateFileAsync("final-page.png",Windows.Storage.CreationCollisionOption.ReplaceExisting);
        using(var stream=await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))
        {
            var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,bytes);await encoder.FlushAsync();
        }
        // Keep the reviewable page alive after the validation command finishes.
        _reportDirectView=view;_reportDirectTransport=fixture;
    }
    private PlanningReportListView? _reportDirectView;
    private ReportDirectHttp? _reportDirectTransport;

    private sealed class ReportValidationScanner : IReportScanner
    {
        public event Action<ReportScannerState>? Status;
        public event Action<string>? Received;
        public void Connect(string? path=null,int baud=9600)=>Status?.Invoke(new(true,"COM-FIXTURE","Conectado · COM-FIXTURE"));
        public void Disconnect(){}
        public void Dispose(){}
        public void Emit(string code)=>Received?.Invoke(code);
    }

    private sealed class ReportDirectHttp : HttpMessageHandler,IHttpClientFactory
    {
        private static readonly JsonSerializerOptions Options=new(JsonSerializerDefaults.Web);
        private readonly List<ReportListItem> _items=[];
        private long _revision=1;
        public int Checks,Keeps,Scans,InvalidScans,MissingScans,Corrections;
        public bool RejectNextDigitization,RejectNextSearch;
        public List<ReportDigitization> Digitizations {get;}=[];
        public List<DateTimeOffset> Times {get;}=[];
        public readonly List<(string Search,int Page)> Queries=[];
        public ReportDirectHttp(int total=5)
        {
            foreach(var (test,lab,mode) in new[]{("PH","Chemical","manual"),("APEOS","Analytical","scanner"),("CROCKING","Physical","manual"),("FIBRA","Chemical","manual"),("FORMALDEHYDE","Chemical","manual")})
                _items.Add(new(new(Guid.NewGuid(),Guid.NewGuid(),"FIXTURE-JOB",new(2026,11,23),new(2026,11,30),"Technical "+test,test,lab,null,null,null,"Sin información",null,null,"ReportList","Cliente de validación",null,false),0,false,null,mode));
            for(var i=_items.Count;i<total;i++)_items.Add(new(new(Guid.NewGuid(),Guid.NewGuid(),"FIXTURE-"+(i+1),new(2026,11,23),new(2026,11,30),"Technical "+(i+1),"PRUEBA "+(i+1),"Physical",null,null,null,"Sin información",null,null,"ReportList","Cliente de validación",null,false),0,false,null,"manual"));
        }
        public HttpClient CreateClient(string name)=>new(this,false){BaseAddress=new Uri("http://fixture.invalid/")};
        public void AddBatchRows()
        {
            var first=_items[0];_items.Add(first with{Execution=first.Execution with{ExecutionId=Guid.NewGuid(),OriginalTest="Second technical PH"}});
            _items.Add(first with{Execution=first.Execution with{ExecutionId=Guid.NewGuid(),EpisodeId=Guid.NewGuid(),Job="OTHER-JOB"}});
        }
        private void Complete(Guid id,DateTimeOffset at)
        {var n=_items.FindIndex(i=>i.Execution.ExecutionId==id);var item=_items[n];if(item.Execution.Complete)return;_items[n]=item with{Version=item.Version+1,Execution=item.Execution with{DigitizedAt=at,TestedOn=DateOnly.FromDateTime(at.DateTime),DigitizedBy="Operador de validación",DigitizationState="Digitado"}};_revision++;Times.Add(at);}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;
            if(request.Method==HttpMethod.Get&&path=="/api/planning/report-list")
            {
                var q=request.RequestUri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Select(p=>p.Split('=',2)).ToDictionary(p=>p[0],p=>Uri.UnescapeDataString(p[1]));
                var rows=_items.AsEnumerable();if(q.TryGetValue("ExecutionId",out var id))rows=rows.Where(i=>i.Execution.ExecutionId==Guid.Parse(id));
                var search=q.GetValueOrDefault("Search","");var page=int.Parse(q.GetValueOrDefault("Page","1"),System.Globalization.CultureInfo.InvariantCulture);var size=int.Parse(q.GetValueOrDefault("PageSize","500"),System.Globalization.CultureInfo.InvariantCulture);Queries.Add((search,page));
                if(RejectNextSearch){RejectNextSearch=false;return Json(new{detail="No se pudo buscar el JOB. Intente nuevamente."},HttpStatusCode.ServiceUnavailable);}
                if(search.Length>0)rows=rows.Where(i=>(i.Execution.Job+" "+i.Execution.PlanningTest+" "+i.Execution.OriginalTest+" "+i.Execution.Buyer).Contains(search,StringComparison.OrdinalIgnoreCase));
                var view=q.GetValueOrDefault("View","Pending");if(view=="Pending")rows=rows.Where(i=>!i.Execution.Complete);if(view=="Checked")rows=rows.Where(i=>i.Execution.Complete);
                var items=rows.ToArray();return Json(new ReportListPage(items.Skip((page-1)*size).Take(size).ToArray(),items.Length,page,size,_revision,new(true,"fixture"),_revision,100));
            }
            if(path.EndsWith("/digitize",StringComparison.Ordinal))
            {var body=(await request.Content!.ReadFromJsonAsync<ReportDigitization>(Options,ct))!;Digitizations.Add(body);if(RejectNextDigitization){RejectNextDigitization=false;return Json(new{detail="Otro usuario modificó una prueba del lote. Revise antes de confirmar."},HttpStatusCode.Conflict);}foreach(var item in body.Executions)Complete(item.ExecutionId,body.OperationalAt);Checks++;var ready=_items.GroupBy(i=>i.Execution.Job).Where(g=>g.All(i=>i.Execution.Complete)).Select(g=>g.Key).ToArray();return Json(new ReportListResult(body.Executions.Select(i=>i.ExecutionId).ToArray(),ready));}
            if(path.EndsWith("/keep",StringComparison.Ordinal))
            {var body=(await request.Content!.ReadFromJsonAsync<ReportListKeep>(Options,ct))!;var n=_items.FindIndex(i=>i.Execution.ExecutionId==body.Execution.ExecutionId);_items[n]=_items[n] with{KeepInPlanning=body.KeepInPlanning,KeepVersion=_items[n].KeepVersion+1};_revision++;Keeps++;return new(HttpStatusCode.NoContent);}
            if(path.EndsWith("/correction",StringComparison.Ordinal))
            {var body=(await request.Content!.ReadFromJsonAsync<ReportListCheck>(Options,ct))!;var n=_items.FindIndex(i=>i.Execution.ExecutionId==body.Executions[0].ExecutionId);var item=_items[n];_items[n]=item with{Version=item.Version+1,Execution=item.Execution with{DigitizedAt=null,TestedOn=null,DigitizedBy=null,DigitizationState="Sin información"}};_revision++;Corrections++;return Json(new ReportListResult([item.Execution.ExecutionId!.Value],[]));}
            if(path.EndsWith("/scan",StringComparison.Ordinal))
            {var body=(await request.Content!.ReadFromJsonAsync<ReportListScan>(Options,ct))!;if(body.Barcode=="AR10TC"){MissingScans++;return Json(new{detail="El código AR no tiene una identidad de tracking verificada en esta base. No se marcará ninguna ejecución."},HttpStatusCode.BadRequest);}if(body.Barcode!="AR1G0"){InvalidScans++;return Json(new{detail="Código AR inválido"},HttpStatusCode.BadRequest);}var item=_items.Single(i=>i.CheckMode=="scanner");Complete(item.Execution.ExecutionId!.Value,body.CheckedAt!.Value);Scans++;return Json(new ReportListResult([item.Execution.ExecutionId.Value],[]));}
            return Json(new{detail="Unexpected fixture request: "+path},HttpStatusCode.NotFound);
        }
        private static HttpResponseMessage Json<T>(T body,HttpStatusCode code=HttpStatusCode.OK)=>new(code){Content=JsonContent.Create(body,options:Options)};
    }
}
