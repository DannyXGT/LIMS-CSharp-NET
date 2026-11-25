using System.Globalization;
using System.Text.Json;
using Lims.Contracts.Planning;
using Lims.DesignSystem.Controls;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Lims.Desktop.Planning;

public sealed partial class PlanningImportsView
{
    private sealed record ReviewColumn(string Key,string Label,int Width);
    private static readonly (string Key,string Label)[] Queues=[("RevisionJobs","REV y JOBs con guion"),("SummaryWithoutReport","Solo Summary"),("ReportWithoutSummary","Solo Report List"),("MissingParty","Sin Buyer/Vendor"),("MissingEquivalence","Sin equivalencia"),("MissingTests","Pruebas por agregar"),("IncompleteData","Datos incompletos"),("Admission","Admisión al trabajo activo"),("ImportError","Errores de importación"),("Reviewed","Retenidos y resueltos")];
    private static readonly ReviewColumn[] AllReviewColumns=[new("Job","JOB",185),new("Reason","Motivo",280),new("State","Situación",100),new("InDate","Fecha ingreso",105),new("DueDate","Due Date",105),new("OfficialState","Estado TIPS",145),new("Buyer","Buyer",165),new("Vendor","Vendor",175),new("Service","Servicio",110),new("Usd","USD",90),new("CaseCategory","Tipo de caso",190),new("OriginalTest","Prueba original",280),new("Equivalence","Equivalencia",180),new("Laboratory","Laboratorio",120),new("Origin","Origen",110),new("ImportedAt","Fecha de lote",160)];
    private readonly StackPanel _queueButtons=new(){Spacing=4};
    private sealed record QueueChoice(string Key,string Label);
    private readonly ComboBox _queuePicker=new(){DisplayMemberPath=nameof(QueueChoice.Label),Width=240,Visibility=Visibility.Collapsed};
    private readonly Dictionary<string,(Button Button,TextBlock Title,TextBlock Count,TextBlock Pending)> _queueNavigation=[];
    private bool _paintingQueue;
    private readonly TextBox _reviewSearch=PlanningUi.Field("",max:2000);
    private readonly ComboBox _situation=new(){ItemsSource=new[]{"Abiertos (pendientes y retenidos)","Pendientes","Retenidos","Resueltos","Todos","Excluidos justificadamente"},SelectedIndex=1,Width=200};
    private sealed record BatchChoice(Guid? Id,string Label);
    private readonly ComboBox _reviewScope=new(){DisplayMemberPath=nameof(BatchChoice.Label),Width=340};
    private Guid? _reviewBatch;
    private bool _paintingScope,_reviewLoading;
    private readonly CalendarDatePicker _importFrom=new(){Header="Importado desde",DateFormat="{year.full}-{month.integer(2)}-{day.integer(2)}"},_importUntil=new(){Header="Hasta",DateFormat="{year.full}-{month.integer(2)}-{day.integer(2)}"};
    private readonly TextBox _reviewTest=PlanningUi.Field("Prueba original o equivalencia"),_reviewLab=PlanningUi.Field("Laboratorio");
    private readonly ComboBox _reviewOrigin=new(){Header="Origen",ItemsSource=new[]{"Todos","Summary","Report List","Manual"},SelectedIndex=0};
    private readonly PlanningSheetGrid _reviewCells=new(true,"Celdas de bandeja de revisión"){AllowColumnLayout=true};
    private Button _completeData=null!,_exclude=null!,_reviewActions=null!,_detailButton=null!;
    private readonly TextBlock _reviewCount=PlanningUi.Text("",12,true),_reviewSelection=PlanningUi.Text("0 celdas seleccionadas",12,true),_activeFilters=PlanningUi.Text("",12,true),_queueSummary=PlanningUi.Text("",12,true);
    private readonly TextBlock _reviewEmpty=PlanningUi.Text("Cargando bandeja…",14,true);
    private readonly TextBlock _reviewActionHint=PlanningUi.Text("",12,true);
    private Button _reviewPrevious=null!,_reviewNext=null!,_addTests=null!,_hold=null!,_resolve=null!,_reopen=null!,_openReview=null!,_identity=null!,_equivalence=null!;
    private string _reviewCategory="SummaryWithoutReport",_reviewSort="Job";
    private int _reviewPage=1;
    private bool _reviewDescending;
    private readonly Dictionary<string,string> _columnFilters=[];
    private ReviewColumn[] _reviewColumns=[];
    private readonly HashSet<string> _visibleReviewColumns=AllReviewColumns.Take(10).Select(c=>c.Key).Append("CaseCategory").ToHashSet();
    private IReadOnlyList<PlanningReviewCase> _reviewRows=[];
    private CancellationTokenSource? _reviewLoad;
    private IReadOnlyList<PlanningReviewCounter> _reviewCounters=[];
    private readonly List<PlanningManualTestWindow> _manualWindows=[];
    private PlanningReviewCase[] SelectedReviews()
    {
        if(_reviewRows.Count==0||_reviewLoading)return [];
        var range=_reviewCells.Model.Range;return _reviewRows.Skip(range.Top).Take(range.Bottom-range.Top+1).ToArray();
    }
    private PlanningReviewCase? SelectedReview=>SelectedReviews() is [var row]?row:null;
    private PlanningReviewQuery ReviewQuery()=>new(_reviewCategory,_situation.SelectedIndex switch{0=>"Open",2=>"Held",3=>"Resolved",4=>"All",5=>"Excluded",_=>"Pending"},_reviewSearch.Text,_reviewSort,_reviewDescending,_reviewPage,100,_columnFilters.Count==0?null:JsonSerializer.Serialize(_columnFilters),
        Scope:_reviewBatch is null?"History":"Batch",BatchId:_reviewBatch,ImportedFrom:_importFrom.Date is{} from?DateOnly.FromDateTime(from.DateTime):null,ImportedUntil:_importUntil.Date is{} until?DateOnly.FromDateTime(until.DateTime):null,
        Test:_reviewTest.Text,Laboratory:_reviewLab.Text,Origin:_reviewOrigin.SelectedIndex switch{1=>"Summary",2=>"ReportList",3=>"Manual",_=>null});
    private Grid ReviewPanel()
    {
        var layout=new Grid{ColumnSpacing=10};layout.ColumnDefinitions.Add(new(){Width=GridLength.Auto});layout.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        var content=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star),GridLength.Auto);content.RowSpacing=8;Grid.SetColumn(content,1);layout.Children.Add(content);
        _reviewScope.Style=(Style)Application.Current.Resources["LimsCompactComboBoxStyle"];AutomationProperties.SetName(_reviewScope,"Alcance de bandejas");
        _reviewScope.SelectionChanged+=async(_,_)=>{if(!_paintingScope&&_reviewScope.SelectedItem is BatchChoice choice){_reviewBatch=choice.Id;_reviewPage=1;CloseReviewDetail();await RefreshReviews();}};
        var railTitle=PlanningUi.Text("Bandejas",14);railTitle.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;railTitle.Margin=new(10,4,0,6);
        var railCountLabel=PlanningUi.Text("Casos",11,true);railCountLabel.Margin=new(0,4,10,6);railCountLabel.VerticalAlignment=VerticalAlignment.Center;
        var railContent=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star),GridLength.Auto);railContent.RowSpacing=4;PlanningUi.Place(railContent,Split(railTitle,railCountLabel),0);PlanningUi.Place(railContent,new ScrollViewer{Content=_queueButtons,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},1);
        var overlap=PlanningUi.Text("Cada JOB aparece en una sola bandeja.",11,true);overlap.Margin=new(10,8,10,4);PlanningUi.Place(railContent,overlap,2);
        var rail=Surface(railContent,new Thickness(6));rail.Width=224;AutomationProperties.SetName(rail,"Bandejas disponibles y sus contadores");layout.Children.Add(rail);
        _queuePicker.Style=(Style)Application.Current.Resources["LimsCompactComboBoxStyle"];AutomationProperties.SetName(_queuePicker,"Bandeja de revisión");_queuePicker.Margin=new(0,0,8,0);
        _queuePicker.SelectionChanged+=async(_,_)=>{if(!_paintingQueue&&_queuePicker.SelectedItem is QueueChoice choice&&choice.Key!=_reviewCategory)await SelectQueue(choice.Key);};
        layout.SizeChanged+=(_,e)=>{var compact=e.NewSize.Width<1100;rail.Visibility=compact?Visibility.Collapsed:Visibility.Visible;_queuePicker.Visibility=compact?Visibility.Visible:Visibility.Collapsed;layout.ColumnSpacing=compact?0:10;};
        _reviewSearch.Header=null;_reviewSearch.PlaceholderText="Buscar JOB o prueba";_reviewSearch.MinWidth=160;AutomationProperties.SetName(_reviewSearch,"Buscar JOB en revisión");
        _reviewSearch.KeyDown+=async(_,e)=>{if(e.Key==VirtualKey.Enter){_reviewPage=1;await RefreshReviews();e.Handled=true;}};
        _reviewSearch.TextChanged+=(_,_)=>{_searchDelay.Stop();_searchDelay.Start();};
        _searchDelay.Tick+=async(_,_)=>{_searchDelay.Stop();_reviewPage=1;await RefreshReviews();};
        _situation.Style=(Style)Application.Current.Resources["LimsCompactComboBoxStyle"];AutomationProperties.SetName(_situation,"Situación de revisión");
        _situation.SelectionChanged+=async(_,_)=>{if(_switchingQueue)return;_reviewPage=1;await RefreshReviews();};
        var more=ReviewIcon("Filtros",LimsIconKind.Filter);more.Flyout=ReviewFilters();
        var columns=ReviewIcon("Columnas",LimsIconKind.Administration);columns.Flyout=ReviewColumns();
        var export=Action("Exportar",LimsIconKind.Export,()=>Task.CompletedTask);var menu=new MenuFlyout();
        foreach(var (label,mode) in new[]{("Registros filtrados",0),("Filas seleccionadas",1),("Categoría completa en este alcance",2)}){var item=new MenuFlyoutItem{Text=label};item.Click+=async(_,_)=>await ExportReviewScope(mode);menu.Items.Add(item);}export.Flyout=menu;
        _situation.Width=165;
        var searchEntry=new Grid();_reviewSearch.Padding=new(32,5,8,5);searchEntry.Children.Add(_reviewSearch);searchEntry.Children.Add(new LimsIcon{Icon=LimsIconKind.Search,Size=15,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,Margin=new(10,0,0,0),Foreground=PlanningUi.Brush("LimsTextSecondaryBrush"),IsHitTestVisible=false});
        var searchGroup=new Grid();searchGroup.ColumnDefinitions.Add(new(){Width=GridLength.Auto});searchGroup.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});searchGroup.Children.Add(_queuePicker);Grid.SetColumn(searchEntry,1);searchGroup.Children.Add(searchEntry);
        var tools=PlanningUi.Row(_situation,more,columns,export);tools.Spacing=6;tools.VerticalAlignment=VerticalAlignment.Center;
        var query=new Grid{ColumnSpacing=8};query.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});query.ColumnDefinitions.Add(new(){Width=GridLength.Auto});query.RowDefinitions.Add(new(){Height=GridLength.Auto});query.RowDefinitions.Add(new(){Height=GridLength.Auto});query.Children.Add(searchGroup);Grid.SetColumn(tools,1);query.Children.Add(tools);
        query.SizeChanged+=(_,e)=>{var narrow=e.NewSize.Width<800;Grid.SetColumnSpan(searchGroup,narrow?2:1);Grid.SetRow(tools,narrow?1:0);Grid.SetColumn(tools,narrow?0:1);Grid.SetColumnSpan(tools,narrow?2:1);tools.Margin=narrow?new(0,6,0,0):new(0);};
        _openReview=Action("Expediente",LimsIconKind.ViewDetail,OpenSelectedReview);
        _addTests=Action("Agregar pruebas",LimsIconKind.Add,AddTests,true);_hold=Action("Retener",LimsIconKind.Lock,()=>DecideReview("Held"));
        _resolve=Action("Resolver",LimsIconKind.Check,()=>DecideReview("Resolved"));_reopen=Action("Reabrir",LimsIconKind.Refresh,()=>DecideReview("Pending"));
        _identity=Action("Conciliar identidad",LimsIconKind.Traceability,ResolveIdentity);_equivalence=Action("Clasificar prueba",LimsIconKind.Edit,ClassifyReview);
        _completeData=Action("Completar datos",LimsIconKind.Edit,CompleteReviewData);_exclude=Action("Excluir justificadamente",LimsIconKind.Cancel,()=>DecideReview("Excluded"));
        _reviewActions=Action("Acciones",LimsIconKind.Edit,()=>Task.CompletedTask);
        _reviewActionHint.MaxWidth=300;
        var actions=new Flyout{Content=PlanningUi.Stack(_openReview,_addTests,_completeData,_equivalence,_identity,_hold,_resolve,_reopen,_exclude,_reviewActionHint)};_reviewActions.Flyout=actions;
        // Closing the menu before invoking an operation also prevents overlapping confirmation surfaces.
        foreach(var button in new[]{_openReview,_addTests,_completeData,_equivalence,_identity,_hold,_resolve,_reopen,_exclude})button.Click+=(_,_)=>{actions.Hide();UpdateReviewActions();};
        _detailButton=Action("Ver detalle",LimsIconKind.ViewDetail,async()=>{_detailOpen=true;await ShowReviewDetail();});
        var contextual=PlanningUi.Row(_detailButton,_reviewActions);contextual.Spacing=6;
        _queueSummary.MaxLines=2;_queueSummary.VerticalAlignment=VerticalAlignment.Center;
        var toolbar=PlanningUi.Rows(GridLength.Auto,GridLength.Auto);toolbar.RowSpacing=6;PlanningUi.Place(toolbar,query,0);PlanningUi.Place(toolbar,Split(_queueSummary,contextual),1);PlanningUi.Place(content,Surface(toolbar,new Thickness(10,8,10,8)),0);
        BuildDetailHost();
        var area=new Grid();area.Children.Add(Surface(_reviewCells,new Thickness(0)));_reviewEmpty.HorizontalAlignment=HorizontalAlignment.Center;_reviewEmpty.VerticalAlignment=VerticalAlignment.Center;_reviewEmpty.IsHitTestVisible=false;area.Children.Add(_reviewEmpty);
        _reviewDetailHost.Children.Add(area);_reviewDetailHost.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});_reviewDetailHost.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        Grid.SetColumn(_reviewDetail,1);_reviewDetailHost.Children.Add(_reviewDetail);PlanningUi.Place(content,_reviewDetailHost,1);
        _reviewCells.CopyRequested+=CopyReviews;_reviewCells.OpenRequested+=OpenSelectedReview;
        _reviewCells.PasteRequested+=()=>{Message("Las fuentes son de solo lectura. Usa Completar datos para una corrección WIP autorizada con motivo y procedencia.");return Task.CompletedTask;};
        _reviewCells.SortRequested+=async key=>{_reviewDescending=_reviewSort==key&&!_reviewDescending;_reviewSort=key;_reviewPage=1;await RefreshReviews();};
        _reviewCells.FilterRequested+=(key,anchor)=>{var column=AllReviewColumns.FirstOrDefault(c=>c.Key==key);if(column is not null)ReviewFilter(column,anchor);};
        _reviewCells.SelectionChanged+=()=>{UpdateReviewActions();if(_detailOpen){_detailDelay.Stop();_detailDelay.Start();}};
        _detailDelay.Tick+=async(_,_)=>{_detailDelay.Stop();await ShowReviewDetail();};
        _reviewPrevious=Action("Anterior",LimsIconKind.ChevronLeft,async()=>{if(_reviewPage>1){_reviewPage--;await RefreshReviews();}});
        _reviewNext=Action("Siguiente",LimsIconKind.ChevronRight,async()=>{_reviewPage++;await RefreshReviews();});_reviewPrevious.IsEnabled=_reviewNext.IsEnabled=false;
        var page=PlanningUi.Row(_reviewPrevious,_reviewNext);page.Spacing=6;
        var footer=PlanningUi.Stack(_reviewCount,_reviewSelection);footer.Spacing=1;PlanningUi.Place(content,Split(footer,page),2);
        AutomationProperties.SetHelpText(_reviewCells,"Ctrl+C copiar · Ctrl+A página cargada · Shift+flechas ampliar · Enter expediente. Fuentes protegidas.");
        _reviewDetailHost.SizeChanged+=(_,e)=>_reviewDetail.MaxWidth=Math.Max(280,Math.Min(440,e.NewSize.Width*.48));
        BuildReviewTable();PaintQueueCounters([]);UpdateReviewActions();return layout;
    }
    private static Button ReviewIcon(string name,LimsIconKind icon)
    {
        var button=new Button{Content=new LimsIcon{Icon=icon,Size=16},Style=(Style)Application.Current.Resources["LimsIconButtonStyle"],Width=36,MinWidth=36,Padding=new(6)};
        AutomationProperties.SetName(button,name);ToolTipService.SetToolTip(button,name);return button;
    }
    private readonly DispatcherTimer _searchDelay=new(){Interval=TimeSpan.FromMilliseconds(350)},_detailDelay=new(){Interval=TimeSpan.FromMilliseconds(160)};
    private bool _switchingQueue;
    private void PaintQueueCounters(IReadOnlyList<PlanningReviewCounter> counters)
    {
        _reviewCounters=counters;
        foreach(var (key,label) in Queues)
        {
            var c=counters.FirstOrDefault(c=>c.Category==key);var active=key==_reviewCategory;
            if(!_queueNavigation.TryGetValue(key,out var entry))
            {
                var caption=PlanningUi.Text(label,12);caption.MaxLines=2;caption.VerticalAlignment=VerticalAlignment.Center;caption.TextAlignment=TextAlignment.Left;
                var count=PlanningUi.Text("—",14);count.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;count.Margin=new(8,0,0,0);count.VerticalAlignment=VerticalAlignment.Center;count.TextAlignment=TextAlignment.Right;
                var pending=PlanningUi.Text("",11,true);pending.FontWeight=Microsoft.UI.Text.FontWeights.Normal;pending.TextAlignment=TextAlignment.Left;
                var cell=PlanningUi.Rows(GridLength.Auto,GridLength.Auto);cell.Width=186;cell.RowSpacing=3;cell.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});cell.ColumnDefinitions.Add(new(){Width=GridLength.Auto});cell.Children.Add(caption);Grid.SetColumn(count,1);cell.Children.Add(count);Grid.SetRow(pending,1);Grid.SetColumnSpan(pending,2);cell.Children.Add(pending);
                var button=new Button{Content=cell,Style=(Style)Application.Current.Resources["LimsStandardsSecondaryButtonStyle"],Padding=new(9,9,9,9),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,BorderThickness=new(3,0,0,0),CornerRadius=new(6)};
                Motion.SetIsEnabled(button,true);AutomationProperties.SetName(button,label);button.Click+=async(_,_)=>await SelectQueue(key);_queueButtons.Children.Add(button);
                entry=(button,caption,count,pending);_queueNavigation.Add(key,entry);
            }
            entry.Title.FontWeight=active?Microsoft.UI.Text.FontWeights.SemiBold:Microsoft.UI.Text.FontWeights.Normal;
            entry.Count.Text=c is null?"—":c.Total.ToString("N0",CultureInfo.InvariantCulture);entry.Count.Foreground=PlanningUi.Brush(active?"LimsAccentBrush":"LimsTextPrimaryBrush");
            entry.Pending.Text=c is null?"":c.Pending==1?"1 pendiente":$"{c.Pending:N0} pendientes";
            if(key=="Reviewed"&&c is not null)entry.Pending.Text=string.Join(" · ",new[]{(c.Held,"retenidos"),(c.Resolved,"resueltos"),(c.Excluded,"excluidos")}.Where(s=>s.Item1>0).Select(s=>$"{s.Item1:N0} {s.Item2}"));
            entry.Button.Background=PlanningUi.Brush(active?"LimsAccentSoftBrush":"LimsSurfaceBrush");entry.Button.BorderBrush=PlanningUi.Brush(active?"LimsAccentBrush":"LimsSurfaceBrush");
            AutomationProperties.SetItemStatus(entry.Button,active?"Bandeja seleccionada":"");AutomationProperties.SetHelpText(entry.Button,CounterDescription(c));ToolTipService.SetToolTip(entry.Button,CounterDescription(c));
        }
        _paintingQueue=true;
        try{var choices=Queues.Select(q=>new QueueChoice(q.Key,q.Label+(counters.FirstOrDefault(c=>c.Category==q.Key) is{} c?$" · {c.Total:N0}":""))).ToArray();_queuePicker.ItemsSource=choices;_queuePicker.SelectedItem=choices.First(q=>q.Key==_reviewCategory);}
        finally{_paintingQueue=false;}
        var current=counters.FirstOrDefault(c=>c.Category==_reviewCategory);
        var states=current is null?[]:new[]{(current.Pending,"pendiente","pendientes"),(current.Held,"retenido","retenidos"),(current.Resolved,"resuelto","resueltos"),(current.Excluded,"excluido","excluidos")}.Where(s=>s.Item1>0).Select(s=>$"{s.Item1:N0} {(s.Item1==1?s.Item2:s.Item3)}").ToArray();
        _queueSummary.Text=current is null?"":states.Length==0?"Sin casos en esta bandeja":string.Join(" · ",states);
        ToolTipService.SetToolTip(_queuePicker,CounterDescription(current));
        ToolTipService.SetToolTip(_queueSummary,"Contadores de casos para todo el alcance y los filtros. Cada JOB pertenece a una sola bandeja; puede conservar varios motivos dentro de ella.");
    }
    private string CounterDescription(PlanningReviewCounter? c)=>(ReviewScopeLabel+" · ")+(c is null?"Cargando…":$"Total {c.Total:N0} · Pendientes {c.Pending:N0} · Retenidos {c.Held:N0} · Resueltos {c.Resolved:N0}"+(c.Excluded>0?$" · Excluidos {c.Excluded:N0}":""));
    private string ReviewScopeLabel=>_reviewBatch is null?"Histórico":$"Lote {_reviewBatch.Value.ToString()[..8]}";
    private async Task SelectQueue(string key)
    {
        _switchingQueue=true;
        try
        {
            if(key=="Reviewed"&&_reviewCategory!="Reviewed"){_regularSituation=_situation.SelectedIndex;_situation.SelectedIndex=4;}
            else if(key!="Reviewed"&&_reviewCategory=="Reviewed")_situation.SelectedIndex=_regularSituation;
            _reviewCategory=key;_reviewPage=1;
        }
        finally{_switchingQueue=false;}
        PaintQueueCounters(_reviewCounters);CloseReviewDetail();await RefreshReviews();
    }
    private void BuildReviewTable()
    {
        var old=_reviewColumns.Length==0?[]:_reviewCells.Model.Columns.ToArray();
        var ordered=old.Where(c=>AllReviewColumns.Any(a=>a.Key==c.Key)).Select(c=>AllReviewColumns.Single(a=>a.Key==c.Key)).Concat(AllReviewColumns).DistinctBy(c=>c.Key).Where(c=>_visibleReviewColumns.Contains(c.Key)).ToArray();
        _reviewColumns=ordered;ConfigureReviewCells();
        foreach(var column in old){var index=_reviewCells.Model.Columns.FindIndex(c=>c.Key==column.Key);if(index>=0)_reviewCells.Model.Columns[index]=_reviewCells.Model.Columns[index] with{Width=column.Width};}
        _reviewCells.RebuildColumns();
    }
    private async Task RefreshReviews(CancellationToken ct=default)
    {
        _searchDelay.Stop();_reviewLoad?.Cancel();_reviewLoad?.Dispose();_reviewLoad=CancellationTokenSource.CreateLinkedTokenSource(ct);var token=_reviewLoad.Token;var query=ReviewQuery();
        var selected=SelectedReview?.Key;var range=_reviewCells.Model.Range;var scroll=_reviewCells.ScrollPosition;
        _reviewLoading=true;UpdateReviewActions();_reviewCells.SetInteractionEnabled(false);_reviewPrevious.IsEnabled=_reviewNext.IsEnabled=false;
        try
        {
            _progress.Visibility=Visibility.Visible;_progress.IsIndeterminate=true;_reviewEmpty.Text="Cargando bandeja…";_reviewEmpty.Visibility=_reviewRows.Count==0?Visibility.Visible:Visibility.Collapsed;
            var result=await _api.ReviewWorkspace(query,token);if(token.IsCancellationRequested)return;
            if(!result.IsSuccess||result.Value is not{} value){Message(result.Error??"No se pudo consultar la revisión.");_reviewEmpty.Text="No se pudo cargar la bandeja. Cambia los filtros para volver a consultar.";return;}
            _reviewRows=value.Items;LoadReviewCells();
            var index=selected is null?-1:_reviewRows.ToList().FindIndex(r=>r.Key==selected);
            if(index>=0){_reviewCells.Model.Select(index,Math.Min(range.Left,_reviewCells.Model.Columns.Count-1));_reviewCells.Model.Select(index,Math.Min(range.Right,_reviewCells.Model.Columns.Count-1),true);_reviewCells.Paint();_reviewCells.RestoreScroll(scroll);}
            _reviewCount.Text=$"{(value.Items.Count==0?0:(value.Page-1)*value.PageSize+1):N0}–{(value.Items.Count==0?0:(value.Page-1)*value.PageSize+value.Items.Count):N0} de {value.Total:N0} casos filtrados";
            _reviewPrevious.IsEnabled=value.Page>1;_reviewNext.IsEnabled=value.Page*value.PageSize<value.Total;PaintQueueCounters(value.Counters);
            _activeFilters.Text=value.ScopeNotice;ToolTipService.SetToolTip(_reviewCount,value.ScopeNotice);
            _reviewEmpty.Text="No hay casos que coincidan con esta bandeja y sus filtros.";_reviewEmpty.Visibility=value.Items.Count==0?Visibility.Visible:Visibility.Collapsed;
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){ }
        finally
        {
            if(!token.IsCancellationRequested){_reviewLoading=false;_reviewCells.SetInteractionEnabled(true);UpdateReviewActions();if(_detailOpen)await ShowReviewDetail();if(!_busy){_progress.IsIndeterminate=false;_progress.Visibility=Visibility.Collapsed;}}
        }
    }
    private void ReviewFilter(ReviewColumn column,FrameworkElement anchor)
    {
        var flyout=new Flyout();var panel=PlanningUi.Stack(PlanningUi.Text(column.Label,14));
        var text=PlanningUi.Field("Contiene",_columnFilters.GetValueOrDefault(column.Key));
        var from=new CalendarDatePicker{Header="Desde",DateFormat="{year.full}-{month.integer(2)}-{day.integer(2)}"};var until=new CalendarDatePicker{Header="Hasta",DateFormat="{year.full}-{month.integer(2)}-{day.integer(2)}"};
        if(column.Key is "InDate" or "DueDate")
        {
            foreach(var (picker,key) in new[]{(from,column.Key+"From"),(until,column.Key+"Until")})if(DateOnly.TryParseExact(_columnFilters.GetValueOrDefault(key),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day))picker.Date=new(day.ToDateTime(TimeOnly.MinValue),TimeSpan.FromHours(-6));
            panel.Children.Add(PlanningUi.Row(from,until));
        }
        else{AutomationProperties.SetName(text,"Valor del filtro de revisión "+column.Label);panel.Children.Add(text);}
        panel.Children.Add(PlanningUi.Row(Action("Aplicar columna",LimsIconKind.ApplyFilters,async()=>{
            if(column.Key is "InDate" or "DueDate")
            {
                if(from.Date>until.Date){Message("El inicio no puede ser posterior al fin.");return;}
                foreach(var (picker,key) in new[]{(from,column.Key+"From"),(until,column.Key+"Until")}){_columnFilters.Remove(key);if(picker.Date is{} date)_columnFilters[key]=date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);}
            }
            else{_columnFilters.Remove(column.Key);if(!string.IsNullOrWhiteSpace(text.Text))_columnFilters[column.Key]=text.Text.Trim();}
            flyout.Hide();_reviewPage=1;BuildReviewTable();await RefreshReviews();
        },true),Action("Limpiar columna",LimsIconKind.ClearFilters,async()=>{_columnFilters.Remove(column.Key);_columnFilters.Remove(column.Key+"From");_columnFilters.Remove(column.Key+"Until");flyout.Hide();_reviewPage=1;BuildReviewTable();await RefreshReviews();})));
        flyout.Content=panel;flyout.ShowAt(anchor);
    }
    private void UpdateReviewActions()
    {
        var selected=SelectedReviews();var row=selected.Length==1?selected[0]:null;
        var edit=_permissions().Contains(PlanningPermissions.EditWip,StringComparer.Ordinal);
        var manual=_permissions().Contains(PlanningPermissions.ManualTests,StringComparer.Ordinal);
        var open=selected.Length>0&&selected.All(r=>r.State is "Pending" or "Held");
        var disabled=new List<string>();
        void Available(Button button,bool show,bool allowed,string explanation)
        {button.Visibility=show?Visibility.Visible:Visibility.Collapsed;button.IsEnabled=allowed;ToolTipService.SetToolTip(button,explanation);AutomationProperties.SetHelpText(button,explanation);if(show&&!allowed)disabled.Add(explanation);}
        Available(_openReview,row is not null,row is not null,row?.EpisodeId is null?"Consultar el lote y sus filas originales":"Abrir el expediente completo en su ventana");
        Available(_addTests,row?.EpisodeId is not null&&row.State is "Pending" or "Held",manual,"Requiere permiso para agregar pruebas manuales");
        Available(_completeData,row?.Category is "MissingParty" or "IncompleteData"&&row.EpisodeId is not null&&row.State is "Pending" or "Held",edit,"Requiere permiso de edición WIP; conserva la procedencia original");
        Available(_hold,open,edit,"Retener con motivo, usuario y fecha; requiere permiso de edición WIP");
        var canResolve=row is{Category:"SummaryWithoutReport",State:"Pending" or "Held",IncidentId:not null};
        var verified=_detailDossier?.EpisodeId==row?.EpisodeId&&_detailDossier is not null&&_detailDossier.Tests.Any(t=>t.Origin=="Manual"&&!string.IsNullOrWhiteSpace(t.Equivalence)&&!string.IsNullOrWhiteSpace(t.Laboratory));
        Available(_resolve,canResolve,edit&&verified,verified?"Resolver la incidencia operativa conservando el estado TIPS":"Primero agrega y verifica pruebas manuales con equivalencia y laboratorio en el detalle");
        Available(_reopen,selected.Length>0&&selected.All(r=>r.State is "Held" or "Excluded"||r is{State:"Resolved",IncidentId:not null,Category:"SummaryWithoutReport"}),edit,"Requiere permiso de edición WIP; la decisión anterior queda en historial");
        Available(_exclude,open,edit,"Excluir requiere motivo y confirmación; conserva datos e historial");
        Available(_identity,row is{Category:"ExecutionIdentity",State:"Pending"},manual,"Vincular una fila original a una ejecución verificada; requiere permiso de pruebas manuales");
        Available(_equivalence,row is{Category:"MissingEquivalence",State:"Pending" or "Held"},_permissions().Contains(PlanningPermissions.Equivalences,StringComparer.Ordinal),"Requiere permiso de equivalencias; permite clasificar o decidir NO PROGRAMAR");
        _reviewActionHint.Text=string.Join('\n',disabled.Distinct());_reviewActionHint.Visibility=disabled.Count>0?Visibility.Visible:Visibility.Collapsed;
        _reviewActions.IsEnabled=selected.Length>0&&!_reviewLoading;_detailButton.IsEnabled=row is not null&&!_reviewLoading;
        var range=_reviewCells.Model.Range;
        var cells=selected.Length*(range.Right-range.Left+1);
        _reviewSelection.Text=selected.Length==0?"Sin selección":$"{selected.Length:N0} {(selected.Length==1?"fila":"filas")} · {cells:N0} {(cells==1?"celda seleccionada":"celdas seleccionadas")}";
    }
    private int _regularSituation=1;
    private Task OpenSelectedReview()=>SelectedReview is{} row?OpenReview(row):Task.CompletedTask;
    private async Task OpenReview(PlanningReviewCase row)
    {
        if(row.EpisodeId is{} id){await _open(id);return;}
        if(row.BatchId is{} batch){var result=await _api.Batch(batch,CancellationToken.None);if(result.IsSuccess&&result.Value is{} preview){Show(preview,true);_flow.IsExpanded=true;_tabs.SelectedIndex=0;}else Message(result.Error??"Lote no disponible.");}
    }
    private Task CopyReviews()
    {
        if(_reviewLoading||_reviewRows.Count==0)return Task.CompletedTask;
        var cells=new DataPackage();cells.SetText(_reviewCells.Model.Copy());Clipboard.SetContent(cells);Clipboard.Flush();return Task.CompletedTask;
    }
    private async Task AddTests()
    {
        if(SelectedReview is not PlanningReviewCase{EpisodeId:{} id}||!_permissions().Contains(PlanningPermissions.ManualTests,StringComparer.Ordinal))return;
        var window=new PlanningManualTestWindow(_api,id,Changed);_manualWindows.Add(window);PlanningUi.Track(window);window.Closed+=(_,_)=>_manualWindows.Remove(window);window.Activate();await Task.CompletedTask;
    }
    private async Task DecideReview(string state)
    {
        var selected=SelectedReviews();if(selected.Length==0||!_permissions().Contains(PlanningPermissions.EditWip,StringComparer.Ordinal))return;
        var reason=PlanningUi.Field("Motivo de la decisión",max:1000);AutomationProperties.SetName(reason,"Motivo de la decisión");
        var detail=state=="Resolved"?"Confirma que completaste y verificaste las pruebas manuales. Se resuelve este caso operativo; el estado oficial de TIPS se conserva.":state=="Held"?"El caso seguirá en seguimiento con tu usuario, fecha y motivo.":state=="Excluded"?"Se excluirán los casos seleccionados del seguimiento pendiente. Los datos y la justificación permanecen en el historial.":"El caso volverá a la bandeja de pendientes. La decisión anterior queda en el historial.";
        var dialog=new ContentDialog{XamlRoot=XamlRoot,Title=(state=="Resolved"?"Resolver":state=="Held"?"Retener":state=="Excluded"?"Excluir justificadamente":"Reabrir")+" · "+selected.Length+" caso(s)",Content=PlanningUi.Stack(PlanningUi.Text(detail,13,true),reason),PrimaryButtonText="Guardar decisión",CloseButtonText="Cancelar",DefaultButton=ContentDialogButton.Close};
        dialog.IsPrimaryButtonEnabled=false;reason.TextChanged+=(_,_)=>dialog.IsPrimaryButtonEnabled=!string.IsNullOrWhiteSpace(reason.Text);
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        var saved=0;string? error=null;
        foreach(var row in selected){var result=await _api.ReviewDecision(new(row.Key,row.Category,row.Version,state,reason.Text),CancellationToken.None);if(!result.IsSuccess){error=result.Error;break;}saved++;}
        await Changed();Message($"{saved} decisiones guardadas con usuario, fecha y motivo."+(error is null?"":" Se detuvo: "+error));
    }
    private async Task ClassifyReview()
    {
        if(SelectedReview is not PlanningReviewCase{OriginalTest:{} original}||!_permissions().Contains(PlanningPermissions.Equivalences,StringComparer.Ordinal))return;
        if(await PlanningEquivalenceEditor.Show(_api,XamlRoot,original)){await Changed();Message("Clasificación persistida y auditada.");}
    }
    private async Task ResolveIdentity()
    {
        if(SelectedReview is not PlanningReviewCase{BatchId:{} batch,SourceRow:>0,State:"Pending"} row||!_permissions().Contains(PlanningPermissions.ManualTests,StringComparer.Ordinal))return;
        IReadOnlyList<PlanningRow> candidates;
        if(row.EpisodeId is{} knownEpisode)
        {
            // REV/suffix JOBs are intentionally absent from the normal consultation search.
            var known=await _api.Dossier(knownEpisode,CancellationToken.None);
            if(!known.IsSuccess||known.Value is not{} knownDossier){Message(known.Error??"No se pudo consultar el expediente.");return;}
            candidates=[new(knownDossier.EpisodeId,null,knownDossier.Job,knownDossier.Wip.InDate,knownDossier.Wip.DueDate,null,null,null,knownDossier.Wip.Service,null,null,"Sin información",null,null,"Summary",knownDossier.Wip.Buyer,knownDossier.Wip.Vendor,false)];
        }
        else
        {
            var jobs=await _api.Jobs(new(Search:row.Job,SearchMode:"Exact",Scope:"History",PageSize:500),CancellationToken.None);
            if(!jobs.IsSuccess){Message(jobs.Error??"No se pudieron consultar los episodios.");return;}
            candidates=jobs.Value?.Items??[];
        }
        var episodes=candidates.DistinctBy(r=>r.EpisodeId).ToArray();if(episodes.Length==0){Message("No hay episodio inequívoco. Importa el Summary correspondiente antes de conciliar.");return;}
        var combo=new ComboBox{Header="Episodio",ItemsSource=episodes.Select(e=>$"{e.Job} · {e.InDate:yyyy-MM-dd} · {e.EpisodeId}").ToArray(),SelectedIndex=0};
        var execution=PlanningUi.Field("UUID de ejecución existente (vacío = nueva)",max:36);var reason=PlanningUi.Field("Motivo y verificación de identidad",max:1000);
        var dialog=new ContentDialog{XamlRoot=XamlRoot,Title="Conciliar fila original",Content=PlanningUi.Stack(PlanningUi.Text(row.OriginalTest??"Prueba no informada"),combo,execution,reason),PrimaryButtonText="Guardar conciliación",CloseButtonText="Cancelar"};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        Guid? id=null;if(!string.IsNullOrWhiteSpace(execution.Text)){if(!Guid.TryParse(execution.Text,out var value)){Message("UUID de ejecución inválido.");return;}id=value;}
        var dossier=await _api.Dossier(episodes[combo.SelectedIndex].EpisodeId,CancellationToken.None);if(dossier.Value is null){Message(dossier.Error??"Expediente no disponible.");return;}
        var result=await _api.Resolve(new(batch,row.SourceRow,dossier.Value.EpisodeId,id,id is null,dossier.Value.Version,reason.Text),CancellationToken.None);if(result.IsSuccess){await Changed();Message("Identidad conciliada sin cambiar las ejecuciones anteriores.");}else Message(result.Error??"No se pudo conciliar.");
    }
}
