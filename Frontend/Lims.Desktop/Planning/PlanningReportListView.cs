using System.Collections.ObjectModel;
using System.Globalization;
using Lims.Contracts.Planning;
using Lims.DesignSystem.Controls;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Lims.Desktop.Planning;

public sealed partial class PlanningReportListView : UserControl,IDisposable
{
    private readonly PlanningApiClient _api;
    private readonly Func<IReadOnlyList<string>> _permissions;
    private readonly Func<Guid,Task> _dossier;
    private readonly Func<bool> _administrator;
    private readonly Func<int?> _userId;
    private readonly Dictionary<string,string> _columnFilters=[];
    private readonly TextBox _search=PlanningUi.Field("",max:20000);
    private readonly TextBox _test=PlanningUi.Field("Prueba de Planificación"),_lab=PlanningUi.Field("Laboratorio"),_service=PlanningUi.Field("Servicio"),_buyer=PlanningUi.Field("Cliente / Buyer");
    private readonly TextBox _clockDate=PlanningUi.Field("",DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),10);
    private readonly TextBox _clockTime=PlanningUi.Field("",DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).ToString("HH:mm",CultureInfo.InvariantCulture),5);
    private readonly ComboBox _view=new(){ItemsSource=new[]{"Pendientes","Digitadas","Todas"},SelectedIndex=0,MinWidth=135,Style=(Style)Application.Current.Resources["LimsCompactComboBoxStyle"]};
    private readonly ComboBox _scheduleState=new(){ItemsSource=new[]{"Todos los horarios","Pendientes","Chequeados","Con excepciones"},SelectedIndex=0,MinWidth=170,Style=(Style)Application.Current.Resources["LimsCompactComboBoxStyle"]};
    private readonly LimsTabStrip _tabs=new();
    private readonly ReportListGrid _grid=new();
    private readonly TextBlock _status=PlanningUi.Text("",12,true),_result=PlanningUi.Text("",12),_connection=PlanningUi.Text("Conectando…",12,true),_selection=PlanningUi.Text("",12,true);
    private readonly ProgressRing _progress=new(){Width=18,Height=18,IsActive=false};
    private readonly Button _mark,_review,_next,_fixedBatch;
    private readonly StackPanel _intro;
    private readonly Border _context;
    private readonly Grid _digitization,_schedules,_closures;
    private readonly Border _detail;
    private readonly TextBlock _detailTitle=PlanningUi.Text("",17),_detailMeta=PlanningUi.Text("",12,true),_history=PlanningUi.Text("",12,true);
    private readonly ReportNativeTable<ReportSchedule> _scheduleTable;
    private readonly ReportNativeTable<ReportScheduleMember> _members;
    private readonly ObservableCollection<ReportSchedule> _scheduleRows=[];
    private readonly ObservableCollection<ReportClosure> _closureRows=[];
    private readonly ReportOperationalClock _fixedClock=new();
    private readonly TextBlock _clockLabel=PlanningUi.Text("Hora actual",12,true);
    private readonly DispatcherTimer _clockTick=new(){Interval=TimeSpan.FromSeconds(1)};
    private readonly DispatcherTimer _sync=new(){Interval=TimeSpan.FromSeconds(15)};
    private readonly DispatcherTimer _searchDelay=new(){Interval=TimeSpan.FromMilliseconds(280)};
    private readonly TextBlock _pageLabel=PlanningUi.Text("Página 1 de 1",12,true);
    private readonly TextBox _pageNumber=PlanningUi.Field("", "1",7);
    private readonly Button _previousPage,_nextPage;
    private readonly StackPanel _pager;
    private int _listPage=1;
    private CancellationTokenSource _request=new(),_detailRequest=new();
    private ReportListRows? _rows;
    private ReportScheduleDetail? _opened;
    private ReportDigitization? _retry;
    private string _sort="Default",_verified="";
    private long? _block,_listRevision;private DateOnly? _date;
    private int _page=1,_total;
    private bool _descending,_mutating,_trackingAvailable,_onlyTable,_loading;
    private bool _pageNeedsReload,_disposed;
    public event Func<Task>? Changed;
    public PlanningReportListView(PlanningApiClient api,Func<Guid,Task> dossier,Func<IReadOnlyList<string>> permissions,Func<bool> administrator,Func<int?>? userId=null,bool scannerEnabled=true,IReportScanner? scanner=null)
    {
        _api=api;_permissions=permissions;_dossier=dossier;_administrator=administrator;_userId=userId??(()=>null);_serial=scanner??new ReportScanner();_grid.PreferenceError+=message=>Feedback(message);
        _search.PlaceholderText="Buscar por JOB, prueba o cliente";_search.MinWidth=180;_search.MinHeight=36;
        AutomationProperties.SetName(_search,"Buscar JOB en Report List");AutomationProperties.SetName(_view,"Vista");AutomationProperties.SetName(_scheduleState,"Estado de horarios");
        _search.Header=null;_clockDate.Header=null;_clockTime.Header=null;
        _clockDate.MinWidth=115;_clockDate.Width=135;_clockDate.PlaceholderText="yyyy-MM-dd";_clockTime.MinWidth=100;_clockTime.Width=105;_clockTime.PlaceholderText="HH:mm";
        AutomationProperties.SetName(_clockDate,"Fecha de digitación");AutomationProperties.SetName(_clockTime,"Hora de digitación");
        ToolTipService.SetToolTip(_clockDate,"Fecha de prueba · Guatemala");ToolTipService.SetToolTip(_clockTime,"Hora de digitación · Guatemala");
        _search.KeyDown+=async(_,e)=>{if(e.Key==VirtualKey.Enter){e.Handled=true;await Load();}};
        _search.TextChanged+=(_,_)=>{_searchDelay.Stop();if(!IsLoaded||_clearing)return;if(!_mutating){_request.Cancel();_grid.SetBusy(true);}_searchDelay.Start();};
        _searchDelay.Tick+=async(_,_)=>{_searchDelay.Stop();if(_mutating){_searchDelay.Start();return;}if(!FlushKeyboard())await Load();};
        _view.SelectionChanged+=async(_,_)=>{if(IsLoaded&&_tabs.SelectedIndex==0)await Load();};
        _scheduleState.SelectionChanged+=async(_,_)=>{if(IsLoaded&&_tabs.SelectedIndex==1)await Load();};
        _grid.CanKeep=()=>!_mutating&&Has(PlanningPermissions.ReportReview);_grid.KeepRequested+=Keep;
        _grid.CanCorrect=()=>!_mutating&&Has(PlanningPermissions.ReportReview);_grid.IsAdministrator=_administrator;_grid.CheckRequested+=CheckDirect;
        _grid.CanMark=()=>!_mutating&&Has(PlanningPermissions.ReportMark);_grid.CorrectionRequested+=Correct;
        _grid.OpenRequested+=item=>_dossier(item.Execution.EpisodeId);_grid.SortRequested+=async key=>{_descending=_sort==key&&!_descending;_sort=key;_grid.SetSort(key,_descending);await Load();};
        _grid.FilterRequested+=ColumnFilter;_grid.SelectionChanged+=UpdateSelection;
        var pageTitle=PlanningUi.Text("Report List",20);pageTitle.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;pageTitle.VerticalAlignment=VerticalAlignment.Center;
        var identity=PlanningUi.Row(new Border{Child=new LimsIcon{Icon=LimsIconKind.Reports,Size=20,Foreground=PlanningUi.Brush("LimsAccentBrush")},Width=34,Height=34,Background=PlanningUi.Brush("LimsSurfaceSecondaryBrush"),CornerRadius=new(6)},pageTitle);identity.Spacing=10;identity.VerticalAlignment=VerticalAlignment.Center;
        _intro=PlanningUi.Stack(identity);_intro.Spacing=0;_intro.Margin=new(0,0,28,0);_intro.VerticalAlignment=VerticalAlignment.Center;
        foreach(var title in new[]{"Digitación","Chequeo de horarios","Cierres"})_tabs.Items.Add(new LimsTabItem{Label=title});
        AutomationProperties.SetName(_tabs,"Vistas de Report List");
        var header=new Grid{ColumnSpacing=0,Padding=new(12,4,12,0)};header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});header.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});header.RowDefinitions.Add(new(){Height=GridLength.Auto});header.RowDefinitions.Add(new(){Height=GridLength.Auto});header.Children.Add(_intro);Grid.SetColumn(_tabs,1);header.Children.Add(_tabs);_tabs.VerticalAlignment=VerticalAlignment.Center;
        header.SizeChanged+=(_,_)=>{var narrow=header.ActualWidth<760;Grid.SetRow(_tabs,narrow?1:0);Grid.SetColumn(_tabs,narrow?0:1);Grid.SetColumnSpan(_tabs,narrow?2:1);_intro.Margin=narrow?new(0,4,0,2):new(0,0,28,0);};
        var find=Compact("Buscar",Load);AutomationProperties.SetName(find,"Buscar Report List");
        var refresh=IconAction(LimsIconKind.Refresh,"Actualizar Report List",Load);
        var multiple=Compact("Varios JOBs",Multiple);AutomationProperties.SetName(multiple,"Búsqueda múltiple de Report List");ToolTipService.SetToolTip(multiple,"Buscar una lista de JOBs");
        var filters=IconAction(LimsIconKind.Filter,"Filtros de Report List",()=>Task.CompletedTask);
        var filterPanel=PlanningUi.Stack(_test,_lab,_service,_buyer);filterPanel.Width=290;
        var filterFlyout=new Flyout{Content=filterPanel};filters.Flyout=filterFlyout;
        filterPanel.Children.Add(Compact("Aplicar filtros",async()=>{filterFlyout.Hide();await Load();}));
        filterPanel.Children.Add(Compact("Limpiar filtros",async()=>{_test.Text=_lab.Text=_service.Text=_buyer.Text=_verified="";_block=null;_date=null;_columnFilters.Clear();filterFlyout.Hide();await Load();}));
        var configuration=IconAction(LimsIconKind.Administration,"Configurar tabla de Report List",()=>Task.CompletedTask);var menu=_grid.ColumnsMenu();configuration.Flyout=menu;
        var only=new ToggleMenuFlyoutItem{Text="Ocultar encabezado"};only.Click+=(_,_)=>{_onlyTable=only.IsChecked;_intro.Visibility=_onlyTable?Visibility.Collapsed:Visibility.Visible;};menu.Items.Add(only);
        var tools=PlanningUi.Row(_view,_scheduleState,filters,refresh,configuration);tools.Spacing=6;tools.VerticalAlignment=VerticalAlignment.Center;
        var searchEntry=new Grid();_search.Padding=new(32,5,8,5);searchEntry.Children.Add(_search);searchEntry.Children.Add(new LimsIcon{Icon=LimsIconKind.Search,Size=15,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,Margin=new(10,0,0,0),Foreground=PlanningUi.Brush("LimsTextSecondaryBrush"),IsHitTestVisible=false});
        var searchGroup=new Grid{ColumnSpacing=6,MaxWidth=800,HorizontalAlignment=HorizontalAlignment.Stretch};foreach(var width in new[]{new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})searchGroup.ColumnDefinitions.Add(new(){Width=width});searchGroup.Children.Add(searchEntry);Grid.SetColumn(find,1);searchGroup.Children.Add(find);Grid.SetColumn(multiple,2);searchGroup.Children.Add(multiple);
        var searchBar=new Grid{ColumnSpacing=16,Padding=new(12,8,12,10)};searchBar.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});searchBar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});searchBar.RowDefinitions.Add(new(){Height=GridLength.Auto});searchBar.RowDefinitions.Add(new(){Height=GridLength.Auto});searchBar.Children.Add(searchGroup);Grid.SetColumn(tools,1);searchBar.Children.Add(tools);searchGroup.HorizontalAlignment=HorizontalAlignment.Left;
        void ArrangeSearch(){var narrow=searchBar.ActualWidth<760;Grid.SetRow(tools,narrow?1:0);Grid.SetColumn(tools,narrow?0:1);Grid.SetColumnSpan(searchGroup,narrow?2:1);Grid.SetColumnSpan(tools,narrow?2:1);tools.Margin=narrow?new(0,8,0,0):new(0);searchGroup.Width=Math.Min(800,Math.Max(0,searchBar.ActualWidth-(narrow?24:tools.ActualWidth+40)));searchBar.ColumnSpacing=narrow?0:16;}
        searchBar.SizeChanged+=(_,_)=>ArrangeSearch();tools.SizeChanged+=(_,_)=>ArrangeSearch();
        _mark=Compact("Guardar digitación",SaveDigitization,true,()=>!_mutating&&_grid.Selected.Count is >0 and <=500&&_grid.Selected.All(ReportListTableModel.CanMark));
        var clear=IconAction(LimsIconKind.Cancel,"Desmarcar selección temporal",()=>{_grid.ClearSelection();return Task.CompletedTask;});
        var selectionRow=PlanningUi.Row(_selection,_mark);selectionRow.Spacing=8;selectionRow.VerticalAlignment=VerticalAlignment.Center;
        _context=new Border{Child=PlanningUi.Split(selectionRow,clear),Padding=new(10,8,10,8),Background=PlanningUi.Brush("LimsSurfaceSecondaryBrush"),BorderBrush=PlanningUi.Brush("LimsAccentBrush"),BorderThickness=new(1),CornerRadius=new(8),Visibility=Visibility.Collapsed};_result.Visibility=Visibility.Collapsed;
        var table=new Border{Child=_grid,Background=PlanningUi.Brush("LimsSurfaceBrush"),BorderBrush=PlanningUi.Brush("LimsBorderBrush"),BorderThickness=new(1),CornerRadius=new(6)};
        _digitization=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star));_digitization.RowSpacing=8;
        PlanningUi.Place(_digitization,_context,0);PlanningUi.Place(_digitization,table,1);
        _scheduleTable=new("Horarios de digitación",[("Fecha",115),("Horario",90),("Pruebas",75),("JOBs",70),("Digitado por",190),("Chequeo",165)],s=>[s.DateText,s.TimeText,s.Tests.ToString("N0",CultureInfo.CurrentCulture),s.Jobs.ToString("N0",CultureInfo.CurrentCulture),s.DigitizedBy,s.State]);
        _scheduleTable.SetItems(_scheduleRows);_scheduleTable.SelectedChanged+=async s=>await OpenSchedule(s.Id);
        _members=new("Pruebas del horario",[("JOB",145),("Prueba",190),("Laboratorio",105),("Fecha / hora",165),("Digitado por",150),("Ejecución",285),("Chequeo",165),("Original",250),("Tipo",130)],m=>[m.Item.Execution.Job,m.Item.Execution.PlanningTest,m.Item.Execution.Laboratory??"",m.Item.CheckedAtText,m.Item.Execution.DigitizedBy??"",m.Item.Execution.ExecutionId?.ToString()??"",m.Changed?"Cambió: revisar":_opened?.Schedule.State??"",m.Item.Execution.OriginalTest??"",m.Item.Execution.ExecutionType??""]);
        _review=Compact("Chequear horario",ReviewSchedule,true,()=>!_mutating&&_opened is{} d&&d.Schedule.Exceptions==0&&d.Schedule.ReviewedAt is null);
        var exceptions=Compact("Revisar excepciones",()=>{if(_opened is{} d){_members.SetItems(d.Members.Where(m=>m.Changed).ToArray());_detailMeta.Text=$"{d.Schedule.Exceptions:N0} pruebas cambiaron después de la digitación. Corrija y registre una nueva digitación antes de chequear.";}return Task.CompletedTask;});
        var history=Compact("Consultar historial",()=>{_history.Visibility=_history.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;return Task.CompletedTask;});
        var allMembers=Compact("Todas las pruebas",()=>{_members.SetItems(_opened?.Members);return Task.CompletedTask;});
        var close=Compact("Cerrar detalle",CloseSchedule);
        _history.Visibility=Visibility.Collapsed;
        var detailLayout=PlanningUi.Rows(GridLength.Auto,GridLength.Auto,new(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto);detailLayout.RowSpacing=8;
        PlanningUi.Place(detailLayout,PlanningUi.Split(_detailTitle,close),0);PlanningUi.Place(detailLayout,_detailMeta,1);PlanningUi.Place(detailLayout,_members,2);
        PlanningUi.Place(detailLayout,PlanningUi.Row(_review,exceptions,allMembers,history),3);PlanningUi.Place(detailLayout,new ScrollViewer{Content=_history,MaxHeight=100},4);
        _detail=new Border{Child=detailLayout,Padding=new(12),Background=PlanningUi.Brush("LimsSurfaceBrush"),BorderBrush=PlanningUi.Brush("LimsBorderBrush"),BorderThickness=new(1),CornerRadius=new(6),Visibility=Visibility.Collapsed,Height=330};
        _schedules=PlanningUi.Rows(new(1,GridUnitType.Star),GridLength.Auto);_schedules.RowSpacing=8;PlanningUi.Place(_schedules,_scheduleTable,0);PlanningUi.Place(_schedules,_detail,1);
        var closureTable=new ReportNativeTable<ReportClosure>("JOBs completamente digitados",[("JOB",190),("Pruebas requeridas",150),("Digitadas",110),("Última digitación",200),("Due Date",130)],c=>[c.Job,c.Required.ToString("N0",CultureInfo.CurrentCulture),c.Digitized.ToString("N0",CultureInfo.CurrentCulture),c.LastDigitizedAt.ToOffset(TimeSpan.FromHours(-6)).ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture),c.DueDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??""]);
        closureTable.SetItems(_closureRows);
        _closures=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star));PlanningUi.Place(_closures,PlanningUi.Text("JOBs completos según las ejecuciones registradas. Cobertura TIPS no certificada; no constituye cierre oficial.",12,true),0);PlanningUi.Place(_closures,closureTable,1);
        var groupTools=PlanningUi.Row(Compact("Exportar chequeados",ExportSchedules));groupTools.Visibility=Visibility.Collapsed;
        _next=Compact("Cargar más",async()=>await LoadGroups(false));_next.Visibility=Visibility.Collapsed;
        var body=new Grid();body.Children.Add(_digitization);body.Children.Add(_schedules);body.Children.Add(_closures);
        var headerSurface=PlanningUi.Rows(GridLength.Auto,GridLength.Auto,GridLength.Auto);headerSurface.RowSpacing=0;PlanningUi.Place(headerSurface,header,0);PlanningUi.Place(headerSurface,searchBar,1);
        var searchSurface=new Border{Child=headerSurface,Background=PlanningUi.Brush("LimsSurfaceBrush"),BorderBrush=PlanningUi.Brush("LimsBorderBrush"),BorderThickness=new(1),CornerRadius=new(8)};
        var clockSection=BuildClockSection();
        var scannerSection=BuildScannerSection(scannerEnabled);
        var batch=_fixedBatch=Compact("Digitar con hora fija",OpenFixedTimeBatch);AutomationProperties.SetName(batch,"Digitar lote con hora fija");
        batch.Visibility=Has(PlanningPermissions.ReportMark)?Visibility.Visible:Visibility.Collapsed;
        var clockTools=PlanningUi.Row(clockSection,batch);clockTools.Spacing=8;
        var operation=new Grid{ColumnSpacing=18,Padding=new(12,7,12,8)};
        operation.ColumnDefinitions.Add(new(){Width=GridLength.Auto});operation.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});operation.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        operation.Children.Add(clockTools);Grid.SetColumn(_scanReceipt,1);operation.Children.Add(_scanReceipt);Grid.SetColumn(scannerSection,2);operation.Children.Add(scannerSection);
        operation.SizeChanged+=(_,_)=>_scanReceipt.Visibility=operation.ActualWidth<850?Visibility.Collapsed:Visibility.Visible;
        PlanningUi.Place(headerSurface,new Border{Child=operation,BorderBrush=PlanningUi.Brush("LimsBorderBrush"),BorderThickness=new(0,1,0,0)},2);
        var top=PlanningUi.Stack(searchSurface,groupTools,_result);top.Spacing=6;
        _previousPage=Compact("Anterior",async()=>await LoadPage(_listPage-1),enabledAfter:()=>!_loading&&!_mutating&&_listPage>1);
        _nextPage=Compact("Siguiente",async()=>await LoadPage(_listPage+1),enabledAfter:()=>!_loading&&!_mutating&&_listPage<PageCount);
        _pageNumber.Header=null;_pageNumber.MinWidth=42;_pageNumber.Width=48;_pageNumber.MinHeight=30;_pageNumber.Padding=new(6,3,6,3);AutomationProperties.SetName(_pageNumber,"Ir a página de Report List");
        _pageNumber.KeyDown+=async(_,e)=>{if(e.Key==VirtualKey.Enter){e.Handled=true;if(int.TryParse(_pageNumber.Text,out var p))await LoadPage(p);}};
        _pager=PlanningUi.Row(_previousPage,_pageLabel,_pageNumber,_nextPage);_pager.Spacing=8;_pageLabel.VerticalAlignment=VerticalAlignment.Center;
        foreach(var b in new[]{_previousPage,_nextPage}){b.MinHeight=30;b.Padding=new(9,3,9,3);}
        var layout=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star),GridLength.Auto);layout.RowSpacing=8;
        PlanningUi.Place(layout,top,0);PlanningUi.Place(layout,body,1);PlanningUi.Place(layout,PlanningUi.Split(PlanningUi.Row(_progress,_status),PlanningUi.Row(_pager,_connection,_next)),2);Content=layout;
        void SetView(){var index=_tabs.SelectedIndex;batch.Visibility=index==0&&Has(PlanningPermissions.ReportMark)?Visibility.Visible:Visibility.Collapsed;_digitization.Visibility=index==0?Visibility.Visible:Visibility.Collapsed;_schedules.Visibility=index==1?Visibility.Visible:Visibility.Collapsed;_closures.Visibility=index==2?Visibility.Visible:Visibility.Collapsed;_view.Visibility=filters.Visibility=multiple.Visibility=configuration.Visibility=index==0?Visibility.Visible:Visibility.Collapsed;_scheduleState.Visibility=groupTools.Visibility=index==1?Visibility.Visible:Visibility.Collapsed;_next.Visibility=index==0?Visibility.Collapsed:Visibility.Visible;}
        _tabs.SelectionChanged+=async(_,_)=>{SetView();_pager.Visibility=_tabs.SelectedIndex==0?Visibility.Visible:Visibility.Collapsed;Feedback(null);Motion.Enter(body,y:3,milliseconds:150);await Load();};SetView();
        _sync.Tick+=async(_,_)=>await Synchronize();Loaded+=(_,_)=>{if(!_disposed){_sync.Start();_clockTick.Start();}};Unloaded+=(_,_)=>{_sync.Stop();_clockTick.Stop();if(!_disposed){_request.Cancel();_detailRequest.Cancel();}};
        Motion.ConfigureVisibility(_context);Motion.ConfigureVisibility(_result);Motion.ConfigureVisibility(_detail);
    }
    private static Button Compact(string title,Func<Task> action,bool primary=false,Func<bool>? enabledAfter=null)
    {var b=PlanningUi.Button(title,action,primary,enabledAfter);b.FontSize=12;b.MinHeight=34;b.Padding=new(10,5,10,5);return b;}
    private static Button IconAction(LimsIconKind icon,string label,Func<Task> action)
    {var button=Compact(label,action);button.Style=(Style)Application.Current.Resources["LimsIconButtonStyle"];button.Content=new LimsIcon{Icon=icon,Size=16};button.Width=36;button.MinWidth=36;button.Padding=new(6);AutomationProperties.SetName(button,label);ToolTipService.SetToolTip(button,label);return button;}
    private bool Has(string permission)=>_permissions().Contains(permission,StringComparer.Ordinal);
    private Task CloseSchedule(){_detailRequest.Cancel();_detail.Visibility=Visibility.Collapsed;_opened=null;return Task.CompletedTask;}
    private void Feedback(string? message){_result.Text=message??"";_result.Visibility=string.IsNullOrEmpty(message)?Visibility.Collapsed:Visibility.Visible;if(!string.IsNullOrEmpty(message))Motion.Enter(_result,y:2,milliseconds:140);}
    private void UpdateSelection()
    {
        var count=_grid.Selected.Count;var showContext=count>0&&_context.Visibility!=Visibility.Visible;
        _selection.Text=count==1?"1 prueba seleccionada":$"{count:N0} pruebas seleccionadas";_context.Visibility=count>0?Visibility.Visible:Visibility.Collapsed;
        if(showContext)Motion.Enter(_context,y:3,milliseconds:140);
        _mark.Visibility=Has(PlanningPermissions.ReportMark)?Visibility.Visible:Visibility.Collapsed;
        _fixedBatch.Visibility=_tabs.SelectedIndex==0&&Has(PlanningPermissions.ReportMark)?Visibility.Visible:Visibility.Collapsed;_fixedBatch.IsEnabled=!_mutating&&Has(PlanningPermissions.ReportMark);
        _mark.IsEnabled=!_mutating&&count is >0 and <=500&&_grid.Selected.All(ReportListTableModel.CanMark);_clockDate.IsEnabled=_clockTime.IsEnabled=!_mutating;
    }
    private void UpdateScanner()
    { _scannerSource.Text=_trackingAvailable?"Tracking · "+(_rows?.Capabilities?.ScannerSource??"verificado"):"La fuente de reportes no está disponible en el servidor"; }
    private async Task Multiple()
    {
        var text=PlanningUi.Field("JOBs",max:20000);text.AcceptsReturn=true;text.Height=190;text.Width=420;text.PlaceholderText="Pegue JOBs desde Excel";AutomationProperties.SetName(text,"JOBs de búsqueda múltiple");var error=PlanningUi.Text("Hasta 500 JOBs únicos",12,true);
        var dialog=new ContentDialog{XamlRoot=XamlRoot,Style=(Style)Application.Current.Resources["LimsEditorDialogStyle"],Title="Búsqueda múltiple",Content=PlanningUi.Stack(text,error),PrimaryButtonText="Buscar JOBs",CloseButtonText="Cancelar"};
        dialog.PrimaryButtonClick+=(_,e)=>{try{PlanningJobList.Parse(text.Text);}catch(FormatException ex){error.Text=ex.Message;e.Cancel=true;}};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;_search.Text=PlanningJobList.Parse(text.Text).Search;await Load();
    }
    public Task Load(){_searchDelay.Stop();return LoadPage(1);}
    private int PageCount=>Math.Max(1,((_rows?.Total??0)+499)/500);
    private void UpdatePager(){_pageLabel.Text=$"Página {_listPage:N0} de {PageCount:N0} · 500 por página";_pageNumber.Text=_listPage.ToString(CultureInfo.InvariantCulture);_previousPage.IsEnabled=!_loading&&!_mutating&&_listPage>1;_nextPage.IsEnabled=!_loading&&!_mutating&&_listPage<PageCount;}
    private async Task LoadPage(int requested,bool preserveScroll=false)
    {
        if(_mutating||_disposed)return;_request.Cancel();_request.Dispose();_request=new();_detailRequest.Cancel();_loading=true;var ct=_request.Token;_progress.IsActive=true;
        _previousPage.IsEnabled=_nextPage.IsEnabled=false;_grid.SetBusy(true);var target=Math.Clamp(requested,1,requested==1?1:PageCount);
        try
        {
            if(_tabs.SelectedIndex!=0){await LoadGroups(true);return;}
            _grid.SetUser(_userId());
            var query=new ReportListQuery(_search.Text,_view.SelectedIndex switch{1=>"Checked",2=>"All",_=>"Pending"},_lab.Text,_test.Text,_service.Text,_buyer.Text,PageSize:500,Sort:_sort,Descending:_descending,VerifiedBy:_verified,Block:_block,CheckedDate:_date,ColumnFilters:_columnFilters.Count==0?null:System.Text.Json.JsonSerializer.Serialize(_columnFilters));
            ReportListRows? rows=null;rows=new ReportListRows(async(page,revision,cancellation)=>{var response=await _api.ReportList(query with{Page=page,Revision=revision},cancellation);if(!response.IsSuccess||response.Value is null)throw new InvalidOperationException(response.Error??"No se pudo consultar Report List.");return response.Value;},ct,target);
            _connection.Text="Consultando…";_status.Text="Cargando ejecuciones…";
            rows.Updated+=error=>{if(ct.IsCancellationRequested)return;if(error is not null){_connection.Text="Sin respuesta";_status.Text=error.Message;return;}_connection.Text="API conectada";var first=rows.Total==0?0:(target-1)*500+1;_status.Text=$"{first:N0}–{Math.Min(rows.Total,(target-1)*500+rows.Count):N0} de {rows.Total:N0} pruebas";_trackingAvailable=rows.Capabilities?.ScannerAvailable==true;UpdateScanner();};
            await rows.LoadNext();if(ct.IsCancellationRequested||rows.Revision is null)return;
            if(target>1&&rows.Count==0){_loading=false;await LoadPage(Math.Max(1,(rows.Total+499)/500));return;}
            _listPage=target;
            _rows=rows;_listRevision=rows.Revision;_grid.SetSort(_sort,_descending);_grid.SetFilters(new[]{string.IsNullOrEmpty(_search.Text)?"":"Job",string.IsNullOrEmpty(_test.Text)?"":"Test",string.IsNullOrEmpty(_lab.Text)?"":"Laboratory",string.IsNullOrEmpty(_buyer.Text)?"":"Buyer",string.IsNullOrEmpty(_verified)?"":"VerifiedBy",_block is null?"":"Block",_date is null?"":"Date"}.Concat(_columnFilters.Keys));_grid.SetSource(rows,preserveScroll);UpdateSelection();
        }
        catch(OperationCanceledException){}
        catch(Exception e){_connection.Text="Sin respuesta";Feedback(e.Message);}
        finally{if(!ct.IsCancellationRequested){_loading=false;_progress.IsActive=false;_grid.SetBusy(false);UpdatePager();}}
    }
    private ReportScheduleQuery GroupQuery()=>new(_search.Text,_scheduleState.SelectedIndex switch{1=>"Pending",2=>"Reviewed",3=>"Exceptions",_=>"All"},_page,100,_page==1?null:_listRevision,GroupByClock:true);
    private async Task LoadGroups(bool reset)
    {
        if(_mutating)return;var ct=_request.Token;_progress.IsActive=true;
        try
        {
            if(reset){_page=1;_detail.Visibility=Visibility.Collapsed;_opened=null;}
            if(_tabs.SelectedIndex==1){var response=await _api.ReportSchedules(GroupQuery(),ct);if(ct.IsCancellationRequested)return;if(!response.IsSuccess||response.Value is not {} data){Feedback(response.Error);return;}if(reset)_scheduleRows.Clear();foreach(var s in data.Items)_scheduleRows.Add(s);_total=data.Total;_listRevision=data.Revision;_status.Text=$"{_scheduleRows.Count:N0} de {_total:N0} horarios · Seleccione un horario para revisarlo";}
            else{var response=await _api.ReportClosures(GroupQuery() with{State="All"},ct);if(ct.IsCancellationRequested)return;if(!response.IsSuccess||response.Value is not {} data){Feedback(response.Error);return;}if(reset)_closureRows.Clear();foreach(var s in data.Items)_closureRows.Add(s);_total=data.Total;_listRevision=data.Revision;_status.Text=$"{_closureRows.Count:N0} de {_total:N0} JOBs completos entre registros disponibles";}
            _connection.Text="API conectada";_page++;_next.IsEnabled=(_tabs.SelectedIndex==1?_scheduleRows.Count:_closureRows.Count)<_total;
        }
        finally{_progress.IsActive=false;}
    }
    private async Task OpenSchedule(Guid id)
    {
        _detailRequest.Cancel();_detailRequest.Dispose();_detailRequest=new();var ct=_detailRequest.Token;
        try
        {
        var response=await _api.ReportSchedule(id,ct,true);if(ct.IsCancellationRequested)return;if(!response.IsSuccess||response.Value is not {} detail){Feedback(response.Error);return;}
        _opened=detail;_detailTitle.Text=$"{detail.Schedule.DateText} · {detail.Schedule.TimeText} · {detail.Schedule.Tests:N0} pruebas";
        _detailMeta.Text=$"Digitado por {detail.Schedule.DigitizedBy} · {detail.Schedule.State}"+(detail.Schedule.ReviewedBy is {} reviewer?$" · Revisado por {reviewer} · {detail.Schedule.ReviewedAt?.ToOffset(TimeSpan.FromHours(-6)):g}":"");
        _history.Text=string.Join('\n',detail.History.Select(h=>$"{h.At.ToOffset(TimeSpan.FromHours(-6)):yyyy-MM-dd HH:mm:ss} · {h.ActorName} · {PlanningStatusText.Action(h.Action)} · {h.Reason}"));_history.Visibility=Visibility.Collapsed;
        _members.SetItems(detail.Members);_review.Visibility=Has(PlanningPermissions.ReportReview)?Visibility.Visible:Visibility.Collapsed;_review.IsEnabled=detail.Schedule.Exceptions==0&&detail.Schedule.ReviewedAt is null&&!_mutating;
        _detail.Visibility=Visibility.Visible;Motion.Enter(_detail,y:4,milliseconds:150);
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){ }
    }
    private async Task SaveDigitization()
    {
        if(_mutating||!OperationalTime(out var at))return;var selected=_grid.Selected.ToArray();if(!Has(PlanningPermissions.ReportMark)||selected.Length is <1 or >500||selected.Any(x=>x.Execution.Complete)){Feedback("Seleccione de 1 a 500 ejecuciones pendientes.");return;}
        _mutating=true;_grid.SetBusy(true);UpdateSelection();
        try
        {
            var reason=PlanningUi.Field("Motivo","Confirmación de digitación",1000);var admin=new CheckBox{Content="Excepción administrativa del modo de captura"};
            var items=PlanningUi.Text(string.Join('\n',selected.Select(x=>$"{x.Execution.Job} · {x.Execution.PlanningTest} · {x.Execution.OriginalTest}")),12);
            var content=PlanningUi.Stack(PlanningUi.Text($"{selected.Length:N0} pruebas · {at:yyyy-MM-dd HH:mm} · Guatemala",14),new ScrollViewer{Content=items,MaxHeight=230},reason);
            var outside=_grid.SelectedOutsideView;var includeOutside=new CheckBox{Content=$"Incluir las {outside:N0} pruebas seleccionadas fuera de la vista actual"};
            if(outside>0)content.Children.Add(includeOutside);
            if(selected.GroupBy(x=>(x.Execution.Job,x.Execution.PlanningTest)).Any(g=>g.Count()>1))content.Children.Add(PlanningUi.Text("Hay pruebas repetidas. Cada ejecución se guardará por separado.",12,true));if(_administrator())content.Children.Add(admin);
            var dialog=new ContentDialog{XamlRoot=XamlRoot,Style=(Style)Application.Current.Resources["LimsEditorDialogStyle"],Title="Confirmar digitación",Content=content,PrimaryButtonText="Confirmar y guardar",CloseButtonText="Cancelar",DefaultButton=ContentDialogButton.Close};dialog.PrimaryButtonClick+=(_,e)=>e.Cancel=string.IsNullOrWhiteSpace(reason.Text)||(outside>0&&includeOutside.IsChecked!=true);
            if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
            var versions=selected.Select(x=>new ReportExecutionVersion(x.Execution.ExecutionId!.Value,x.Version)).OrderBy(x=>x.ExecutionId).ToArray();
            if(_retry is null||_retry.OperationalAt!=at||_retry.Reason!=reason.Text||_retry.AdminOverride!=(admin.IsChecked==true)||!_retry.Executions.SequenceEqual(versions))_retry=new(Guid.NewGuid(),versions,at,reason.Text,admin.IsChecked==true);
            var response=await _api.DigitizeReport(_retry,_request.Token);if(!response.IsSuccess){Feedback(response.Error);return;}_retry=null;Feedback(selected.Length==1?"1 prueba digitada. Horario disponible para chequeo.":$"{selected.Length:N0} pruebas digitadas. Horario disponible para chequeo.");_grid.ClearSelection();if(Changed is{} changed)await changed();
        }
        finally{_mutating=false;_grid.SetBusy(false);UpdateSelection();UpdateScanner();await RefillPage();}
        await Load();
    }
    private async Task ReviewSchedule()
    {
        if(_mutating||!Has(PlanningPermissions.ReportReview)||_opened is not {} detail)return;_mutating=true;_review.IsEnabled=false;
        try
        {
            if(!await PlanningUi.Confirm(XamlRoot,"Chequear horario",$"Confirmar revisión de las {detail.Schedule.Tests:N0} pruebas de {detail.Schedule.DateText} a las {detail.Schedule.TimeText}."))return;
            var response=await _api.ReviewSchedule(new(detail.Schedule.Id,detail.Schedule.Version,detail.Members.Select(m=>new ReportExecutionVersion(m.Item.Execution.ExecutionId!.Value,m.Item.Version)).ToArray(),"Revisión completa del horario",detail.Sessions?.Select(s=>new ReportSessionVersion(s.Id,s.Version)).ToArray()),_request.Token);
            if(!response.IsSuccess){Feedback(response.Error);await OpenSchedule(detail.Schedule.Id);return;}
            Feedback("Horario chequeado y guardado en el historial.");if(Changed is{} changed)await changed();var index=_scheduleRows.ToList().FindIndex(x=>x.Id==detail.Schedule.Id);if(index>=0&&response.Value is{} saved)_scheduleRows[index]=saved;await OpenSchedule(detail.Schedule.Id);
        }
        finally{_mutating=false;if(_opened is{} d)_review.IsEnabled=d.Schedule.Exceptions==0&&d.Schedule.ReviewedAt is null;}
    }
    private async Task ExportSchedules()
    {
        var result=await _api.ExportSchedules(GroupQuery() with{Page=1,Revision=null},_request.Token);if(!result.IsSuccess||result.Value is null){Feedback(result.Error);return;}var path=await PlanningUi.PickFile(XamlRoot,true,"Horarios chequeados");if(path is null)return;await System.IO.File.WriteAllBytesAsync(path,result.Value);Feedback("Chequeados exportados.");
    }
    private async Task Correct(ReportListItem item)
    {
        if(_mutating||!Has(PlanningPermissions.ReportReview)||!item.Execution.Complete)return;ReportListItem[] selected=[item];_mutating=true;_grid.SetBusy(true);UpdateSelection();
        try
        {
            var reason=PlanningUi.Field("Motivo obligatorio",max:1000);var admin=new CheckBox{Content="Excepción administrativa del modo de captura"};var content=PlanningUi.Stack(PlanningUi.Text("La corrección restaura pruebas pendientes e invalida el chequeo de su horario.",13,true),reason);if(_administrator())content.Children.Add(admin);
            var dialog=new ContentDialog{XamlRoot=XamlRoot,Style=(Style)Application.Current.Resources["LimsEditorDialogStyle"],Title=$"Corregir digitación · {item.Execution.Job} · {item.Execution.PlanningTest}",Content=content,PrimaryButtonText="Guardar corrección",CloseButtonText="Cancelar"};dialog.PrimaryButtonClick+=(_,e)=>e.Cancel=string.IsNullOrWhiteSpace(reason.Text);if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
            var response=await _api.CheckReport(new(selected.Select(x=>new ReportExecutionVersion(x.Execution.ExecutionId!.Value,x.Version)).ToArray(),false,reason.Text,admin.IsChecked==true),true,_request.Token);Feedback(response.IsSuccess?"Corrección guardada. Revise el horario anterior.":response.Error);if(response.IsSuccess&&Changed is{} changed)await changed();
        }
        finally{_mutating=false;_grid.SetBusy(false);UpdateSelection();}await Load();
    }
    private async Task Keep(ReportListItem item)
    {
        if(_mutating||!Has(PlanningPermissions.ReportReview))return;_mutating=true;_grid.SetBusy(true);
        try
        {
            var response=await _api.KeepReport(new(new(item.Execution.ExecutionId!.Value,item.Version),!item.KeepInPlanning,"Mantener en Muestreo desde Report List",item.KeepVersion),_request.Token);
            if(!response.IsSuccess){Feedback(response.Error);return;}
            if(!await RefreshSaved(item.Execution.ExecutionId.Value))return;
            Feedback(item.KeepInPlanning?"Permanencia en Muestreo desactivada.":"La prueba se mantendrá en Muestreo después de digitar.");
            if(Changed is{} changed)await changed();
        }
        catch(OperationCanceledException)when(_request.IsCancellationRequested){}
        finally{_mutating=false;_grid.SetBusy(false);UpdateSelection();UpdateScanner();await RefillPage();}
    }
    private async Task CheckDirect(ReportListItem item)
    {
        if(_mutating||!ReportListTableModel.CanCheck(item,Has(PlanningPermissions.ReportMark),Has(PlanningPermissions.ReportReview),_administrator()))return;
        DateTimeOffset at=default;if(!item.Execution.Complete&&!OperationalTime(out at))return;
        _mutating=true;_grid.SetBusy(true);UpdateScanner();
        try
        {
            var version=new ReportExecutionVersion(item.Execution.ExecutionId!.Value,item.Version);
            if(!item.Execution.Complete&&(_retry is null||_retry.OperationalAt!=at||_retry.Executions.Count!=1||_retry.Executions[0]!=version||_retry.Reason!="CHK desde Report List"||_retry.AdminOverride!=(_administrator()&&item.CheckMode!="manual")))
                _retry=new(Guid.NewGuid(),[version],at,"CHK desde Report List",_administrator()&&item.CheckMode!="manual");
            var response=item.Execution.Complete
                ?await _api.CheckReport(new([version],false,"Desmarcado de CHK desde Report List",_administrator()&&item.CheckMode!="manual"),true,_request.Token)
                :await _api.DigitizeReport(_retry!,_request.Token);
            if(!response.IsSuccess){Feedback(response.Error);return;}
            _retry=null;if(!await RefreshSaved(version.ExecutionId))return;
            Feedback(item.Execution.Complete?"Corrección guardada. El chequeo del horario anterior requiere revisión.":$"Digitación guardada · {at:HH:mm} · horario pendiente de chequeo"+(response.Value?.ReadyJobs.Count>0?" · JOB listo en Cierres":""));
            if(Changed is{} changed)await changed();
        }
        catch(OperationCanceledException)when(_request.IsCancellationRequested){}
        finally{_mutating=false;_grid.SetBusy(false);UpdateSelection();UpdateScanner();await RefillPage();}
    }
    private async Task<bool> RefreshSaved(Guid id,Action<ReportListItem>? saved=null)
    {
        var response=await _api.ReportList(new(View:"All",PageSize:1,ExecutionId:id),_request.Token);
        if(!response.IsSuccess||response.Value is not{} page||page.Items.Count!=1||page.Items[0].Execution.ExecutionId!=id){Feedback("La operación se guardó. Actualice el servidor y la lista para consultar su estado.");return false;}
        saved?.Invoke(page.Items[0]);
        var stableOrder=_view.SelectedIndex!=2&&_sort is not ("Time" or "Date" or "VerifiedBy" or "Block" or "Keep" or "Checked");
        var before=_rows?.Count??0;var visible=_rows?.Any(x=>x.Execution.ExecutionId==id)==true;
        var consistent=_rows?.ApplySaved(page.Items[0],page.Revision,_view.SelectedIndex switch{1=>"Checked",2=>"All",_=>"Pending"},page.DataRevision,page.CatalogueRevision,stableOrder)??true;_grid.RefreshSaved();
        _pageNeedsReload|=!visible||before!=(_rows?.Count??0)||!stableOrder;UpdatePager();
        if(!consistent)Feedback("Operación guardada. Actualice la lista para seguir cargando páginas sin omitir pruebas.");return consistent;
    }
    private async Task RefillPage(){if(!_pageNeedsReload||_clearing||_disposed)return;_pageNeedsReload=false;await LoadPage(Math.Min(_listPage,PageCount),true);}
    private async Task Scan(string code)
    {
        if(_mutating||!Has(PlanningPermissions.ReportMark))return;var at=_fixedClock.ForScan(DateTimeOffset.UtcNow);_mutating=true;_grid.SetBusy(true);UpdateScanner();
        try
        {
            var response=await _api.ScanReport(new(code,"Digitación automática mediante lector",at),_request.Token);
            if(!response.IsSuccess){SetScannerStatus(_readerLabel+" · lectura rechazada",_readerConnected);ScanFeedback(ScanError(response.Error),true);return;}
            if(response.Value?.ExecutionIds.Count is not >0){ScanFeedback("El servidor recibió la lectura, pero no confirmó ninguna prueba marcada. Actualice la lista antes de reintentar.",true);return;}
            ReportListItem? target=null;foreach(var id in response.Value?.ExecutionIds??[])if(!await RefreshSaved(id,item=>target=item)){ScanFeedback("El servidor confirmó el marcado del reporte, pero no se pudo actualizar su detalle. Actualice la lista para consultar el estado.",true,"LIMS · Marcado confirmado",true);return;}
            SetScannerStatus(_readerLabel,_readerConnected);
            if(target is{} marked)ScanFeedback($"✓  {marked.Execution.Job} · {marked.Execution.PlanningTest} · marcado {marked.Execution.DigitizedAt?.ToOffset(TimeSpan.FromHours(-6)):HH:mm}",false);
            Feedback(null);
            if(Changed is{} changed)await changed();
        }
        catch(OperationCanceledException)when(_request.IsCancellationRequested){ScanFeedback("La lectura se interrumpió. Consulte el estado de la prueba antes de volver a escanear.",true);}
        catch(Exception e){ScanFeedback("No se pudo confirmar la lectura. "+e.Message,true);}
        finally{_mutating=false;_grid.SetBusy(false);UpdateScanner();await RefillPage();}
    }
    private bool OperationalTime(out DateTimeOffset value)
    {
        value=default;if(_fixedClock.FixedAt is not{} fixedAt){Feedback("Fija un horario desde el reloj para digitar manualmente.");return false;}value=fixedAt;return true;
    }
    private void ColumnFilter(string key,FrameworkElement anchor)
    {
        if(key is not ("Job" or "Test" or "Laboratory" or "Buyer" or "VerifiedBy" or "Block"))
        {
            var mapped=key switch{"Date"=>"TestedOn","Time"=>"DigitizedAt","Checked"=>"DigitizationState",_=>key};
            var date=key is "InDate" or "DueDate" or "Date";
            var input=PlanningUi.Field(date?"Fecha · yyyy-MM-dd":"Contiene",_columnFilters.GetValueOrDefault(key));input.Width=255;
            AutomationProperties.SetName(input,"Filtro de columna "+key);
            var notice=PlanningUi.Text("",12,true);var body=PlanningUi.Stack(input,notice);var popup=new Flyout{Content=body};
            // Store API column names while keeping header identifiers separate.
            async Task ApplyMapped(){if(date&&!string.IsNullOrWhiteSpace(input.Text)&&!DateOnly.TryParseExact(input.Text,"yyyy-MM-dd",out _)){notice.Text="Use yyyy-MM-dd.";return;}if(string.IsNullOrWhiteSpace(input.Text))_columnFilters.Remove(mapped);else _columnFilters[mapped]=input.Text.Trim();popup.Hide();await Load();}
            input.Text=_columnFilters.GetValueOrDefault(mapped)??"";
            body.Children.Add(Compact("Aplicar columna",ApplyMapped));body.Children.Add(Compact("Quitar filtro",async()=>{input.Text="";await ApplyMapped();}));input.KeyDown+=async(_,e)=>{if(e.Key==VirtualKey.Enter){e.Handled=true;await ApplyMapped();}};popup.ShowAt(anchor);return;
        }
        var value=key switch{"Job"=>_search.Text,"Test"=>_test.Text,"Laboratory"=>_lab.Text,"Buyer"=>_buyer.Text,"VerifiedBy"=>_verified,"Block"=>_block?.ToString(CultureInfo.InvariantCulture),"Date"=>_date?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),_=>""};var field=PlanningUi.Field(key=="Date"?"Fecha · yyyy-MM-dd":"Contiene",value);field.Width=255;var error=PlanningUi.Text("",12,true);var panel=PlanningUi.Stack(field,error);var flyout=new Flyout{Content=panel};
        async Task Apply(){if(key=="Block"&&!string.IsNullOrWhiteSpace(field.Text)&&(!long.TryParse(field.Text,out var n)||n<1)){error.Text="Escriba el bloque histórico.";return;}if(key=="Date"&&!string.IsNullOrWhiteSpace(field.Text)&&!DateOnly.TryParseExact(field.Text,"yyyy-MM-dd",out _)){error.Text="Use yyyy-MM-dd.";return;}switch(key){case "Job":_search.Text=field.Text;break;case "Test":_test.Text=field.Text;break;case "Laboratory":_lab.Text=field.Text;break;case "Buyer":_buyer.Text=field.Text;break;case "VerifiedBy":_verified=field.Text;break;case "Block":_block=long.TryParse(field.Text,out var b)?b:null;break;case "Date":_date=DateOnly.TryParseExact(field.Text,"yyyy-MM-dd",out var d)?d:null;break;}flyout.Hide();await Load();}
        panel.Children.Add(Compact("Aplicar columna",Apply));panel.Children.Add(Compact("Quitar filtro",async()=>{field.Text="";await Apply();}));field.KeyDown+=async(_,e)=>{if(e.Key==VirtualKey.Enter){e.Handled=true;await Apply();}};flyout.ShowAt(anchor);
    }
    private async Task Synchronize()
    {
        if(_loading||_mutating||!IsLoaded)return;
        try
        {
            var tab=_tabs.SelectedIndex;var ct=_request.Token;long? revision;
            if(tab==0){var result=await _api.ReportList(new(PageSize:1),ct);revision=result.IsSuccess?result.Value?.Revision:null;}
            else if(tab==1){var result=await _api.ReportSchedules(GroupQuery() with{Page=1,PageSize=1,Revision=null},ct);revision=result.IsSuccess?result.Value?.Revision:null;}
            else{var result=await _api.ReportClosures(GroupQuery() with{Page=1,PageSize=1,Revision=null,State="All"},ct);revision=result.IsSuccess?result.Value?.Revision:null;}
            if(ct.IsCancellationRequested||tab!=_tabs.SelectedIndex)return;
            var current=tab==0?_rows?.Revision:_listRevision;
            if(revision is null){_connection.Text="Sin respuesta";return;}
            _connection.Text="API conectada";if(current==revision)return;
            if(_grid.Selected.Count>0||_opened is not null||(_rows?.Count??0)>0)Feedback("Hay cambios de otro usuario. Actualice antes de confirmar; su selección se conserva.");else await Load();
        }
        catch(OperationCanceledException){}
    }
    public void Clear(){if(_disposed)return;_clearing=true;_batchDialog?.Dispose();_batchDialog?.Hide();StopScanner();_searchDelay.Stop();_fixedClock.Clear();_clockLabel.Text="Hora actual";_clockTick.Stop();_scanReceipt.Text="Escanea el reporte para marcar su JOB y prueba";_receivedScans=0;_scannerActivity.Text="Todavía no se han recibido datos del lector.";_sync.Stop();_request.Cancel();_detailRequest.Cancel();_grid.SetSource(null);_grid.SetUser(null);_rows=null;_search.Text="";_columnFilters.Clear();_scheduleRows.Clear();_closureRows.Clear();_opened=null;_retry=null;_pageNeedsReload=false;Feedback(null);_clearing=false;}
    public void Dispose(){if(_disposed)return;Clear();_disposed=true;_serial.Dispose();_request.Dispose();_detailRequest.Dispose();}
}
