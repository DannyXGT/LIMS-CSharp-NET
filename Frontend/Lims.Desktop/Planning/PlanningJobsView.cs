using System.Globalization;
using Lims.Contracts.Planning;
using Lims.DesignSystem.Presentation;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Lims.Desktop.Planning;

public sealed partial class PlanningJobsView : Grid,IDisposable
{
    private readonly PlanningApiClient _api;private readonly Func<Guid,Task> _open;private readonly Func<IReadOnlyList<string>> _permissions;
    private readonly PlanningSheetGrid _sheet=new();
    private readonly TextBox _search=Field("Buscar JOB",330,20000),_test=Field("Filtrar prueba",200),_lab=Field("Laboratorio",155),_service=Field("Servicio",155),_buyer=Field("Buyer / marca",155),_vendor=Field("Vendor",155),_equivalence=Field("Equivalencia",155);
    private readonly TextBox _from=Field("Vence desde · yyyy-MM-dd",165,10),_until=Field("Vence hasta · yyyy-MM-dd",165,10),_inFrom=Field("Ingreso desde · yyyy-MM-dd",165,10),_inUntil=Field("Ingreso hasta · yyyy-MM-dd",165,10);
    private readonly ComboBox _mode=new(){ItemsSource=new[]{"Contiene","Exacto","Comienza con"},SelectedIndex=0,Width=165,MinHeight=36};
    private readonly ComboBox _state=new(){Header="Estado / condición",ItemsSource=new[]{"Todos","Pendientes operativos","Digitadas (hora y fecha)","Incidencias","NO PROGRAMAR","Sin información de digitación","Información incompleta"},SelectedIndex=0,Width=200};
    private readonly ComboBox _incident=new(){Header="Incidencia",ItemsSource=new[]{"Todas","Pruebas faltantes","Vendor faltante","Equivalencia faltante"},SelectedIndex=0,Width=200};
    private readonly CheckBox _overdue=new(){Content="Vencidas (incluye hoy; excluye On Hold)"};
    private readonly TextBlock _message=PlanningUi.Text("",12,true),_selection=PlanningUi.Text("Sin celdas seleccionadas",12,true),_pageLabel=PlanningUi.Text("",12,true);
    private readonly TextBlock _recordCount;
    private readonly StackPanel _pager;
    private readonly DispatcherTimer _feedbackTimer=new(){Interval=TimeSpan.FromSeconds(5)};
    private readonly TextBlock[] _counters=Enumerable.Range(0,4).Select(_=>PlanningUi.Text("—",20)).ToArray();
    private readonly ComboBox _target=new(){ItemsSource=new[]{"JOB","Prueba","Equivalencia","Laboratorio","Cliente / Buyer"},SelectedIndex=0,Width=145,MinHeight=36};
    private readonly ProgressBar _progress=new(){IsIndeterminate=false,Visibility=Visibility.Collapsed,Height=3};
    private readonly TextBlock _emptyTitle=PlanningUi.Text("Sin resultados",18),_emptyDetail=PlanningUi.Text("Ajuste la búsqueda o restablezca los filtros para volver a consultar.",13,true);
    private readonly Border _empty;
    private readonly Button _reset,_previous,_next,_status,_filter;
    private string _searchTarget="JOB";
    private PlanningMultiSearchWindow? _multiple;
    private readonly FrameworkElement _tools;
    private readonly ConsultationHeader _heading;
    private CancellationTokenSource? _load;private int _page=1,_total;private string _sort="job";private bool _descending,_solo,_editing,_saving;
    public PlanningJobsView(PlanningApiClient api,Func<Guid,Task> open,Func<IReadOnlyList<string>>? permissions=null)
    {
        _api=api;_open=open;_permissions=permissions??(()=>[]);RowSpacing=12;
        foreach(var height in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star)})RowDefinitions.Add(new(){Height=height});
        var solo=ConsultationVisuals.Action("Solo tabla",LimsIconKind.Maximize,()=>
        {
            _solo=!_solo;if(_tools is{} tools)tools.Visibility=_solo?Visibility.Collapsed:Visibility.Visible;
            if(_heading is{} heading)heading.TableOnly(_solo);return Task.CompletedTask;
        });
        solo.Click+=(_,_)=>{var name=_solo?"Recuperar herramientas":"Solo tabla";((TextBlock)((StackPanel)solo.Content).Children[1]).Text=name;AutomationProperties.SetName(solo,name);};
        _status=ConsultationVisuals.Action("Estado del JOB",LimsIconKind.ViewDetail,Open,true);_status.IsEnabled=false;
        _heading=new ConsultationHeader(_counters,solo,_status);PlanningUi.Place(this,_heading,0);
        _search.PlaceholderText="Buscar JOB…";_search.Width=double.NaN;_search.MinWidth=150;_search.MinHeight=36;_search.AcceptsReturn=true;_search.Height=36;
        _search.TextChanged+=(_,_)=>
        {
            if(_search.Text.IndexOfAny(['\r','\n','\t'])>=0){_search.Text=_search.Text.Replace("\r\n",",",StringComparison.Ordinal).Replace('\r',',').Replace('\n',',').Replace('\t',',').Trim(',');_search.SelectionStart=_search.Text.Length;}
            UpdateFilters();
        };
        _target.Style=_mode.Style=(Style)Application.Current.Resources["LimsCompactComboBoxStyle"];_mode.MinHeight=36;
        AutomationProperties.SetName(_target,"Buscar por");AutomationProperties.SetName(_mode,"Modo de búsqueda JOB");
        _target.SelectionChanged+=(_,_)=>{_searchTarget=(string)_target.SelectedItem;_search.PlaceholderText="Buscar "+_searchTarget.ToLowerInvariant()+"…";_mode.IsEnabled=_target.SelectedIndex==0;UpdateFilters();};
        _filter=ConsultationVisuals.Action("Filtros",LimsIconKind.Filter,()=>Task.CompletedTask);
        foreach(var field in new[]{_test,_equivalence,_lab,_service,_buyer,_vendor,_from,_until,_inFrom,_inUntil})field.Width=200;
        foreach(var field in new[]{_from,_until,_inFrom,_inUntil})field.PlaceholderText="yyyy-MM-dd";
        var criteria=PlanningUi.Stack(PlanningUi.Row(Labeled(_test,"Prueba o equivalencia"),Labeled(_equivalence,"Equivalencia")),PlanningUi.Row(Labeled(_lab,"Laboratorio"),Labeled(_service,"Servicio")),PlanningUi.Row(Labeled(_buyer,"Buyer / marca"),Labeled(_vendor,"Vendor")),PlanningUi.Row(DateFilter(_from,"Vencimiento desde"),DateFilter(_until,"Hasta")),PlanningUi.Row(DateFilter(_inFrom,"Ingreso desde"),DateFilter(_inUntil,"Hasta")),PlanningUi.Row(_state,_incident),_overdue);
        AddOperationalFilters(criteria);
        var panel=PlanningUi.Stack(PlanningUi.Text("Filtros por columna",18),PlanningUi.Text("Se aplican a todos los resultados de la consulta.",13,true),new ScrollViewer{Content=criteria,MaxHeight=360,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},ConsultationVisuals.Action("Aplicar filtros",LimsIconKind.ApplyFilters,async()=>{_filter.Flyout.Hide();_page=1;await Load();},true));
        _filter.Flyout=new Flyout{Content=panel};
        _filter.Flyout.Opened+=(_,_)=>{if(Motion.Enabled)Motion.Enter(panel,y:4,milliseconds:120);};
        var columns=ConsultationVisuals.Action("Columnas",LimsIconKind.Results,()=>Task.CompletedTask);var choices=new StackPanel{Spacing=4};
        foreach(var column in PlanningSheetModel.AvailableColumns)
        {
            var check=new CheckBox{Content=column.Title,IsChecked=column.InitiallyVisible,IsEnabled=column.Key!="Job"};
            check.Click+=(_,_)=>{if(check.IsChecked==true&&!_sheet.Model.Columns.Contains(column))_sheet.Model.Columns.Add(column);if(check.IsChecked!=true)_sheet.Model.Columns.Remove(column);_sheet.Model.Columns.Sort((a,b)=>PlanningSheetModel.AvailableColumns.ToList().IndexOf(a).CompareTo(PlanningSheetModel.AvailableColumns.ToList().IndexOf(b)));_sheet.RebuildColumns();};choices.Children.Add(check);
        }
        var restoreColumns=ConsultationVisuals.Action("Restablecer columnas",LimsIconKind.Restore,()=>
        {
            _sheet.Model.Columns.Clear();_sheet.Model.Columns.AddRange(PlanningSheetModel.AvailableColumns.Where(c=>c.InitiallyVisible));
            foreach(var check in choices.Children.OfType<CheckBox>()){var column=PlanningSheetModel.AvailableColumns.Single(c=>c.Title==(string)check.Content);check.IsChecked=column.InitiallyVisible;}
            _sheet.RebuildColumns();return Task.CompletedTask;
        });
        columns.Flyout=new Flyout{Content=PlanningUi.Stack(PlanningUi.Text("Columnas de Consulta",18),new ScrollViewer{Content=choices,MaxHeight=390},restoreColumns)};
        _reset=ConsultationVisuals.Action("Restablecer filtros",LimsIconKind.Refresh,ClearFilters,quiet:true);_reset.Visibility=Visibility.Collapsed;
        var searchRow=new Grid{ColumnSpacing=8};foreach(var width in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})searchRow.ColumnDefinitions.Add(new(){Width=width});
        var searchControls=new FrameworkElement[]{_target,_search,_mode,ConsultationVisuals.Action("Buscar",LimsIconKind.Search,Search,true)};
        for(int i=0;i<searchControls.Length;i++){Grid.SetColumn(searchControls[i],i);searchRow.Children.Add(searchControls[i]);}
        var multiple=ConsultationVisuals.Action("Búsqueda múltiple",LimsIconKind.Search,PasteJobs);ConsultationVisuals.Symbol(multiple,Symbol.Copy);
        var secondary=PlanningUi.Row(multiple,_filter,columns);secondary.Spacing=8;
        var export=ConsultationVisuals.Action("Exportar CSV",LimsIconKind.Export,Export,quiet:true);ConsultationVisuals.Symbol(export,Symbol.Download);
        var utilities=PlanningUi.Row(export,_reset);utilities.Spacing=4;utilities.Padding=new Thickness(12,0,0,0);utilities.BorderBrush=ConsultationVisuals.Edge;utilities.BorderThickness=new Thickness(1,0,0,0);
        var commands=ConsultationVisuals.Split(secondary,utilities);commands.RowDefinitions.Add(new(){Height=GridLength.Auto});commands.RowDefinitions.Add(new(){Height=GridLength.Auto});commands.RowSpacing=8;
        commands.SizeChanged+=(_,_)=>{bool narrow=commands.ActualWidth<850;commands.RowSpacing=narrow?8:0;Grid.SetRow(utilities,narrow?1:0);Grid.SetColumn(utilities,narrow?0:1);Grid.SetColumnSpan(utilities,narrow?2:1);utilities.HorizontalAlignment=HorizontalAlignment.Right;};
        var tools=PlanningUi.Stack(searchRow,commands);tools.Spacing=8;
        var toolsSurface=ConsultationVisuals.Surface(tools,new Thickness(16,12,16,12));_tools=toolsSurface;PlanningUi.Place(this,_tools,1);
        SizeChanged+=(_,_)=>{bool shortWindow=ActualHeight<640;RowSpacing=shortWindow?8:12;_heading.CompactHeight(shortWindow);toolsSurface.Padding=shortWindow?new Thickness(12,8,12,8):new Thickness(16,12,16,12);};
        foreach(var field in new[]{_test,_lab,_service,_buyer,_vendor,_equivalence,_from,_until,_inFrom,_inUntil})field.TextChanged+=(_,_)=>UpdateFilters();
        _state.SelectionChanged+=(_,_)=>UpdateFilters();_incident.SelectionChanged+=(_,_)=>UpdateFilters();_mode.SelectionChanged+=(_,_)=>UpdateFilters();_overdue.Click+=(_,_)=>UpdateFilters();
        var table=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star),GridLength.Auto);table.RowSpacing=0;
        PlanningUi.Place(table,_progress,0);PlanningUi.Place(table,_sheet,1);
        var emptyContent=PlanningUi.Stack(new LimsIcon{Icon=LimsIconKind.Search,Size=28,HorizontalAlignment=HorizontalAlignment.Center},_emptyTitle,_emptyDetail,ConsultationVisuals.Action("Reintentar",LimsIconKind.Refresh,Load));
        emptyContent.MaxWidth=490;emptyContent.HorizontalAlignment=HorizontalAlignment.Center;_emptyTitle.TextAlignment=_emptyDetail.TextAlignment=TextAlignment.Center;
        _empty=new Border{Child=emptyContent,Padding=new Thickness(28),Background=PlanningUi.Brush("LimsSurfaceBrush"),Visibility=Visibility.Collapsed};PlanningUi.Place(table,_empty,1);
        _previous=FooterButton("Anterior",new LimsIcon{Icon=LimsIconKind.ChevronLeft,Size=14},async()=>{if(_page>1){_page--;await Load();}});
        _next=FooterButton("Siguiente",new LimsIcon{Icon=LimsIconKind.ChevronRight,Size=14},async()=>{if(_page*100<_total){_page++;await Load();}});
        _pager=PlanningUi.Row(_pageLabel,_previous,_next);_pager.Spacing=4;_pager.Visibility=Visibility.Collapsed;
        var keyboard=FooterButton("Atajos de teclado",new FontIcon{FontFamily=new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),Glyph=char.ConvertFromUtf32((int)Symbol.Keyboard),FontSize=16},()=>Task.CompletedTask);
        keyboard.Flyout=new Flyout{Content=PlanningUi.Stack(PlanningUi.Text("Atajos de la tabla",16),PlanningUi.Text("Ctrl+C · Copiar selección\nCtrl+V · Pegar en campos autorizados\nShift+clic o Shift+flechas · Ampliar rango\nDoble clic · Editar campo autorizado\nEnter · Abrir expediente o confirmar edición\nTab · Guardar y continuar editando\nEscape · Cancelar edición",13))};
        var recordCount=PlanningUi.Text("",12,true);_recordCount=recordCount;
        var footer=new Grid{Height=42,ColumnSpacing=12,Padding=new Thickness(12,0,8,0),Background=ConsultationVisuals.Navy,BorderBrush=ConsultationVisuals.Edge,BorderThickness=new Thickness(0,1,0,0)};
        foreach(var width in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})footer.ColumnDefinitions.Add(new(){Width=width});
        var feedback=new Grid{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Stretch};feedback.Children.Add(_selection);feedback.Children.Add(_message);_message.Visibility=Visibility.Collapsed;
        var footerItems=new FrameworkElement[]{recordCount,feedback,_pager,keyboard};
        for(int i=0;i<footerItems.Length;i++){Grid.SetColumn(footerItems[i],i);footer.Children.Add(footerItems[i]);footerItems[i].VerticalAlignment=VerticalAlignment.Center;}
        foreach(var text in new[]{recordCount,_selection,_message,_pageLabel}){text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;text.VerticalAlignment=VerticalAlignment.Center;}
        _selection.HorizontalAlignment=_message.HorizontalAlignment=HorizontalAlignment.Right;
        AutomationProperties.SetName(footer,"Pie de Consulta de Jobs");PlanningUi.Place(table,footer,2);
        var surface=ConsultationVisuals.Surface(table,new Thickness(0));PlanningUi.Place(this,surface,2);
        AutomationProperties.SetLiveSetting(_message,AutomationLiveSetting.Polite);
        _feedbackTimer.Tick+=(_,_)=>ClearFeedback();Unloaded+=(_,_)=>_feedbackTimer.Stop();
        _sheet.SelectionChanged+=()=>{var r=_sheet.Model.Range;int rows=r.Bottom-r.Top+1,columns=r.Right-r.Left+1;_selection.Text=_sheet.Model.Rows.Count==0?"Sin celdas seleccionadas":rows==1&&columns==1?"1 celda seleccionada":$"{rows} filas × {columns} columnas";ClearFeedback();};
        _sheet.CopyRequested+=Copy;_sheet.PasteRequested+=Paste;_sheet.OpenRequested+=Open;_sheet.EditRequested+=Edit;
        _sheet.SortRequested+=async key=>{_descending=_sort==SortKey(key)&&!_descending;_sort=SortKey(key);_page=1;await Load();};
        _sheet.FilterRequested+=ShowColumnFilter;
        _sheet.SetSort("Job",false);
        _search.AddHandler(KeyDownEvent,new KeyEventHandler(async(_,e)=>{if(e.Key==VirtualKey.Enter){e.Handled=true;await Search();}}),true);
    }
    private static Button FooterButton(string name,UIElement icon,Func<Task> action)
    {
        var button=new Button{Content=icon,Width=32,Height=32,MinHeight=0,MinWidth=0,Padding=new Thickness(6),VerticalAlignment=VerticalAlignment.Center,Style=(Style)Application.Current.Resources["LimsIconButtonStyle"]};
        AutomationProperties.SetName(button,name);ToolTipService.SetToolTip(button,name);button.Click+=async(_,_)=>await action();return button;
    }
    private static TextBox Labeled(TextBox field,string label){field.Header=label;return field;}
    private TextBox? FilterField(string key)=>key switch{"Job"=>_search,"OriginalTest"=>_test,"Equivalence"=>_equivalence,"Laboratory"=>_lab,"Service"=>_service,"InDate"=>_inFrom,"DueDate"=>_from,_=>null};
    private void UpdateFilters()
    {
        if(_reset is null)return;
        bool active=new[]{_search,_test,_lab,_service,_buyer,_vendor,_equivalence,_from,_until,_inFrom,_inUntil}.Any(f=>!string.IsNullOrWhiteSpace(f.Text))||_state.SelectedIndex>0||_incident.SelectedIndex>0||_overdue.IsChecked==true||_mode.SelectedIndex>0||_columnFilters.Count>0||_holdFilter.SelectedIndex>0||_tagFilter.SelectedIndex>0;
        _reset.Visibility=active||_batchId is not null?Visibility.Visible:Visibility.Collapsed;
        _sheet.SetFilters(PlanningSheetModel.AvailableColumns.Where(c=>c.Key=="DigitizationState"?_state.SelectedIndex>0:FilterField(c.Key)is{} f&&!string.IsNullOrWhiteSpace(f.Text)).Select(c=>c.Key).Concat(_columnFilters.Keys));
        _columnSummary.Text=_columnFilters.Count==0?"Sin filtros de cabecera.":"Cabeceras: "+string.Join(" · ",_columnFilters.Select(f=>PlanningSheetModel.AvailableColumns.Single(c=>c.Key==f.Key).Title+" = "+f.Value));
    }
    private static TextBox Field(string name,int width,int max=512)
    {var field=PlanningUi.Field(name,max:max);field.Header=null;field.PlaceholderText=name;field.Width=width;field.MinHeight=36;AutomationProperties.SetName(field,name);return field;}
    private static string SortKey(string key)=>key switch{"OriginalTest"=>"test","DigitizationState"=>"state",_=>char.ToLowerInvariant(key[0])+key[1..]};
    private Task Search(){_page=1;return Load();}
    private Guid? _batchId;
    public async Task OpenBatch(Guid id)
    {
        foreach(var field in new[]{_search,_test,_lab,_service,_buyer,_vendor,_equivalence,_from,_until,_inFrom,_inUntil})field.Text="";
        _columnFilters.Clear();_holdFilter.SelectedIndex=_tagFilter.SelectedIndex=_state.SelectedIndex=_incident.SelectedIndex=_mode.SelectedIndex=_target.SelectedIndex=0;_overdue.IsChecked=false;
        _batchId=id;_scope.SelectedIndex=1;_page=1;UpdateFilters();await Load();Notify("Consulta filtrada por lote · "+id.ToString()[..8]);
    }
    private async Task ClearFilters()
    {_batchId=null;foreach(var field in new[]{_search,_test,_lab,_service,_buyer,_vendor,_equivalence,_from,_until,_inFrom,_inUntil})field.Text="";_columnFilters.Clear();_holdFilter.SelectedIndex=_tagFilter.SelectedIndex=0;_state.SelectedIndex=_incident.SelectedIndex=_mode.SelectedIndex=0;_overdue.IsChecked=false;_target.SelectedIndex=0;UpdateFilters();_page=1;await Load();}
    private PlanningQuery Query()
    {
        static DateOnly? Date(TextBox field)=>string.IsNullOrWhiteSpace(field.Text)?null:DateOnly.TryParseExact(field.Text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)?date:throw new FormatException("Use fechas yyyy-MM-dd en los filtros.");
        string Combine(TextBox field,string target)=>_searchTarget==target&&!string.IsNullOrWhiteSpace(_search.Text)?_search.Text:field.Text;
        return new(_searchTarget=="JOB"?_search.Text:null,Combine(_test,"Prueba"),Combine(_lab,"Laboratorio"),_service.Text,_state.SelectedIndex switch{1=>"Pending",2=>"Digitado",3=>"Exceptions",4=>"NoProgramar",5=>"Unknown",6=>"Partial",_=>null},Date(_from),Date(_until),_sort,_descending,_page,100,Combine(_buyer,"Cliente / Buyer"),_vendor.Text,Combine(_equivalence,"Equivalencia"),
            _incident.SelectedIndex switch{1=>"MissingTests",2=>"MissingVendor",3=>"MissingEquivalence",_=>null},_mode.SelectedIndex switch{1=>"Exact",2=>"Prefix",_=>"Contains"},Date(_inFrom),Date(_inUntil),_overdue.IsChecked==true,
            Scope:_scope.SelectedIndex==0?"Operational":"History",OnHold:_holdFilter.SelectedIndex==0?null:_holdFilter.SelectedIndex==1,Tagged:_tagFilter.SelectedIndex==0?null:_tagFilter.SelectedIndex==1,
            ColumnFilters:_columnFilters.Count==0?null:System.Text.Json.JsonSerializer.Serialize(_columnFilters),BatchId:_batchId);
    }
    public async Task Load()
    {
        if(_saving||_editing){Notify("Termine o cancele la edición antes de cambiar la consulta.");return;}
        Cancel();_load=new();var ct=_load.Token;ClearFeedback();_progress.Visibility=Visibility.Visible;_progress.IsIndeterminate=true;_empty.Visibility=Visibility.Collapsed;_sheet.IsHitTestVisible=false;_status.IsEnabled=false;_previous.IsEnabled=_next.IsEnabled=false;
        try
        {
            var result=await _api.Jobs(Query(),ct);if(ct.IsCancellationRequested)return;
            if(!result.IsSuccess||result.Value is not{} page){_sheet.Model.Rows.Clear();_total=0;_recordCount.Text="— ejecuciones · — JOBs";_pager.Visibility=Visibility.Collapsed;foreach(var counter in _counters)counter.Text="—";ShowEmpty("No se pudo consultar",result.Error??"Revise su conexión y vuelva a intentar.");return;}
            if(page.Total>0&&(page.Page-1)*page.PageSize>=page.Total){_page=(page.Total+page.PageSize-1)/page.PageSize;await Load();return;}
            _sheet.Model.Rows.Clear();foreach(var row in page.Items)_sheet.Model.Rows.Add(row);_total=page.Total;_page=page.Page;
            _recordCount.Text=page.Counts is{} counts?$"{counts.Executions.ToString("N0",CultureInfo.InvariantCulture)} {(counts.Executions==1?"ejecución":"ejecuciones")} · {counts.Jobs.ToString("N0",CultureInfo.InvariantCulture)} {(counts.Jobs==1?"JOB":"JOBs")}":"— ejecuciones · — JOBs";
            _sheet.Select(0,0);_sheet.SetSort(PlanningSheetModel.AvailableColumns.FirstOrDefault(c=>SortKey(c.Key)==_sort)?.Key??"Job",_descending);
            _pageLabel.Text=$"{((page.Page-1)*page.PageSize+1).ToString("N0",CultureInfo.InvariantCulture)}–{((page.Page-1)*page.PageSize+page.Items.Count).ToString("N0",CultureInfo.InvariantCulture)} de {page.Total.ToString("N0",CultureInfo.InvariantCulture)}";_pager.Visibility=page.Total>page.PageSize?Visibility.Visible:Visibility.Collapsed;
            if(page.Counts is{} c){_counters[0].Text=$"{c.Records:N0}";_counters[1].Text=$"{c.Pending:N0}";_counters[2].Text=$"{c.Overdue:N0}";_counters[3].Text=$"{c.OnHoldTests:N0}";}
            else{_counters[0].Text=$"{page.Total:N0} registros";foreach(var label in _counters.Skip(1))label.Text="—";}
            _heading.Context(page,_batchId);_coverage.Text=(page.Scope?.Notice??"Cobertura no informada.")+$"\nFilas sin pruebas: {page.Scope?.MissingTests} · sin Due Date: {page.Scope?.MissingDueDates} · sin hora ni fecha: {page.Scope?.MissingDigitization}.";
            if(page.Total==0)ShowEmpty("Sin resultados",_scope.SelectedIndex==0?"Ajuste los filtros. Para consultar JOBs fuera del período operativo, seleccione Histórico completo en Filtros.":"Ajuste la búsqueda o restablezca los filtros para volver a consultar.");
            System.Diagnostics.Debug.WriteLine($"Consulta de Jobs: {page.Total} registros filtrados en servidor; selección sobre página {page.Page}.");
        }
        catch(FormatException e){Notify(e.Message);}catch(OperationCanceledException)when(ct.IsCancellationRequested){ }
        finally{if(!ct.IsCancellationRequested){_progress.IsIndeterminate=false;_progress.Visibility=Visibility.Collapsed;_sheet.IsHitTestVisible=true;_status.IsEnabled=_sheet.Model.Rows.Count>0;_previous.IsEnabled=_page>1;_next.IsEnabled=_page*100<_total;UpdateFilters();}}
    }
    private void ShowEmpty(string title,string detail)
    {
        _emptyTitle.Text=title;_emptyDetail.Text=detail;_empty.Visibility=Visibility.Visible;_selection.Text="Sin celdas seleccionadas";Notify(detail);
    }
    private Task Open()=>_sheet.Model.Rows.Count==0?Task.CompletedTask:_open(_sheet.Model.Rows[_sheet.Model.Active.Row].EpisodeId);
    public async Task RefreshPreservingSelection()
    {
        var anchor=_sheet.Model.Anchor;var end=_sheet.Model.End;var scroll=_sheet.ScrollPosition;
        await Load();if(_sheet.Model.Rows.Count==0)return;
        _sheet.Model.Select(anchor.Row,anchor.Column);_sheet.Model.Select(end.Row,end.Column,true);_sheet.Paint();
        _sheet.RestoreScroll(scroll);_selection.Text=anchor==end?"1 celda seleccionada":$"{_sheet.Model.Range.Bottom-_sheet.Model.Range.Top+1} filas × {_sheet.Model.Range.Right-_sheet.Model.Range.Left+1} columnas";
    }
    private void ClearFeedback(){_feedbackTimer.Stop();_message.Visibility=Visibility.Collapsed;_selection.Visibility=Visibility.Visible;}
    private void Notify(string message){_message.Text=message;ToolTipService.SetToolTip(_message,message);_selection.Visibility=Visibility.Collapsed;_message.Visibility=Visibility.Visible;_feedbackTimer.Stop();_feedbackTimer.Start();if(Motion.Enabled)Motion.Enter(_message,y:2,milliseconds:120);}
    private static void ClipboardText(string text){var content=new DataPackage();content.SetText(text);Clipboard.SetContent(content);Clipboard.Flush();}
    private async Task Copy()
    {try{if(_sheet.Model.Rows.Count==0){Notify("Seleccione una celda.");return;}ClipboardText(_sheet.Model.Copy());Notify("Selección copiada al portapapeles de Windows.");}catch(Exception e)when(e is System.Runtime.InteropServices.COMException){Notify("El portapapeles está ocupado; vuelva a copiar.");}await Task.CompletedTask;}
    private async Task<string?> ReadClipboard()
    {try{var data=Clipboard.GetContent();return data.Contains(StandardDataFormats.Text)?await data.GetTextAsync():null;}catch(System.Runtime.InteropServices.COMException){Notify("El portapapeles no está disponible.");return null;}}
    private Task PasteJobs()
    {
        if(_multiple is not null){_multiple.Activate();return Task.CompletedTask;}
        _multiple=new PlanningMultiSearchWindow(_api,async text=>{_scope.SelectedIndex=1;_target.SelectedIndex=0;_search.Text=text;_mode.SelectedIndex=1;await Search();});
        _multiple.Closed+=(_,_)=>_multiple=null;PlanningUi.Track(_multiple);_multiple.Activate();return Task.CompletedTask;
    }
    private bool CanEdit=>_permissions().Contains(PlanningPermissions.EditWip,StringComparer.Ordinal);
    private async Task Paste()
    {
        if(_editing||_saving)return;
        var text=await ReadClipboard();if(text is null){Notify("El portapapeles no contiene texto.");return;}
        try
        {
            var preview=_sheet.Model.Preview(text,CanEdit);var start=_sheet.Model.Active;
            var rows=new ListView{MaxHeight=260,SelectionMode=ListViewSelectionMode.None};
            foreach(var cell in preview.Cells)rows.Items.Add(PlanningUi.Text($"F{cell.Row+1} C{cell.Column+1} · {cell.Status}\n{cell.Before} → {cell.After}",12));
            var reason=Field("Motivo del cambio",510,1000);reason.Text="Actualización operativa desde Consulta de Jobs";
            var dialog=new ContentDialog{XamlRoot=XamlRoot,Title="Revisar pegado desde Excel",Content=PlanningUi.Stack(PlanningUi.Text($"Inicio F{start.Row+1} C{start.Column+1} · {preview.Valid} válidos · {preview.Invalid} inválidos · {preview.Skipped} omitidos",13),PlanningUi.Text("Solo Responsable y Observación. Se guardan juntos los cambios válidos; los demás se omiten.",12,true),rows,reason),PrimaryButtonText="Confirmar cambios válidos",CloseButtonText="Cancelar",DefaultButton=ContentDialogButton.Close,IsPrimaryButtonEnabled=preview.Valid>0};
            _editing=true;try{if(await dialog.ShowAsync()==ContentDialogResult.Primary)await Save(preview.Cells.Where(c=>c.Change is not null).Select(c=>c.Change!).ToArray(),reason.Text);}finally{_editing=false;_sheet.FocusTable();}
        }
        catch(FormatException e){Notify(e.Message);}
    }
    private async Task Save(PlanningNoteCell[] cells,string reason)
    {
        _saving=true;try{var result=await _api.SaveNotes(new(cells,reason),CancellationToken.None);if(result.IsSuccess&&result.Value is{} saved){_sheet.Model.Apply(saved);_sheet.Paint();Notify($"Guardado: {cells.Length} celda(s) · cambios auditados. Selección conservada.");}else Notify(result.Error??"No se guardó el lote. Revise antes de reintentar.");}finally{_saving=false;}
    }
    private async Task Edit(int row,int col)
    {
        if(_sheet.Model.Rows.Count>row&&_sheet.Model.Rows[row].OnHold){Notify("JOB On Hold: consulte el motivo en Estado del JOB.");return;}
        if(_editing||_saving||_sheet.Model.Rows.Count==0)return;
        var column=_sheet.Model.Columns[col];var item=_sheet.Model.Rows[row];
        if(!CanEdit||!column.Editable||item.ExecutionId is null){Notify("La celda está protegida o no tiene permiso de edición.");return;}
        var value=Field("Valor de "+column.Title,500,column.Key=="Responsible"?150:2000);value.Text=_sheet.Model.Value(row,col);value.AcceptsReturn=false;value.TextWrapping=column.Key=="Observation"?TextWrapping.Wrap:TextWrapping.NoWrap;value.Height=column.Key=="Observation"?85:35;
        var reason=Field("Motivo del cambio",500,1000);reason.Text="Edición individual desde Consulta de Jobs";bool next=false;
        var dialog=new ContentDialog{XamlRoot=XamlRoot,Title=$"Editar {column.Title}",Content=PlanningUi.Stack(PlanningUi.Text(item.Job+" · "+item.OriginalTest,12,true),value,reason),PrimaryButtonText="Guardar",CloseButtonText="Cancelar",DefaultButton=ContentDialogButton.Primary};
        value.AddHandler(KeyDownEvent,new KeyEventHandler((_,e)=>{if(e.Key==VirtualKey.Escape){dialog.Hide();e.Handled=true;}else if(e.Key==VirtualKey.Enter||e.Key==VirtualKey.Tab){next=e.Key==VirtualKey.Tab;dialog.Tag=true;dialog.Hide();e.Handled=true;}}),true);
        dialog.Opened+=(_,_)=>{value.Focus(FocusState.Programmatic);value.SelectAll();};
        _editing=true;try
        {
            var result=await dialog.ShowAsync();if(result==ContentDialogResult.Primary||dialog.Tag is true)
            {var before=_sheet.Model.Value(row,col);if(value.Text!=before)await Save([new(item.ExecutionId.Value,item.NoteVersion,column.Key,value.Text)],reason.Text);}
        }
        finally{_editing=false;_sheet.FocusTable();}
        if(next){int c=col+1,r=row;while(r<_sheet.Model.Rows.Count){if(c>=_sheet.Model.Columns.Count){c=0;r++;}if(r>=_sheet.Model.Rows.Count)break;if(_sheet.Model.Columns[c].Editable){_sheet.Select(r,c);await Edit(r,c);break;}c++;}}
    }
    private async Task Export()
    {
        try{var path=await PlanningUi.PickFile(XamlRoot,true);if(path is null)return;var result=await _api.Export(Query(),CancellationToken.None);if(!result.IsSuccess||result.Value is null){Notify(result.Error??"No se pudo exportar.");return;}await File.WriteAllBytesAsync(path,result.Value);Notify("Exportación guardada.");}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or FormatException){Notify(e.Message);}
    }
    public void Cancel(){_load?.Cancel();_load?.Dispose();_load=null;}
    public void Clear(){Cancel();_multiple?.Close();_sheet.Model.Rows.Clear();foreach(var label in _counters)label.Text="—";_message.Text="";}
    public void Dispose()=>Clear();
}
