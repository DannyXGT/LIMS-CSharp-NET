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
    private async Task ValidateReportReaderBatchAsync()
    {
        _reportDirectView?.Dispose();_reportDirectTransport?.Dispose();var fixture=new ReportDirectHttp(12);fixture.AddBatchRows();var scanner=new ReportValidationScanner();
        var view=new PlanningReportListView(new(fixture),_=>Task.CompletedTask,()=>[PlanningPermissions.View,PlanningPermissions.ReportMark,PlanningPermissions.ReportReview],()=>false,scanner:scanner);
        var root=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star));root.Margin=new(12);root.Background=PlanningUi.Brush("LimsBackgroundBrush");root.RequestedTheme=ElementTheme.Dark;
        PlanningUi.Place(root,PlanningUi.Text("VALIDACIÓN LOCAL · datos ficticios · rechazo del lector y lote con hora fija",11,true),0);PlanningUi.Place(root,view,1);
        _window!.Content=root;_window.AppWindow.Resize(new SizeInt32(1366,768));await Task.Delay(250);await view.Load();await Task.Delay(200);
        PlanningUi.Track(_window);
        static void Invoke(Button b)=>((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)).Invoke();
        async Task Wait(Func<bool> ready){for(var i=0;i<200&&!ready();i++)await Task.Delay(30);if(!ready())throw new InvalidOperationException("Reader/batch UI did not settle.");await Task.Delay(120);}
        var message=ReportScannerMessageProbe.AcceptAsync(_window,"Reporte no encontrado.",element=>SaveReportReaderImage(element,"scanner-modal.png"));scanner.Emit("AR10TC");var messageText=await message;
        await Wait(()=>fixture.MissingScans==1&&ReportScannerMessageProbe.Find(_window) is null);
        if(VisualDescendants<InfoBar>(view).Any()||fixture.Checks+fixture.Scans!=0)throw new InvalidOperationException("An unknown report used an inline panel or marked another execution.");
        if(messageText.Contains("React",StringComparison.OrdinalIgnoreCase)||messageText.Contains("tracking",StringComparison.OrdinalIgnoreCase)||messageText.Contains("AR #",StringComparison.Ordinal))throw new InvalidOperationException("The scanner modal exposes technical report details.");
        if(ReportBarcode.BarcodeId("AR292892Z")!=29289)throw new InvalidOperationException("The actual React barcode identity does not decode correctly.");
        File.WriteAllText(Path.Combine(_directory,"scanner-modal.json"),JsonSerializer.Serialize(new{Passed=true,Component="ContentDialog",Theme="Dark",Accepted=true,Message=messageText,InlineErrorPanel=false,ReactBarcode="AR292892Z",TrackingId=29289},ReportOptions));
        scanner.Emit("AR1G0");await Wait(()=>fixture.Scans==1);
        var open=VisualDescendants<Button>(view).Single(b=>AutomationProperties.GetName(b)=="Digitar lote con hora fija");
        DependencyObject? Popup()=>VisualTreeHelper.GetOpenPopupsForXamlRoot(view.XamlRoot).Select(p=>p.Child).FirstOrDefault(p=>VisualDescendants<TextBox>(p).Any(t=>AutomationProperties.GetName(t)=="Buscar JOB para lote de hora fija"));
        Invoke(open);await Wait(()=>Popup() is not null);
        var popup=Popup()!;var search=VisualDescendants<TextBox>(popup).Single(t=>AutomationProperties.GetName(t)=="Buscar JOB para lote de hora fija");
        var results=VisualDescendants<ListView>(popup).Single(l=>AutomationProperties.GetName(l)=="Pruebas pendientes para agregar al lote");
        var chosen=VisualDescendants<ListView>(popup).Single(l=>AutomationProperties.GetName(l)=="Pruebas del lote de hora fija");
        search.Text="PH";await Wait(()=>results.Items.Count==3);results.SelectedItems.Add(results.Items[0]);await Wait(()=>chosen.Items.Count==1);
        Invoke(VisualDescendants<Button>(popup).Single(b=>b.Content as string=="Cancelar"));await Wait(()=>Popup() is null&&open.IsEnabled);
        if(fixture.Checks!=0||fixture.Digitizations.Count!=0)throw new InvalidOperationException("Canceling a batch wrote digitization.");
        Invoke(open);await Wait(()=>Popup() is not null);popup=Popup()!;
        search=VisualDescendants<TextBox>(popup).Single(t=>AutomationProperties.GetName(t)=="Buscar JOB para lote de hora fija");
        results=VisualDescendants<ListView>(popup).Single(l=>AutomationProperties.GetName(l)=="Pruebas pendientes para agregar al lote");
        chosen=VisualDescendants<ListView>(popup).Single(l=>AutomationProperties.GetName(l)=="Pruebas del lote de hora fija");
        if(chosen.Items.Count!=0)throw new InvalidOperationException("A canceled batch leaked its selection.");
        search.Text="FIXTURE-JOB";await Wait(()=>results.Items.Count==5);
        foreach(var item in results.Items.OfType<ReportListItem>().Where(i=>i.Execution.PlanningTest=="PH").ToArray())results.SelectedItems.Add(item);
        await Wait(()=>chosen.Items.Count==2);
        search.Text="FIBRA";await Wait(()=>results.Items.Count==1);results.SelectedItems.Add(results.Items[0]);await Wait(()=>chosen.Items.Count==3);
        chosen.SelectedItem=chosen.Items.OfType<ReportListItem>().Single(i=>i.Execution.PlanningTest=="FIBRA");
        Invoke(VisualDescendants<Button>(popup).Single(b=>b.Content as string=="Quitar del lote"));await Wait(()=>chosen.Items.Count==2);
        search.Text="OTHER-JOB";await Wait(()=>fixture.Queries.Last().Search=="OTHER-JOB"&&results.Items.Count==1);results.SelectedItems.Add(results.Items[0]);await Wait(()=>chosen.Items.Count==3);
        if(chosen.Items.OfType<ReportListItem>().Select(i=>i.Execution.Job).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=2)throw new InvalidOperationException("The batch did not retain executions from different JOBs.");
        VisualDescendants<TextBox>(popup).Single(t=>AutomationProperties.GetName(t)=="Hora fija del lote").Text="09:20";
        await Task.Delay(150);await SaveReportReaderImage(popup as UIElement??throw new InvalidOperationException("No batch popup"),"fixed-time-batch.png");
        search.Text="PRUEBA";await Wait(()=>results.Items.Count==7);foreach(var item in results.Items.OfType<ReportListItem>().ToArray())results.SelectedItems.Add(item);await Wait(()=>chosen.Items.Count==10);
        var batch=VisualDescendants<ReportFixedTimeBatchDialog>(popup).FirstOrDefault()??popup as ReportFixedTimeBatchDialog??throw new InvalidOperationException("No fixed-time dialog.");
        var identities=chosen.Items.OfType<ReportListItem>().Select(i=>i.Execution.ExecutionId).Order().ToArray();
        var fields=VisualDescendants<TextBox>(popup).Where(t=>t!=search).ToDictionary(t=>AutomationProperties.GetName(t)+"|"+t.Header,t=>t.Text);
        void CheckDraft()
        {
            if(!ReferenceEquals(Popup(),popup)||!identities.SequenceEqual(chosen.Items.OfType<ReportListItem>().Select(i=>i.Execution.ExecutionId).Order())||fields.Any(p=>VisualDescendants<TextBox>(popup).Single(t=>AutomationProperties.GetName(t)+"|"+t.Header==p.Key).Text!=p.Value)||fixture.Digitizations.Count!=0)
                throw new InvalidOperationException("A notice or failed search lost the ten selected executions, date, time or reason.");
        }
        search.Text="JOB-INEXISTENTE";await Wait(()=>fixture.Queries.Last().Search=="JOB-INEXISTENTE"&&results.Items.Count==0);CheckDraft();
        fixture.RejectNextSearch=true;search.Text="JOB-ERROR";await Wait(()=>VisualDescendants<InfoBar>(popup).Any(b=>b.IsOpen&&b.Message.Contains("Intente nuevamente",StringComparison.Ordinal)));CheckDraft();
        batch.Hide();await Task.Delay(200);CheckDraft();
        var batchMessage=ReportScannerMessageProbe.AcceptAsync(_window,"Las pruebas seleccionadas y la hora fija se conservan.",async element=>{CheckDraft();await SaveReportReaderImage(element,"fixed-time-notice.png");});scanner.Emit("AR1G0");await batchMessage;await Task.Delay(150);CheckDraft();
        if(fixture.Scans!=1)throw new InvalidOperationException("A scanner wrote while reviewing the fixed-time batch.");
        scanner.Emit("AR10TC");await Wait(()=>ReportScannerMessageProbe.Find(_window) is not null);batch.Hide();await Wait(()=>ReportScannerMessageProbe.Find(_window) is null);CheckDraft();
        search.Text="OTHER-JOB";await Wait(()=>fixture.Queries.Last().Search=="OTHER-JOB"&&results.Items.Count==1);CheckDraft();
        foreach(var extra in chosen.Items.OfType<ReportListItem>().Where(i=>i.Execution.Job.StartsWith("FIXTURE-",StringComparison.Ordinal)&&i.Execution.Job!="FIXTURE-JOB").ToArray())
        {chosen.SelectedItem=extra;Invoke(VisualDescendants<Button>(popup).Single(b=>b.Content as string=="Quitar del lote"));}
        await Wait(()=>chosen.Items.Count==3);
        fixture.RejectNextDigitization=true;Invoke(VisualDescendants<Button>(popup).Single(b=>b.Content as string=="Confirmar y guardar lote"));
        await Wait(()=>fixture.Digitizations.Count==1&&VisualDescendants<InfoBar>(popup).Any(b=>b.IsOpen&&b.Message.Contains("Otro usuario",StringComparison.Ordinal)));
        if(fixture.Checks!=0||chosen.Items.Count!=3||Popup() is null)throw new InvalidOperationException("A rejected batch lost its selection or closed the dialog.");
        Invoke(VisualDescendants<Button>(popup).Single(b=>b.Content as string=="Confirmar y guardar lote"));await Wait(()=>fixture.Checks==1&&Popup() is null&&open.IsEnabled);
        var saved=fixture.Digitizations.Last();
        if(saved.Executions.Count!=3||saved.Executions.Select(x=>x.ExecutionId).Distinct().Count()!=3||fixture.Digitizations.Select(x=>x.SessionId).Distinct().Count()!=1||saved.OperationalAt.ToString("HH:mm",System.Globalization.CultureInfo.InvariantCulture)!="09:20")throw new InvalidOperationException("The fixed-time batch merged identities, lost retries or used another hour.");
        File.WriteAllText(Path.Combine(_directory,"report-reader-batch.json"),JsonSerializer.Serialize(new{Passed=true,Boundary="Actual WinUI and HTTP client, synthetic transport and reader; no production data or physical reader",VisibleRejection=true,MissingReportDoesNotMark=true,AutomaticScannerStillWorks=true,CancelDoesNotSave=true,SelectionAcrossSearches=true,RepeatedTestsRemainSeparate=true,ScannerBlockedInsideBatch=true,TenSelectionsSurviveMissingJob=true,SearchFailurePreservesDraft=true,NoticeKeepsBatchVisible=true,UnexpectedClosePreservesDraft=true,NoticeDismissalPreservesDraft=true,DateTimeReasonPreserved=true,RejectedBatchStaysOpen=true,RetryKeepsSession=true,Executions=saved.Executions.Count,OperationalAt=saved.OperationalAt},ReportOptions));
        _reportDirectView=view;_reportDirectTransport=fixture;
    }
    private async Task ShowReportScannerMessageBoxAsync()
    {
        _reportDirectView?.Dispose();_reportDirectTransport?.Dispose();var fixture=new ReportDirectHttp();var scanner=new ReportValidationScanner();
        var view=new PlanningReportListView(new(fixture),_=>Task.CompletedTask,()=>[PlanningPermissions.View,PlanningPermissions.ReportMark,PlanningPermissions.ReportReview],()=>false,scanner:scanner);
        var root=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star));root.Margin=new(12);root.Background=PlanningUi.Brush("LimsBackgroundBrush");root.RequestedTheme=ElementTheme.Dark;
        PlanningUi.Place(root,PlanningUi.Text("VALIDACIÓN LOCAL · modal LIMS · reporte ficticio",11,true),0);PlanningUi.Place(root,view,1);
        _window!.Content=root;PlanningUi.Track(_window);await Task.Delay(150);await view.Load();_reportDirectView=view;_reportDirectTransport=fixture;scanner.Emit("AR10TC");
    }
    private async Task SaveReportReaderImage(UIElement element,string name)
    {
        var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(element);var pixels=await bitmap.GetPixelsAsync();var bytes=new byte[pixels.Length];using(var reader=Windows.Storage.Streams.DataReader.FromBuffer(pixels))reader.ReadBytes(bytes);
        var folder=await Windows.Storage.StorageFolder.GetFolderFromPathAsync(_directory);var file=await folder.CreateFileAsync(name,Windows.Storage.CreationCollisionOption.ReplaceExisting);
        using var stream=await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,bytes);await encoder.FlushAsync();
    }
}
