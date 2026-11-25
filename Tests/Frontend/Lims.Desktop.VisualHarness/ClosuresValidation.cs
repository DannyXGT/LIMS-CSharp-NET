using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
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
    private static readonly string[] ClosureViewports=["1366x768","1280x800"];
    private PlanningClosuresView? _closureValidation;
    private PlanningJobsView? _closureHost;
    private PlanningClosuresPage? _standaloneClosure;
    private ClosureHttp? _closureHttp;
    private async Task ValidateClosuresAsync()
    {
        _closureHost?.Dispose();_standaloneClosure?.Dispose();_closureValidation?.Dispose();_closureHttp?.Dispose();var http=new ClosureHttp();_closureHttp=http;
        var allow=true;var dossierOpened=false;var view=new PlanningClosuresView(new(http),_=>{dossierOpened=true;return Task.CompletedTask;},()=>allow);_closureValidation=view;
        var root=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star));root.Margin=new(12);root.Background=PlanningUi.Brush("LimsBackgroundBrush");root.RequestedTheme=ElementTheme.Dark;
        PlanningUi.Place(root,PlanningUi.Text("VALIDACIÓN LOCAL · datos ficticios · sin servidor ni base de datos",11,true),0);PlanningUi.Place(root,view,1);
        _window!.Content=root;if(_window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)presenter.Restore();await Task.Delay(200);_window.AppWindow.MoveAndResize(new RectInt32(40,40,1366,768));PlanningUi.Track(_window);
        await Task.Delay(200);await view.Load();await Task.Delay(150);
        static void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        Button Find(string text)=>VisualDescendants<Button>(view).Single(b=>b.Content as string==text||AutomationProperties.GetName(b)==text);
        async Task Wait(Func<bool> done){for(var i=0;i<120&&!done();i++)await Task.Delay(30);if(!done())throw new InvalidOperationException("Closure validation timed out.");await Task.Delay(120);}
        var selectionGrid=VisualDescendants<ReportClosureGrid>(view).Single();var table=VisualDescendants<ListView>(view).Single(l=>AutomationProperties.GetName(l)=="Bandeja de cierres");selectionGrid.Select(0,0);selectionGrid.OpenSelection();
        await Wait(()=>VisualDescendants<TextBlock>(view).Any(t=>t.Text.Contains("Última prueba:",StringComparison.Ordinal)));
        var tests=VisualDescendants<ListView>(view).Single(l=>AutomationProperties.GetName(l)=="Pruebas actuales del JOB en cierre");
        if(tests.Items.Count!=2||tests.Items.OfType<PlanningRow>().Select(t=>t.ExecutionId).Distinct().Count()!=2)throw new InvalidOperationException("Repeated executions were lost in closure detail.");
        Invoke(Find("Abrir expediente"));if(!dossierOpened)throw new InvalidOperationException("The closure did not open its real dossier callback.");
        Invoke(Find("Registrar seguimiento"));
        ContentDialog? ActiveDialog()=>VisualTreeHelper.GetOpenPopupsForXamlRoot(view.XamlRoot).Select(p=>p.Child as ContentDialog??VisualDescendants<ContentDialog>(p.Child).FirstOrDefault()).FirstOrDefault(d=>d is not null);
        await Wait(()=>ActiveDialog() is not null);
        var dialog=ActiveDialog()!;
        var responsible=VisualDescendants<TextBox>(dialog).Single(t=>t.Header as string=="Responsable");responsible.Text="Analista de validación";
        var notes=VisualDescendants<TextBox>(dialog).Single(t=>t.Header as string=="Notas");notes.Text="Conservar estas notas al fallar el guardado";
        var reason=VisualDescendants<TextBox>(dialog).Single(t=>t.Header as string=="Motivo obligatorio");reason.Text="Atención de fixture";
        var state=VisualDescendants<ComboBox>(dialog).Single();state.SelectedIndex=1;
        Button Primary()=>VisualDescendants<Button>(dialog).Single(b=>b.Content as string=="Guardar seguimiento");
        Invoke(Primary());await Wait(()=>http.Saves==1&&dialog.IsPrimaryButtonEnabled);
        if(notes.Text!="Conservar estas notas al fallar el guardado"||responsible.Text!="Analista de validación"||ActiveDialog()!=dialog)throw new InvalidOperationException("A failed save discarded the follow-up draft.");
        Invoke(Primary());await Wait(()=>http.Saves==2&&table.Items.OfType<ClosureGridRow>().Any(c=>c.Closure?.State=="Seen"));
        if(http.Requests[0].RequestId!=http.Requests[1].RequestId)throw new InvalidOperationException("The retry used another operation identity.");
        selectionGrid.Select(0,0);selectionGrid.OpenSelection();await Wait(()=>tests.Items.Count==2);
        foreach(var size in new[]{new SizeInt32(1366,768),new SizeInt32(1280,800)})
        {
            if(_window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter current)current.Restore();await Task.Delay(150);
            _window.AppWindow.MoveAndResize(new RectInt32(40,40,size.Width,size.Height));await Task.Delay(200);
            if(Math.Abs(_window.AppWindow.Size.Width-size.Width)>2||Math.Abs(_window.AppWindow.Size.Height-size.Height)>2)throw new InvalidOperationException("The requested closure viewport was not applied.");
            foreach(var control in new FrameworkElement[]{Find("Actualizar"),Find("Registrar seguimiento"),table,tests})
            {var point=control.TransformToVisual(root).TransformPoint(new());if(point.X<0||point.Y<0||point.X+control.ActualWidth>root.ActualWidth+1||point.Y+control.ActualHeight>root.ActualHeight+1)throw new InvalidOperationException("A closure control falls outside the window.");}
        }
        await SaveClosureImage(root,"cierres-detalle.png");
        allow=false;await view.Load();selectionGrid.Select(0,0);selectionGrid.OpenSelection();await Wait(()=>tests.Items.Count==2);
        if(Find("Registrar seguimiento").Visibility!=Visibility.Collapsed)throw new InvalidOperationException("Read-only permission exposes the follow-up action.");
        allow=true;await view.Load();selectionGrid.Select(0,0);selectionGrid.OpenSelection();await Wait(()=>tests.Items.Count==2);
        view.Dispose();root.Children.Remove(view);
        var host=new PlanningJobsView(new(http),_=>Task.CompletedTask,()=>[PlanningPermissions.View,PlanningPermissions.ReportReview]);_closureHost=host;
        PlanningUi.Place(root,host,1);await Task.Delay(150);
        if(VisualDescendants<Button>(host).Any(b=>AutomationProperties.GetName(b)=="Cierres")||VisualDescendants<PlanningClosuresView>(host).Any())throw new InvalidOperationException("Consulta still embeds a duplicate closure entry.");
        await SaveClosureImage(root,"consulta-sin-acceso-cierres.png");host.Dispose();root.Children.Remove(host);
        using(var reportList=new PlanningReportListView(new(http),_=>Task.CompletedTask,()=>[PlanningPermissions.View],()=>false,scannerEnabled:false))
        {
            if(VisualDescendants<Lims.DesignSystem.Controls.LimsTabItem>(reportList).Any(t=>t.Label=="Cierres")||VisualDescendants<PlanningClosuresView>(reportList).Any())throw new InvalidOperationException("Report List still embeds a duplicate closure entry.");
        }
        var standalone=new PlanningClosuresPage(new(http),_=>Task.CompletedTask,()=>[PlanningPermissions.View,PlanningPermissions.ReportReview]);_standaloneClosure=standalone;
        PlanningUi.Place(root,standalone,1);await Task.Delay(150);await standalone.Load();await Task.Delay(150);
        if(!VisualDescendants<ListView>(standalone).Single(l=>AutomationProperties.GetName(l)=="Bandeja de cierres").Items.OfType<ClosureGridRow>().Any(r=>r.Closure?.State=="Seen"))throw new InvalidOperationException("The standalone module did not share the saved follow-up.");
        var standaloneTable=VisualDescendants<ListView>(standalone).Single(l=>AutomationProperties.GetName(l)=="Bandeja de cierres");
        var display=VisualDescendants<ComboBox>(standalone).Single(c=>AutomationProperties.GetName(c)=="Presentación de cierres");display.SelectedIndex=1;await Task.Delay(100);
        if(standaloneTable.Items.OfType<ClosureGridRow>().Any(r=>r.Closure is null))throw new InvalidOperationException("Table mode retained date bands.");display.SelectedIndex=0;await Task.Delay(100);
        if(VisualDescendants<TextBlock>(standalone).Any(t=>t.Text=="PRUEBA2"))throw new InvalidOperationException("The removed column is still displayed.");
        var copyGrid=VisualDescendants<ReportClosureGrid>(standalone).Single();var copyRows=standaloneTable.Items.OfType<ClosureGridRow>().Where(r=>r.Closure is not null).Select(r=>r.Closure!).ToArray();
        copyGrid.Select(2,2);copyGrid.Select(0,0,true);var rectangle=PlanningSheetModel.ParseTsv(copyGrid.SelectedText());
        if(rectangle.Count!=3||rectangle.Any(r=>r.Count!=3)||!rectangle.Select(r=>r[0]).SequenceEqual(copyRows.Take(3).Select(r=>r.Job)))throw new InvalidOperationException("The reverse rectangle copied group bands or lost cell order.");
        if(PlanningSheetModel.ParseTsv(copyGrid.SelectedText(true))[0][2]!="PRUEBA")throw new InvalidOperationException("Copy with headers did not match the selected columns.");
        var htmlTable=XElement.Parse(copyGrid.SelectedHtml(true));
        if(htmlTable.Name!="table"||htmlTable.Elements("tr").Count()!=4||htmlTable.Descendants("th").Count()!=3||htmlTable.Descendants("td").Count()!=9)throw new InvalidOperationException("Rich clipboard table lost the selected rectangle or headers.");
        if(!htmlTable.Descendants("td").Where((_,i)=>i%3==0).Select(c=>c.Value).SequenceEqual(copyRows.Take(3).Select(r=>r.Job)))throw new InvalidOperationException("The rich table changed row order or copied group bands.");
        var specialBuyer="Buyer\t\"Validación\" <script>alert('x')</script>&\r\nLínea 2";copyGrid.SetItems([copyRows[0] with{Buyer=specialBuyer}],false);copyGrid.Select(0,1);
        if(PlanningSheetModel.ParseTsv(copyGrid.SelectedText())[0][0]!=specialBuyer.Replace("\r\n","\n",StringComparison.Ordinal))throw new InvalidOperationException("Clipboard escaping changed embedded tabs, quotes or line breaks.");
        var richCell=XElement.Parse(copyGrid.SelectedHtml()).Descendants("td").Single();
        if(richCell.Value!=specialBuyer.Replace("\r\n","",StringComparison.Ordinal)||richCell.Descendants("script").Any()||richCell.Elements("br").Count()!=1)throw new InvalidOperationException("HTML escaping changed cell text, lost a line break or created markup from source data.");
        copyGrid.Select(0,5);if(copyGrid.SelectedText()!="24/11/2026 10:35")throw new InvalidOperationException("Table mode copied the wrong date column.");
        copyGrid.SetItems([],true);if(copyGrid.HasSelection||copyGrid.SelectedText()!="")throw new InvalidOperationException("A page replacement retained stale selected cells.");
        await standalone.Load();await Task.Delay(150);
        var heading=VisualDescendants<TextBlock>(standalone).Single(t=>t.Text=="Cierres");if(heading.FontSize<22)throw new InvalidOperationException("The closure heading lost the module title hierarchy.");
        foreach(var size in new[]{new SizeInt32(1366,768),new SizeInt32(1280,800)})
        {
            _window.AppWindow.MoveAndResize(new RectInt32(40,40,size.Width,size.Height));await Task.Delay(200);
            foreach(var control in VisualDescendants<Button>(standalone).Where(b=>AutomationProperties.GetName(b) is "Actualizar" or "Exportar"))
            {var point=control.TransformToVisual(root).TransformPoint(new());if(point.X<0||point.Y<0||point.X+control.ActualWidth>root.ActualWidth+1||point.Y+control.ActualHeight>root.ActualHeight+1)throw new InvalidOperationException("A closure heading action falls outside the window.");}
        }
        _window.AppWindow.MoveAndResize(new RectInt32(0,0,1920,1080));await Task.Delay(200);
        await SaveClosureImage(root,"cierres-modulo-independiente.png");
        var dateFilter=VisualDescendants<Button>(standalone).Single(b=>AutomationProperties.GetName(b)=="Fechas de cierre");Invoke(dateFilter);await Task.Delay(180);
        var calendar=VisualTreeHelper.GetOpenPopupsForXamlRoot(standalone.XamlRoot).Select(p=>p.Child).First(p=>VisualDescendants<CheckBox>(p).Any(c=>c.Content as string=="2026"));
        if(VisualDescendants<Expander>(calendar).Any())throw new InvalidOperationException("The date filter still uses nested expansion cards.");
        var day=VisualDescendants<CheckBox>(calendar).Single(c=>c.Content as string=="24");var parent=VisualDescendants<CheckBox>(calendar).Single(c=>c.Content as string=="2026");
        ((IToggleProvider)new CheckBoxAutomationPeer(day).GetPattern(PatternInterface.Toggle)).Toggle();await Task.Delay(60);
        // Automation toggles the dependency property directly; invoke the parent to exercise selection propagation.
        ((IToggleProvider)new CheckBoxAutomationPeer(parent).GetPattern(PatternInterface.Toggle)).Toggle();await Task.Delay(60);
        if(parent.IsChecked!=true||day.IsChecked!=true)throw new InvalidOperationException("The date filter did not synchronize year and day selection.");
        var calendarWidth=((FrameworkElement)calendar).ActualWidth;if(calendarWidth>260)throw new InvalidOperationException("The compact date filter is too wide.");
        File.WriteAllText(Path.Combine(_directory,"closures.json"),JsonSerializer.Serialize(new{Passed=true,Boundary="WinUI closure component and sidebar-only module with real API client and in-memory HTTP fixture. No authenticated server, database, production or LAN acceptance.",RepeatedExecutionsPreserved=true,DossierOpened=dossierOpened,DraftPreservedOnFailure=true,IdempotentRetry=true,ReadOnlyActionHidden=true,SidebarOnly=true,ConsultationEntryRemoved=true,ReportListEntryRemoved=true,ModuleHeading=true,StandaloneModule=true,NoSecondTestColumn=true,ReadOnlyCellSelection=true,RectangularCopy=true,GroupBandsExcluded=true,TsvEscaping=true,RichHtmlTable=true,HtmlTextEscaped=true,SelectionClearedOnReload=true,CompactDateFilter=true,CalendarWidth=calendarWidth,Viewports=ClosureViewports,http.Saves},ReportOptions));
    }
    private async Task SaveClosureImage(FrameworkElement root,string name)
    {
        var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(root);var pixels=await bitmap.GetPixelsAsync();var bytes=new byte[pixels.Length];using(var reader=Windows.Storage.Streams.DataReader.FromBuffer(pixels))reader.ReadBytes(bytes);
        var folder=await Windows.Storage.StorageFolder.GetFolderFromPathAsync(_directory);var file=await folder.CreateFileAsync(name,Windows.Storage.CreationCollisionOption.ReplaceExisting);
        using var stream=await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,bytes);await encoder.FlushAsync();
    }
    private sealed class ClosureHttp : HttpMessageHandler,IHttpClientFactory
    {
        private static readonly JsonSerializerOptions Options=new(JsonSerializerDefaults.Web);
        private readonly Guid _episode=Guid.NewGuid();
        private ReportClosure _row;
        private readonly ReportClosure[] _examples;
        private readonly PlanningRow[] _tests;
        public int Saves;
        public List<ReportClosureFollowUp> Requests {get;}=[];
        public ClosureHttp()
        {
            var at=new DateTimeOffset(2026,11,24,10,35,0,TimeSpan.FromHours(-6));
            _row=new("GUA20260001",2,2,at,new(2026,11,26)){Buyer="Cliente de validación",Laboratories="CHEMICAL",DigitizedBy="Operador de validación",LastTest="PH",Reviewed=0,EpisodeIds=[_episode],Usd=190.40m,Service="Regular (After cut-off)",GroupCount=1,Fingerprint=new string('a',32)};
            _examples=Enumerable.Range(2,12).Select(i=>_row with{Job="DEMO2026"+i.ToString("0000",System.Globalization.CultureInfo.InvariantCulture),Buyer=i%2==0?"Buyer de prueba A":"Buyer de prueba B",LastTest=(i%3) switch{0=>"TENSION & ELONGATION",1=>"CROCKING",_=>"FORMALDEHYDE"},Usd=45m+i*17,Service=i%2==0?"Regular":"Express (After cut-off)",LastDigitizedAt=at.AddMinutes(-30*((i-2)/4+1)),GroupCount=4}).ToArray();
            _tests=Enumerable.Range(1,2).Select(i=>new PlanningRow(_episode,Guid.NewGuid(),_row.Job,new(2026,11,20),new(2026,11,26),"Technical PH "+i,"PH","CHEMICAL",null,at,new(2026,11,24),"Digitado","Operador de validación",null,"ReportList",_row.Buyer,null,false){ScheduleReviewState="Pendiente"}).ToArray();
        }
        public HttpClient CreateClient(string name)=>new(this,false){BaseAddress=new Uri("http://fixture.invalid/")};
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;
            if(path=="/api/planning/closures")return Reply(new ReportClosurePage([_row,.._examples],13,1,100,Saves,"Fixture"){Facets=[new(new(2026,11,24),"10:35",1),new(new(2026,11,24),"10:05",4),new(new(2026,11,24),"09:35",4),new(new(2026,11,24),"09:05",4)]});
            if(path=="/api/planning/closures/detail")return Reply(new ReportClosureDetail(_row,_tests,[]));
            if(path=="/api/planning/closures/follow-up")
            {
                var body=(await request.Content!.ReadFromJsonAsync<ReportClosureFollowUp>(Options,ct))!;Requests.Add(body);Saves++;
                if(Saves==1)return Reply(new{detail="Fallo simulado. Sus notas se conservan; reintente."},HttpStatusCode.ServiceUnavailable);
                _row=_row with{State=body.State,Responsible=body.Responsible,Notes=body.Notes,FollowUpVersion=1,UpdatedBy="Revisor de validación",UpdatedAt=DateTimeOffset.UtcNow};return Reply(_row);
            }
            return Reply(new{detail="Unexpected closure fixture request"},HttpStatusCode.NotFound);
        }
        private static HttpResponseMessage Reply<T>(T value,HttpStatusCode status=HttpStatusCode.OK)=>new(status){Content=JsonContent.Create(value,options:Options)};
    }
}
