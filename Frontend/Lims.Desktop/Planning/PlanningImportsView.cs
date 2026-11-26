using System.Globalization;
using Lims.Contracts.Planning;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Planning;

public sealed partial class PlanningImportsView : Grid, IDisposable
{
    private readonly PlanningApiClient _api;
    private readonly Func<Guid,Task> _open;
    private readonly Func<IReadOnlyList<string>> _permissions;
    private PlanningImportPreview? _preview;
    private bool _busy,_initialized,_shortWindow;
    private readonly TextBlock _status=PlanningUi.Text("",12,true);
    private readonly ProgressBar _progress=new(){Height=2,Visibility=Visibility.Collapsed};
    private readonly ContentControl _section=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
    private readonly LimsTabStrip _tabs=new();
    private readonly LimsTabItem _previewTab=new(){Label="Nueva importación"};
    private readonly Expander _flow=new(){HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
    private readonly Grid _results=new(){ColumnSpacing=0};
    private readonly ContentControl _resultHost=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch};
    private Border? _resultSurface;
    private FrameworkElement _reviewPanel=null!,_differencePanel=null!,_historyPanel=null!;
    private Button _new=null!,_confirm=null!;
    private CancellationTokenSource? _load;
    public event Func<Task>? Applied;
    public event Func<Guid,Task>? OpenImportedJobs;
    private readonly List<Window> _importWindows=[];
    public PlanningImportsView(PlanningApiClient api,Func<Guid,Task> open,Func<IReadOnlyList<string>> permissions)
    {
        _api=api;_open=open;_permissions=permissions;RowSpacing=0;
        foreach(var height in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star)})RowDefinitions.Add(new(){Height=height});
        var title=PlanningUi.Text("Centro de Importaciones",20);title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;title.VerticalAlignment=VerticalAlignment.Center;
        var icon=new Border{Width=34,Height=34,CornerRadius=new(6),Background=PlanningUi.Brush("LimsAccentSoftBrush"),Child=new LimsIcon{Icon=LimsIconKind.Import,Size=20,Foreground=PlanningUi.Brush("LimsAccentBrush")}};
        var identity=PlanningUi.Row(icon,title);identity.Spacing=10;identity.Margin=new(0,0,24,0);identity.VerticalAlignment=VerticalAlignment.Center;
        var heading=new Grid{Padding=new(12,4,12,4)};heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});heading.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});heading.RowDefinitions.Add(new(){Height=GridLength.Auto});heading.RowDefinitions.Add(new(){Height=GridLength.Auto});heading.Children.Add(identity);
        _new=Action("Nueva importación",LimsIconKind.Add,()=>{BeginImport();return Task.CompletedTask;},true);
        var history=Action("Ver historial",LimsIconKind.Traceability,async()=>{_tabs.SelectedIndex=2;await LoadHistory(CancellationToken.None);});
        history.Visibility=Visibility.Collapsed;_new.Visibility=Visibility.Collapsed;
        PlanningUi.Place(this,Surface(heading,new Thickness(0)),0);
        BuildFlow();_flow.Margin=new(0,8,0,0);PlanningUi.Place(this,_flow,1);PlanningUi.Place(this,_resultHost,2);
        _reviewPanel=ReviewPanel();_differencePanel=DifferencePanel();_historyPanel=HistoryPanel();
        _tabs.Items.Add(_previewTab);_tabs.Items.Add(new(){Label="Bandejas de revisión"});_tabs.Items.Add(new(){Label="Historial"});
        AutomationProperties.SetName(_tabs,"Secciones del Centro de Importaciones");
        _tabs.SelectionChanged+=async(_,_)=>{SelectSection();if(_tabs.SelectedIndex!=0)await Load();};_tabs.SelectedIndex=0;Grid.SetColumn(_tabs,1);heading.Children.Add(_tabs);_tabs.VerticalAlignment=VerticalAlignment.Center;
        Grid.SetColumn(_reviewScope,2);heading.Children.Add(_reviewScope);_reviewScope.Margin=new(12,0,0,0);_reviewScope.Width=310;_reviewScope.VerticalAlignment=VerticalAlignment.Center;
        void ArrangeHeading(){var narrow=heading.ActualWidth<(_tabs.SelectedIndex==1?1310:900);Grid.SetRow(_tabs,narrow?1:0);Grid.SetColumn(_tabs,narrow?0:1);Grid.SetColumnSpan(_tabs,narrow?3:1);identity.Margin=narrow?new(0,2,12,2):new(0,0,24,0);}
        heading.SizeChanged+=(_,_)=>ArrangeHeading();_tabs.SelectionChanged+=(_,_)=>ArrangeHeading();
        _status.Visibility=Visibility.Collapsed;var message=PlanningUi.Stack(_progress,_status);message.Spacing=2;PlanningUi.Place(this,message,3);_section.Margin=new(0,8,0,0);PlanningUi.Place(this,_section,4);
        _flow.Header="Importar archivos de planificación";_flow.IsExpanded=true;
        SizeChanged+=(_,e)=>{_shortWindow=e.NewSize.Height<650;CompactSurface();};_flow.RegisterPropertyChangedCallback(Expander.IsExpandedProperty,(_,_)=>CompactSurface());
        SelectSection();
    }
    private void CompactSurface()
    {
        _resultHost.Visibility=Visibility.Collapsed;
        if(ActualHeight>0)_flowScroll.MaxHeight=Math.Max(240,ActualHeight-130-(_preview is null?0:160));
    }
    private static Button Action(string text,LimsIconKind icon,Func<Task> action,bool primary=false)
    {
        var button=PlanningUi.Button(text,action,primary);LimsAction.SetIcon(button,icon);AutomationProperties.SetName(button,text);button.MinHeight=34;return button;
    }
    private static Border Surface(UIElement child,Thickness? padding=null)=>new(){Child=child,Background=PlanningUi.Brush("LimsSurfaceBrush"),BorderBrush=PlanningUi.Brush("LimsBorderBrush"),BorderThickness=new(1),CornerRadius=new(8),Padding=padding??new Thickness(12)};
    private static Grid Split(FrameworkElement left,FrameworkElement right)
    {
        var grid=new Grid{ColumnSpacing=8};grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});grid.Children.Add(left);Grid.SetColumn(right,1);grid.Children.Add(right);return grid;
    }
    private static Grid ResponsiveSplit(FrameworkElement left,FrameworkElement right)
    {
        var grid=Split(left,right);grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowSpacing=6;
        grid.SizeChanged+=(_,e)=>{var narrow=e.NewSize.Width<900;Grid.SetColumnSpan(left,narrow?2:1);Grid.SetColumn(right,narrow?0:1);Grid.SetRow(right,narrow?1:0);Grid.SetColumnSpan(right,narrow?2:1);right.HorizontalAlignment=HorizontalAlignment.Right;};return grid;
    }
    private void Message(string text){_status.Text=text;_status.Visibility=string.IsNullOrEmpty(text)?Visibility.Collapsed:Visibility.Visible;}
    private void Busy(bool busy,string? text=null)
    {
        _busy=busy;_new.IsEnabled=!busy;_progress.IsIndeterminate=busy;_progress.Visibility=busy?Visibility.Visible:Visibility.Collapsed;
        _flow.IsEnabled=!busy;_confirm.IsEnabled=!busy&&_step==3&&_preview is{Status:"Preview"} p&&!p.Issues.Any(i=>i.Blocking);
        UpdateImportActions();
        if(text is not null)Message(text);
    }
    private void SelectSection()
    {
        if(_tabs.SelectedIndex==0&&!_busy){Cancel();_progress.Visibility=Visibility.Collapsed;Message("");}
        if(_tabs.SelectedIndex==0&&!_busy&&_preview?.Status=="Applied"){BeginImport();}
        _flow.Visibility=_tabs.SelectedIndex==0?Visibility.Visible:Visibility.Collapsed;
        _reviewScope.Visibility=_tabs.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;
        _resultHost.Visibility=Visibility.Collapsed;
        _section.Content=_tabs.SelectedIndex switch{0=>_differencePanel,2=>_historyPanel,_=>_reviewPanel};
        if(_tabs.SelectedIndex==0&&_preview is{} preview)ShowDifferences(preview);
        _differencePanel.Visibility=_preview is null?Visibility.Collapsed:Visibility.Visible;
    }
    private void Show(PlanningImportPreview preview,bool history=false)
    {
        _preview=preview;
        if(history){_updatingFlow=true;_summary=_report=_supplement=null;_importingSide2=preview.Cut.Policy=="HtmlReactSide2";_day.Date=new DateTimeOffset(preview.Cut.Day.ToDateTime(TimeOnly.MinValue),TimeSpan.FromHours(-6));_window.SelectedIndex=preview.Cut.Window switch{"Noon"=>1,"Last"=>2,_=>0};_updatingFlow=false;}
        UpdateFiles(preview);SetStep(preview.Status=="Applied"?4:2);
        _differencePanel.Visibility=Visibility.Visible;
        _flow.Header=$"{(preview.Status=="Applied"?"Lote confirmado":"Importación en revisión")} · {preview.Cut.Day:yyyy-MM-dd} · {CutLabel(preview.Cut.Window)} · Procedencia y validación";
        _flow.IsExpanded=preview.Status!="Applied";ShowResults(preview);SetReviewBatch(preview.Id);
        _previewTab.IsEnabled=true;_tabs.SelectedIndex=preview.Status=="Applied"?1:0;
        ShowDifferences(preview);
    }
    private static string CutLabel(string cut)=>cut switch{"Noon"=>"Mediodía","Last"=>"Última carga",_=>"Primera carga"};
    private void ShowResults(PlanningImportPreview preview)
    {
        _results.Children.Clear();_results.ColumnDefinitions.Clear();
        var wip=preview.Changes.Where(c=>c.Entity=="WIP"&&c.Action=="UpdateSource").Select(c=>c.EpisodeId).Distinct().Count();
        var metrics=new (string Label,int Count,string Kind)[]{("JOBs nuevos",preview.NewJobs,"NewJobs"),("Pruebas nuevas",preview.NewTests,"NewTests"),("JOBs actualizados",preview.UpdatedJobs,"Updated"),("Cambios WIP",wip,"WIP"),("Por revisar en el lote",preview.PendingReview,"Review")};
        foreach(var (label,count,kind) in metrics)
        {
            var col=_results.ColumnDefinitions.Count;_results.ColumnDefinitions.Add(new());
            var value=PlanningUi.Text(count.ToString("N0",CultureInfo.InvariantCulture),21);value.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
            if(kind=="Review")value.Foreground=PlanningUi.Brush("LimsWarningBrush");
            var stack=PlanningUi.Stack(value,PlanningUi.Text(label,12,true));stack.Spacing=1;
            var button=new Button{Content=stack,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new(12,5,12,5),Background=PlanningUi.Brush("LimsSurfaceBrush"),BorderThickness=new(0)};
            AutomationProperties.SetName(button,label+": "+count.ToString(CultureInfo.InvariantCulture));
            button.Click+=(_,_)=>{_metricKind=kind;_tabs.SelectedIndex=0;FilterDifferences();};
            Grid.SetColumn(button,col);_results.Children.Add(button);
        }
        _resultSurface??=Surface(_results,new Thickness(0,3,0,3));_resultHost.Content=_resultSurface;
    }
    public async Task Load()
    {
        var canImport=_permissions().Contains(PlanningPermissions.Import,StringComparer.Ordinal);_new.Visibility=Visibility.Collapsed;_previewTab.IsEnabled=canImport;
        if(_tabs.SelectedIndex==0)
        {
            if(!_initialized){_initialized=true;if(canImport)BeginImport();else _tabs.SelectedIndex=1;}
            return;
        }
        if(_busy)return;Cancel();_load=new();var ct=_load.Token;
        try
        {
            await LoadHistory(ct);if(ct.IsCancellationRequested)return;
            if(_tabs.SelectedIndex==1)await RefreshReviews(ct);
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){ }
    }
    private async Task Changed()
    {await RefreshReviews();if(Applied is{} applied)await applied();}
    public async Task OpenReviewWorkspace(Guid? batch=null)
    {
        if(_busy)return;
        _tabs.SelectedIndex=1;_situation.SelectedIndex=1;_reviewPage=1;if(batch is{} id)SetReviewBatch(id);
        await Load();
        if(batch is{} selected&&_reviewBatch!=selected){SetReviewBatch(selected);await RefreshReviews();}
    }
    private void TrackImportWindow(Window window){_importWindows.Add(window);PlanningUi.Track(window);window.Closed+=(_,_)=>_importWindows.Remove(window);window.Activate();}
    private async Task Summary(Guid id)
    {
        var result=await _api.InspectImport(id,CancellationToken.None);if(result.Value is not{} data){Message(result.Error??"No se pudo consultar el resumen.");return;}
        TrackImportWindow(new PlanningImportSummaryWindow(data,async batch=>{if(OpenImportedJobs is{} open)await open(batch);},batch=>OpenReviewWorkspace(batch)));
    }
    public void Cancel(){_searchDelay.Stop();_detailDelay.Stop();_detailLoad?.Cancel();_load?.Cancel();_load?.Dispose();_load=null;_reviewLoad?.Cancel();}
    public void Clear(){Cancel();foreach(var window in _manualWindows.ToArray())window.Close();foreach(var window in _importWindows.ToArray())window.Close();_preview=null;_initialized=false;_summary=_report=null;_reviewRows=[];_reviewCells.Model.Rows.Clear();CloseReviewDetail();_changes.ItemsSource=null;_history.ItemsSource=null;_resultHost.Content=null;Message("");_flow.IsExpanded=false;}
    public void Dispose(){Clear();_reviewLoad?.Dispose();foreach(var window in _manualWindows.ToArray())window.Close();}
}
