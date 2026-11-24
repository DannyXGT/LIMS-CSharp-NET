using System.Diagnostics;
using System.Text.Json;
using Lims.Contracts.Planning;
using Lims.Desktop.Planning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Lims.Desktop.VisualHarness;

public partial class App
{
    private static readonly string[] ReportScrollTimes=["08:00","09:00","10:00"];
    private static readonly string[] ReportScrollViewports=["1440x900","1280x800"];
    private async Task ValidateReportListScrollAsync()
    {
        const int total=9038;
        var fetches=0;
        var rows=new ReportListRows(async(page,revision,ct)=>
        {
            await Task.Delay(20,ct);fetches++;
            var items=Enumerable.Range((page-1)*500,Math.Min(500,total-(page-1)*500)).Select(i=>
            {
                var original=i%3==0?"Determination of pH value in textiles — technical method ISO 3071":"Technical assay "+i;
                var row=new PlanningRow(Guid.NewGuid(),Guid.NewGuid(),"FIXTURE"+i.ToString("D7",System.Globalization.CultureInfo.InvariantCulture),new(2026,11,16),new(2026,11,30),original,i%3==0?"PH":"Prueba "+i,"Chemical","Regular",null,null,"Sin información",null,null,"ReportList","Cliente de prueba",null,false);
                return new ReportListItem(row,0,false,null,"manual");
            }).ToArray();
            return new ReportListPage(items,total,page,500,7,new(false,null));
        },default);
        var grid=new ReportListGrid{CanMark=()=>true};
        var root=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star),GridLength.Auto);root.Margin=new(16);root.RequestedTheme=ElementTheme.Dark;
        root.Background=PlanningUi.Brush("LimsBackgroundBrush");
        PlanningUi.Place(root,PlanningUi.Text("Report List · validación de desplazamiento · DATOS FICTICIOS",18),0);
        PlanningUi.Place(root,new Border{Child=grid,BorderBrush=PlanningUi.Brush("LimsBorderBrush"),BorderThickness=new(1)},1);
        var status=PlanningUi.Text("Cargando…",12,true);PlanningUi.Place(root,status,2);
        _window!.Content=root;_window.AppWindow.Resize(new SizeInt32(1440,900));
        await rows.LoadNext();grid.SetSource(rows);
        await Task.Delay(350);
        var list=VisualDescendants<ListView>(grid).Single();
        var scroll=VisualDescendants<ScrollViewer>(list).First();
        var maximumRealized=0;var checkedRows=0;
        ListViewItem[] Visible()=>VisualDescendants<ListViewItem>(list).Where(c=>
        {
            var p=c.TransformToVisual(list).TransformPoint(new());
            return c.ActualHeight>0&&p.Y>=0&&p.Y+c.ActualHeight<=scroll.ViewportHeight+1;
        }).ToArray();
        void Verify()
        {
            var realized=VisualDescendants<ListViewItem>(list).ToArray();maximumRealized=Math.Max(maximumRealized,realized.Length);
            var visible=Visible();
            if(visible.Length<5)throw new InvalidOperationException("The native viewport did not render its visible rows.");
            foreach(var container in visible)
            {
                if(container.ContentTemplateRoot is not Grid row||row.Tag is not ReportListItem item||row.Children.Count!=9)
                    throw new InvalidOperationException($"A visible recycled row is blank or incomplete: index={list.IndexFromContainer(container)}, root={container.ContentTemplateRoot?.GetType().Name}, children={(container.ContentTemplateRoot as Grid)?.Children.Count}, tag={(container.ContentTemplateRoot as Grid)?.Tag?.GetType().Name}.");
                if(((Border)row.Children[1]).Child is not TextBlock plan||plan.Text!=item.Execution.PlanningTest)
                    throw new InvalidOperationException("The Prueba cell did not retain its planning name.");
                checkedRows++;
            }
        }
        Verify();
        var originalGrid=(Grid)Visible()[0].ContentTemplateRoot;
        var originalCell=originalGrid.Children[1];
        var samples=new List<double>();
        for(int i=0;i<32;i++)
        {
            var timer=Stopwatch.StartNew();
            scroll.ChangeView(i%2==0?scroll.ScrollableWidth:0,null,null,true);
            await Task.Delay(20);samples.Add(timer.Elapsed.TotalMilliseconds);Verify();
        }
        if(!ReferenceEquals(originalCell,originalGrid.Children[1]))throw new InvalidOperationException("Horizontal scrolling rebuilt the row controls.");
        for(int i=0;i<12;i++)
        {
            scroll.ChangeView(null,scroll.ScrollableHeight,null,true);await Task.Delay(140);Verify();
        }
        if(rows.Count<1500||maximumRealized>=300||maximumRealized>=rows.Count)
            throw new InvalidOperationException("Incremental paging or bounded row virtualization failed.");
        for(int i=0;i<32;i++)
        {
            scroll.ChangeView(null,scroll.ScrollableHeight*(i%8)/8,null,true);await Task.Delay(20);Verify();
        }
        _window.AppWindow.Resize(new SizeInt32(1280,800));await Task.Delay(250);
        scroll.ChangeView(scroll.ScrollableWidth,scroll.ScrollableHeight/2,null,true);await Task.Delay(150);Verify();
        var header=VisualDescendants<StackPanel>(grid).Single(s=>s.Height==40);
        var visibleRow=(Grid)Visible()[0].ContentTemplateRoot;
        var headerX=header.Children[1].TransformToVisual(grid).TransformPoint(new()).X;
        var cellX=visibleRow.Children[1].TransformToVisual(grid).TransformPoint(new()).X;
        if(Math.Abs(headerX-cellX)>1)throw new InvalidOperationException("The header and row columns lost horizontal alignment.");
        var members=new ReportNativeTable<string>("Fixture horarios",[("Horario",140),("Estado",200)],s=>[s,"Pendiente"]);
        members.SetItems(ReportScrollTimes);
        _window.Content=members;await Task.Delay(200);
        if(!VisualDescendants<TextBlock>(members).Any(t=>t.Text=="09:00"))throw new InvalidOperationException("The native schedules table did not render its rows.");
        _window.Content=root;_window.AppWindow.Resize(new SizeInt32(1440,900));await Task.Delay(200);
        scroll.ChangeView(0,0,null,true);await Task.Delay(150);Verify();
        status.Text=$"{rows.Count:N0} de {total:N0} ejecuciones · {maximumRealized} filas realizadas · Prueba: nombre de planificación · DATOS FICTICIOS";
        File.WriteAllText(Path.Combine(_directory,"report-list-scroll.json"),JsonSerializer.Serialize(new
        {
            Passed=true,Boundary="Real WinUI presentation with synthetic paged fixtures; no database, API or production writes",
            Loaded=rows.Count,Total=total,Requests=fetches,MaximumRealized=maximumRealized,CheckedVisibleRows=checkedRows,
            PlanningNameOnly="passed",HorizontalControlReuse="passed",HeaderAlignment="passed",SchedulesRendering="passed",
            Viewports=ReportScrollViewports,ScrollTurnMillisecondsP95=samples.Order().ElementAt((int)(samples.Count*.95)),
            Note="Timing includes a 20 ms wait and is not a LAN or target-hardware benchmark"
        },ReportOptions));
    }
}
