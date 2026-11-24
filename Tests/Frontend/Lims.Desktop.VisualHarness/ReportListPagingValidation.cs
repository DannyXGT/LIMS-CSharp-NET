using System.Text.Json;
using Lims.Contracts.Planning;
using Lims.Desktop.Planning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Lims.Desktop.VisualHarness;

public partial class App
{
    private static readonly int[] PagingExpectedCounts=[500,500,203];
    private async Task ValidateReportListPagingAsync()
    {
        _reportDirectView?.Dispose();_reportDirectTransport?.Dispose();var fixture=new ReportDirectHttp(1203);
        var view=new PlanningReportListView(new(fixture),_=>Task.CompletedTask,()=>[PlanningPermissions.View,PlanningPermissions.ReportMark,PlanningPermissions.ReportReview],()=>false,scannerEnabled:false);
        var root=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star));root.Margin=new(12);root.Background=PlanningUi.Brush("LimsBackgroundBrush");root.RequestedTheme=ElementTheme.Dark;
        PlanningUi.Place(root,PlanningUi.Text("VALIDACIÓN LOCAL · 1.203 pruebas ficticias · paginación y búsqueda",11,true),0);PlanningUi.Place(root,view,1);
        _window!.Content=root;_window.AppWindow.Resize(new SizeInt32(1366,768));await Task.Delay(250);await view.Load();await Task.Delay(250);
        var grid=VisualDescendants<ReportListGrid>(view).Single();var list=VisualDescendants<ListView>(grid).Single();
        ReportListRows Rows()=>(ReportListRows)list.ItemsSource;
        Button Button(string text)=>VisualDescendants<Button>(view).Single(b=>b.Content as string==text);
        static void Invoke(Button b)=>((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)).Invoke();
        async Task Wait(Func<bool> done){for(var i=0;i<200&&!done();i++)await Task.Delay(30);if(!done())throw new InvalidOperationException("Paged Report List did not settle.");await Task.Delay(120);}
        if(Rows().Count!=500||Rows().Total!=1203||Rows().HasMoreItems)throw new InvalidOperationException("Initial page did not stop at 500 rows.");
        Invoke(Button("Siguiente"));await Wait(()=>Rows().Page==2&&Button("Siguiente").IsEnabled);
        if(Rows().Count!=500||Rows()[0].Execution.Job!="FIXTURE-501")throw new InvalidOperationException("Second page skipped or mixed executions.");
        Invoke(Button("Siguiente"));await Wait(()=>Rows().Page==3&&Button("Anterior").IsEnabled);
        if(Rows().Count!=203||Button("Siguiente").IsEnabled)throw new InvalidOperationException("Last page count or navigation is wrong.");
        Invoke(Button("Anterior"));await Wait(()=>Rows().Page==2&&Rows().Count==500);
        var search=VisualDescendants<TextBox>(view).Single(t=>AutomationProperties.GetName(t)=="Buscar JOB en Report List");
        var requests=fixture.Queries.Count;search.Text="PRUEBA";await Task.Delay(50);search.Text="PRUEBA 11";await Task.Delay(50);search.Text="PRUEBA 1199";
        await Wait(()=>Rows().Count==1&&Rows()[0].Execution.Job=="FIXTURE-1199");
        if(Rows().Page!=1||fixture.Queries.Count-requests!=1)throw new InvalidOperationException("Typing did not coalesce into one server query on page 1.");
        search.Text="";await Wait(()=>Rows().Page==1&&Rows().Count==500);
        var scroll=VisualDescendants<ScrollViewer>(list).First();scroll.ChangeView(null,scroll.ScrollableHeight,null,true);await Task.Delay(350);
        if(Rows().Count!=500)throw new InvalidOperationException("Scrolling appended another page.");scroll.ChangeView(null,0,null,true);
        foreach(var size in new[]{new SizeInt32(1366,768),new SizeInt32(1280,800)})
        {
            _window.AppWindow.Resize(size);await Task.Delay(300);if(scroll.ScrollableWidth>1)throw new InvalidOperationException("Default table overflows horizontally.");
            foreach(var element in new FrameworkElement[]{search,VisualDescendants<Button>(view).Single(b=>AutomationProperties.GetName(b)=="Hora de digitación"),VisualDescendants<Button>(view).Single(b=>AutomationProperties.GetName(b)=="Estado del lector"),Button("Anterior"),Button("Siguiente"),grid})
            {var point=element.TransformToVisual(root).TransformPoint(new());if(point.X<0||point.Y<0||point.X+element.ActualWidth>root.ActualWidth+1||point.Y+element.ActualHeight>root.ActualHeight+1)throw new InvalidOperationException("A primary control falls outside the page.");}
        }
        File.WriteAllText(Path.Combine(_directory,"report-list-paging.json"),JsonSerializer.Serialize(new{Passed=true,Boundary="Real WinUI and HTTP client with synthetic responses; no server or physical scanner",Total=1203,PageSize=500,Pages=PagingExpectedCounts,DebouncedQueries=1,ScrollDoesNotAppend=true,Viewports=DirectReportViewports},ReportOptions));
        _reportDirectView=view;_reportDirectTransport=fixture;
    }
}
