using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Lims.Contracts.Planning;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Lims.Desktop.Planning;

/// <summary>Operational closure queue; digitization, schedule review and follow-up remain independent.</summary>
internal sealed partial class PlanningClosuresView : Grid,IDisposable
{
    private readonly PlanningApiClient _api;
    private readonly Func<Guid,Task> _dossier;
    private readonly Func<bool> _canReview;
    private readonly ObservableCollection<ReportClosure> _rows=[];
    private readonly ReportClosureGrid _table;
    private readonly ReportNativeTable<PlanningRow> _tests;
    private readonly TextBox _search=PlanningUi.Field("","",20000),_lab=PlanningUi.Field("Laboratorio"),_from=PlanningUi.Field("Desde · yyyy-MM-dd"),_until=PlanningUi.Field("Hasta · yyyy-MM-dd");
    private readonly ComboBox _scope=Combo(["Todos los cierres","Por atender","Historial de seguimiento"]),_state=Combo(["Todos los estados","Por atender","En seguimiento","Atendidos"]),_sort=Combo(["Última digitación","Vencimiento más próximo","JOB","Fecha de seguimiento"]),_display=Combo(["Agrupado","Tabla"]);
    private readonly TextBlock _status=PlanningUi.Text("",12,true),_notice=PlanningUi.Text("",12,true),_title=PlanningUi.Text("",16),_meta=PlanningUi.Text("",12,true);
    private readonly TextBlock _selection=PlanningUi.Text("Arrastre para seleccionar · Ctrl+C para copiar · Doble clic para abrir detalle",11,true);
    private readonly Border _detail;
    private readonly StackPanel _episodes=PlanningUi.Row();
    private readonly Button _previous,_next,_follow;
    private readonly ProgressRing _busy=new(){Width=18,Height=18};
    private readonly DispatcherTimer _delay=new(){Interval=TimeSpan.FromMilliseconds(300)},_sync=new(){Interval=TimeSpan.FromSeconds(15)};
    private CancellationTokenSource _request=new(),_detailRequest=new();
    private ReportClosureDetail? _opened;
    private ReportClosureFollowUp? _retry;
    private ReportClosureQuery? _loadedQuery;
    private ContentDialog? _editDialog;
    private long? _revision;
    private int _page=1,_total;
    private bool _loading,_saving,_editing,_clearing,_disposed;
    public event Func<Task>? Changed;
    public FrameworkElement HeaderActions {get;}
    private static ComboBox Combo(string[] values)=>new(){ItemsSource=values,SelectedIndex=0,Style=(Style)Application.Current.Resources["LimsCompactComboBoxStyle"],MinWidth=145};
    private static Button Button(string text,Func<Task> action,bool primary=false)
    {
        var button=PlanningUi.Button(text,action,primary);button.FontSize=12;button.Height=28;button.MinHeight=28;button.MinWidth=0;button.Padding=new(9,3,9,3);button.CornerRadius=new(4);
        return button;
    }
    private readonly TextBlock _count=PlanningUi.Text("— cierres",12,true);
    public PlanningClosuresView(PlanningApiClient api,Func<Guid,Task> dossier,Func<bool> canReview,bool actionsInHeader=false)
    {
        _api=api;_dossier=dossier;_canReview=canReview;RowSpacing=8;
        foreach(var height in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})RowDefinitions.Add(new(){Height=height});
        _search.Header=null;_search.PlaceholderText="Filtrar por JOB, prueba o cliente…";_search.MinWidth=170;_search.Height=28;_search.MinHeight=28;_search.FontSize=12;_search.Padding=new(9,3,9,3);
        _notice.Visibility=Visibility.Collapsed;
        AutomationProperties.SetName(_search,"Buscar cierres");AutomationProperties.SetName(_scope,"Bandeja de cierres");AutomationProperties.SetName(_state,"Estado del seguimiento de cierres");AutomationProperties.SetName(_sort,"Orden de cierres");
        var filters=Button("Más filtros",()=>Task.CompletedTask);var filterBody=PlanningUi.Stack(_scope,_state,_sort,_lab,_from,_until);filterBody.Width=280;
        var flyout=new Flyout{Content=filterBody,FlyoutPresenterStyle=ClosureFlyoutStyle()};filters.Flyout=flyout;
        filterBody.Children.Add(Button("Aplicar filtros",async()=>{if(!DatesValid())return;flyout.Hide();await Load();}));
        filterBody.Children.Add(Button("Limpiar filtros",async()=>{_lab.Text=_from.Text=_until.Text="";flyout.Hide();await Load();}));
        var export=ConsultationVisuals.Action("Exportar",LimsIconKind.Export,()=>Task.CompletedTask);var exports=new MenuFlyout();
        var jobsExport=new MenuFlyoutItem{Text="CSV por JOB"};jobsExport.Click+=async(_,_)=>await Export();exports.Items.Add(jobsExport);
        var testsExport=new MenuFlyoutItem{Text="CSV por prueba (vigentes)"};testsExport.Click+=async(_,_)=>await Export(true);exports.Items.Add(testsExport);exports.Opening+=(_,_)=>testsExport.IsEnabled=_scope.SelectedIndex!=2;export.Flyout=exports;
        var actions=PlanningUi.Row(ConsultationVisuals.Action("Actualizar",LimsIconKind.Refresh,()=>Load(),true),export);actions.Spacing=8;HeaderActions=actions;
        AutomationProperties.SetName(_display,"Presentación de cierres");
        _count.VerticalAlignment=VerticalAlignment.Center;_count.Margin=new(0,0,4,0);
        _display.Visibility=Visibility.Collapsed;
        var grouped=Button("Agrupado",()=>{_display.SelectedIndex=0;return Task.CompletedTask;});var flat=Button("Tabla",()=>{_display.SelectedIndex=1;return Task.CompletedTask;});
        foreach(var choice in new[]{grouped,flat}){choice.BorderThickness=new(0);choice.CornerRadius=new(3);choice.Padding=new(10,2,10,2);choice.Height=24;choice.MinHeight=24;}
        var switchRow=PlanningUi.Row(grouped,flat);switchRow.Spacing=1;
        var presentation=new Border{Child=switchRow,Padding=new(2),CornerRadius=new(4),BorderBrush=PlanningSheetGrid.Color("#2B3945"),BorderThickness=new(1),Background=PlanningSheetGrid.Color("#101820")};
        void StylePresentation(){foreach(var (choice,index) in new[]{(grouped,0),(flat,1)}){choice.Background=PlanningSheetGrid.Color(_display.SelectedIndex==index?"#38D89B":"#101820");choice.Foreground=PlanningSheetGrid.Color(_display.SelectedIndex==index?"#092318":"#A8B8C5");}}
        _display.SelectionChanged+=(_,_)=>StylePresentation();StylePresentation();
        var controls=PlanningUi.Row(_count,presentation,CalendarFilter(true),CalendarFilter(false),filters,_display);controls.Spacing=6;
        var toolbar=new Grid{ColumnSpacing=10,RowSpacing=6,Padding=new(8,6,8,6)};
        foreach(var width in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})toolbar.ColumnDefinitions.Add(new(){Width=width});
        toolbar.RowDefinitions.Add(new(){Height=GridLength.Auto});toolbar.RowDefinitions.Add(new(){Height=GridLength.Auto});toolbar.Children.Add(controls);Grid.SetColumn(_search,1);toolbar.Children.Add(_search);if(!actionsInHeader){Grid.SetColumn(actions,2);toolbar.Children.Add(actions);}
        toolbar.SizeChanged+=(_,_)=>{var narrow=toolbar.ActualWidth<1000;Grid.SetColumnSpan(controls,narrow?3:1);Grid.SetRow(_search,narrow?1:0);Grid.SetColumn(_search,narrow?0:1);Grid.SetColumnSpan(_search,actionsInHeader?(narrow?3:2):(narrow?2:1));Grid.SetRow(actions,narrow?1:0);};
        var tools=ConsultationVisuals.Surface(toolbar,new Thickness(4));
        ToolTipService.SetToolTip(tools,"Los cierres se agrupan por la fecha y hora de su última prueba. Arrastre para seleccionar celdas; doble clic o Enter para abrir el detalle.");
        var top=PlanningUi.Stack(tools,_notice);top.Spacing=4;
        PlanningUi.Place(this,top,0);
        _table=new();_table.OpenRequested+=async row=>await Open(row);PlanningUi.Place(this,_table,1);
        _tests=new("Pruebas actuales del JOB en cierre",[("Prueba",205),("Laboratorio",115),("Digitación",155),("Digitado por",150),("Chequeo del horario",150),("Chequeado por",150),("Prueba original",220)],
            t=>[t.PlanningTest,t.Laboratory??"Sin información",t.DigitizedAt is {} at?Time(at):t.DigitizationState,t.DigitizedBy??"",t.ScheduleReviewState??"Sin horario",t.ScheduleReviewedBy??"",t.OriginalTest??""]);
        _follow=Button("Registrar seguimiento",Edit,true);
        var copy=Button("Copiar resumen",()=>{if(_opened is {} d)Copy(Summary(d));return Task.CompletedTask;});
        var history=Button("Ver historial",ShowHistory);
        var close=Button("Cerrar detalle",()=>{CloseDetail();return Task.CompletedTask;});
        var detailLayout=PlanningUi.Rows(GridLength.Auto,GridLength.Auto,GridLength.Auto,new(1,GridUnitType.Star),GridLength.Auto);detailLayout.RowSpacing=6;
        PlanningUi.Place(detailLayout,PlanningUi.Split(_title,close),0);PlanningUi.Place(detailLayout,new ScrollViewer{Content=_meta,MaxHeight=65},1);PlanningUi.Place(detailLayout,new ScrollViewer{Content=_episodes,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled},2);PlanningUi.Place(detailLayout,_tests,3);PlanningUi.Place(detailLayout,PlanningUi.Row(_follow,copy,history),4);
        _detail=new(){Child=detailLayout,Padding=new(10),Height=300,Background=PlanningUi.Brush("LimsSurfaceBrush"),BorderBrush=PlanningUi.Brush("LimsBorderBrush"),BorderThickness=new(1),CornerRadius=new(6),Visibility=Visibility.Collapsed};
        PlanningUi.Place(this,_detail,2);
        _previous=Button("Anterior",async()=>await Load(_page-1));_next=Button("Siguiente",async()=>await Load(_page+1));
        var copySelection=Button("Copiar selección",()=>{_table.CopySelection();return Task.CompletedTask;});copySelection.IsEnabled=false;
        AutomationProperties.SetName(copySelection,"Copiar celdas seleccionadas de Cierres");
        _selection.TextWrapping=TextWrapping.NoWrap;_selection.TextTrimming=TextTrimming.CharacterEllipsis;
        _table.SelectionFeedback+=text=>{_selection.Text=text;copySelection.IsEnabled=_table.HasSelection;};
        var footerInfo=PlanningUi.Stack(_status,_selection);footerInfo.Spacing=2;
        PlanningUi.Place(this,PlanningUi.Split(PlanningUi.Row(_busy,footerInfo),PlanningUi.Row(copySelection,_previous,_next)),3);
        _search.TextChanged+=(_,_)=>{if(_clearing)return;_delay.Stop();_delay.Start();};
        _search.KeyDown+=async(_,e)=>{if(e.Key==VirtualKey.Enter){e.Handled=true;await Load();}};
        _delay.Tick+=async(_,_)=>{_delay.Stop();await Load();};
        foreach(var combo in new[]{_scope,_state,_sort})combo.SelectionChanged+=async(_,_)=>{if(IsLoaded&&!_clearing)await Load();};
        _display.SelectionChanged+=(_,_)=>_table.SetItems(_rows,_display.SelectedIndex==0);
        _sync.Tick+=async(_,_)=>await Synchronize();Loaded+=(_,_)=>{if(!_disposed)_sync.Start();};Unloaded+=(_,_)=>{_sync.Stop();_delay.Stop();if(!_disposed){_request.Cancel();_detailRequest.Cancel();}};
        SizeChanged+=(_,_)=>_detail.Height=Math.Min(340,Math.Max(230,ActualHeight*.49));
    }
    private static string Date(DateOnly? value)=>value?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??"Sin información";
    private static string Time(DateTimeOffset value)=>value.ToOffset(TimeSpan.FromHours(-6)).ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture);
    private void Notice(string? message){_notice.Text=message??"";_notice.Visibility=string.IsNullOrEmpty(message)?Visibility.Collapsed:Visibility.Visible;}
    private bool DatesValid()
    {
        foreach(var input in new[]{_from,_until})if(!string.IsNullOrWhiteSpace(input.Text)&&!DateOnly.TryParseExact(input.Text.Trim(),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _)){Notice("Escriba las fechas como yyyy-MM-dd.");return false;}
        if(DateOnly.TryParse(_from.Text,out var from)&&DateOnly.TryParse(_until.Text,out var until)&&from>until){Notice("La fecha Desde debe ser anterior o igual a Hasta.");return false;}return true;
    }
    private ReportClosureQuery Query(int page=1)=>new(_search.Text,_scope.SelectedIndex switch{1=>"Current",2=>"History",_=>"All"},_state.SelectedIndex switch{1=>"Pending",2=>"Seen",3=>"Archived",_=>"All"},_lab.Text,
        DateOnly.TryParse(_from.Text,out var from)?from:null,DateOnly.TryParse(_until.Text,out var until)?until:null,_sort.SelectedIndex switch{1=>"Due",2=>"Job",3=>"FollowUp",_=>"Latest"},page,100,Dates:Selection(_selectedDates),Times:Selection(_selectedTimes));
    public async Task Load(int page=1)
    {
        _delay.Stop();if(_disposed||_saving||_editing||!DatesValid())return;_request.Cancel();_request.Dispose();_request=new();var ct=_request.Token;
        _loading=true;_busy.IsActive=true;_previous.IsEnabled=_next.IsEnabled=false;
        try
        {
            var query=Query(Math.Max(1,page));var response=await _api.Closures(query with{Revision=page>1?_revision:null},ct);if(ct.IsCancellationRequested)return;
            if(!response.IsSuccess||response.Value is not {} result){Notice(response.StatusCode==404?"Cierres requiere actualizar la API y su migración en el servidor.":response.Error);return;}
            if(result.Items.Count==0&&page>1){_loading=false;await Load(Math.Max(1,(result.Total+99)/100));return;}
            CloseDetail();_rows.Clear();foreach(var row in result.Items)_rows.Add(row);_page=result.Page;_total=result.Total;_revision=result.Revision;_loadedQuery=query;
            if(page==1)_facets=result.Facets;_table.SetItems(_rows,_display.SelectedIndex==0);UpdateCalendarLabels();
            _count.Text=$"{result.Total:N0} cierres";
            _status.Text=$"{result.Total:N0} JOBs · Página {_page:N0} de {Math.Max(1,(_total+99)/100):N0}";Notice(result.Total==0?"No hay cierres que coincidan con esta búsqueda.":null);
        }
        catch(OperationCanceledException){}
        catch(Exception e){Notice(e.Message);}
        finally{if(!ct.IsCancellationRequested){_loading=false;_busy.IsActive=false;_previous.IsEnabled=_page>1;_next.IsEnabled=_page*100<_total;}}
    }
    private void CloseDetail(){_detailRequest.Cancel();_opened=null;_detail.Visibility=Visibility.Collapsed;_tests.SetItems(null);_episodes.Children.Clear();}
    private async Task Open(ReportClosure row)
    {
        if(_disposed||_editing||_saving)return;_detailRequest.Cancel();_detailRequest.Dispose();_detailRequest=new();var ct=_detailRequest.Token;
        try
        {
            var response=await _api.ClosureDetail(row.Job,row.Fingerprint,ct);if(ct.IsCancellationRequested)return;
            if(!response.IsSuccess||response.Value is not {} d){Notice(response.Error);return;}_opened=d;_detail.Visibility=Visibility.Visible;_title.Text=d.Closure.Job+" · "+d.Closure.StateText;
            _meta.Text=$"Última prueba: {d.Closure.LastTest} · {d.Closure.Reviewed}/{d.Closure.Required} horarios chequeados · Seguimiento: {Date(d.Closure.FollowUpOn)}"+(d.Closure.Current?"":" · Este cierre cambió. Se muestran las pruebas actuales del JOB.")+$"\nResponsable: {d.Closure.Responsible??"Sin asignar"} · Notas: {d.Closure.Notes??"Sin notas"}";
            _tests.SetItems(d.Tests);_follow.Visibility=_canReview()?Visibility.Visible:Visibility.Collapsed;_follow.IsEnabled=d.Closure.Current;
            _episodes.Children.Clear();for(var i=0;i<d.Closure.EpisodeIds.Length;i++){var id=d.Closure.EpisodeIds[i];_episodes.Children.Add(Button(d.Closure.EpisodeIds.Length==1?"Abrir expediente":$"Abrir expediente {i+1}",()=>_dossier(id)));}
        }
        catch(OperationCanceledException){}
        catch(Exception e){Notice(e.Message);}
    }
    private async Task Edit()
    {
        if(_saving||_editing||!_canReview()||_opened is not {} detail||!detail.Closure.Current)return;_editing=true;_delay.Stop();
        var c=detail.Closure;var state=Combo(["Por atender","En seguimiento","Atendido"]);state.SelectedIndex=c.State switch{"Seen"=>1,"Archived"=>2,_=>0};
        var responsible=PlanningUi.Field("Responsable",c.Responsible,max:200);var notes=PlanningUi.Field("Notas",c.Notes,max:2000);notes.AcceptsReturn=true;notes.Height=90;
        var follow=PlanningUi.Field("Próximo seguimiento · yyyy-MM-dd",c.FollowUpOn?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),10);var reason=PlanningUi.Field("Motivo obligatorio",max:1000);var error=PlanningUi.Text("",12,true);
        var content=PlanningUi.Stack(PlanningUi.Text($"{c.Required:N0} pruebas digitadas · {c.Reviewed:N0} con horario chequeado. Atendido registra la atención de esta bandeja.",12,true),state,responsible,follow,notes,reason,error);content.Width=440;
        var dialog=new ContentDialog{XamlRoot=XamlRoot,Style=(Style)Application.Current.Resources["LimsEditorDialogStyle"],Title="Seguimiento · "+c.Job,Content=content,PrimaryButtonText="Guardar seguimiento",CloseButtonText="Cancelar",DefaultButton=ContentDialogButton.Close};
        _editDialog=dialog;var saved=false;
        dialog.PrimaryButtonClick+=async(sender,e)=>
        {
            if(string.IsNullOrWhiteSpace(reason.Text)){error.Text="Escriba el motivo.";e.Cancel=true;return;}
            if(!string.IsNullOrWhiteSpace(follow.Text)&&!DateOnly.TryParseExact(follow.Text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _)){error.Text="Revise la fecha de seguimiento.";e.Cancel=true;return;}
            var deferral=e.GetDeferral();_saving=true;dialog.IsPrimaryButtonEnabled=dialog.IsSecondaryButtonEnabled=false;
            try
            {
                var request=new ReportClosureFollowUp(Guid.Empty,c.Job,c.Fingerprint,c.FollowUpVersion,state.SelectedIndex switch{1=>"Seen",2=>"Archived",_=>"Pending"},responsible.Text,notes.Text,DateOnly.TryParse(follow.Text,out var date)?date:null,reason.Text);
                if(_retry is null||(_retry with{RequestId=Guid.Empty})!=request)_retry=request with{RequestId=Guid.NewGuid()};
                var response=await _api.SaveClosureFollowUp(_retry,_request.Token);
                if(!response.IsSuccess||response.Value is null){error.Text=response.Error??"No se pudo guardar. Puede reintentar.";e.Cancel=true;return;}
                _retry=null;saved=true;
            }
            catch(OperationCanceledException){e.Cancel=true;}
            catch(Exception ex){error.Text=ex.Message;e.Cancel=true;}
            finally{_saving=false;dialog.IsPrimaryButtonEnabled=true;deferral.Complete();}
        };
        dialog.Closing+=(_,e)=>{if(_saving&&!_clearing)e.Cancel=true;};
        try
        {
            await dialog.ShowAsync();if(!saved||_loadedQuery is null||_disposed)return;_editing=false;
            if(Changed is {} changed)await changed();await Load(_page);Notice("Seguimiento guardado. Puede consultarlo en el historial y en el expediente.");
        }
        catch(OperationCanceledException){}
        catch(Exception e){Notice(e.Message);}
        finally{_saving=false;_editing=false;_editDialog=null;}
    }
    private static string Summary(ReportClosureDetail d)=>$"JOB: {d.Closure.Job}\nCliente: {d.Closure.Buyer??"Sin información"}\nLaboratorios: {d.Closure.Laboratories}\nPruebas digitadas: {d.Closure.Digitized}/{d.Closure.Required}\nHorarios chequeados: {d.Closure.Reviewed}/{d.Closure.Required}\nÚltima digitación: {Time(d.Closure.LastDigitizedAt)}\nSeguimiento: {d.Closure.StateText}\nResponsable: {d.Closure.Responsible??"Sin asignar"}\nPróxima atención: {Date(d.Closure.FollowUpOn)}\nNotas: {d.Closure.Notes}\nVigencia: {(d.Closure.Current?"Vigente":"Cambió: revisar")}\nSeguimiento interno LIMS. Estado oficial: consultar expediente.";
    private static void Copy(string value){var package=new DataPackage();package.SetText(value);Clipboard.SetContent(package);}
    private async Task ShowHistory()
    {
        if(_opened is not {} d||_editing)return;_editing=true;
        try
        {
            var text=string.Join("\n\n",d.History.DistinctBy(h=>h.Details).Select(h=>
            {using var json=JsonDocument.Parse(h.Details);var after=json.RootElement.GetProperty("after");return $"{Time(h.At)} · {h.ActorName}\n{h.Reason}\nEstado: {after.GetProperty("stateText").GetString()} · Responsable: {after.GetProperty("responsible").GetString()}\nNotas: {after.GetProperty("notes").GetString()}";}));
            var dialog=new ContentDialog{XamlRoot=XamlRoot,Style=(Style)Application.Current.Resources["LimsEditorDialogStyle"],Title="Historial · "+d.Closure.Job,Content=new ScrollViewer{Content=PlanningUi.Text(string.IsNullOrEmpty(text)?"Todavía no hay seguimiento registrado.":text,13,true),MaxHeight=420,Width=480},CloseButtonText="Cerrar"};await dialog.ShowAsync();
        }
        finally{_editing=false;}
    }
    private async Task Export(bool tests=false)
    {
        if(_loading||_loadedQuery is null||_editing)return;_editing=true;
        try
        {
            if(Query(_page)!=_loadedQuery){Notice("Actualice la bandeja para exportar los filtros actuales.");return;}
            var result=await _api.ExportClosures(_loadedQuery with{Page=1,Revision=_revision,IncludeTests=tests},_request.Token);if(!result.IsSuccess||result.Value is null){Notice(result.Error);return;}
            var path=await PlanningUi.PickFile(XamlRoot,true,tests?"Cierres por prueba":"Cierres");if(path is null)return;await System.IO.File.WriteAllBytesAsync(path,result.Value);Notice($"Exportación completa de {_total:N0} JOBs con los filtros de la bandeja.");
        }
        catch(OperationCanceledException){}
        catch(Exception e){Notice(e.Message);}
        finally{_editing=false;}
    }
    private async Task Synchronize()
    {
        if(_disposed||_loading||_saving||_editing||!IsLoaded||Visibility!=Visibility.Visible||_loadedQuery is null)return;
        try
        {var ct=_request.Token;var response=await _api.Closures(_loadedQuery with{Page=1,PageSize=1,Revision=null},ct);if(ct.IsCancellationRequested)return;
         if(!response.IsSuccess){Notice(response.Error);return;}if(response.Value?.Revision==_revision)return;
         if(_opened is not null||_table.HasSelection)Notice("Hay cambios en Cierres. Actualice para revisar; se conservan el detalle y la selección.");else await Load(_page);}
        catch(OperationCanceledException){}
    }
    public void Clear(){if(_disposed)return;_clearing=true;_delay.Stop();_sync.Stop();_request.Cancel();_editDialog?.Hide();CloseDetail();_rows.Clear();_search.Text=_lab.Text=_from.Text=_until.Text="";_scope.SelectedIndex=_state.SelectedIndex=_sort.SelectedIndex=_display.SelectedIndex=0;_selectedDates=_selectedTimes=null;_facets=[];_table.SetItems(_rows,true);UpdateCalendarLabels();_loadedQuery=null;_retry=null;_revision=null;_status.Text="";Notice(null);_clearing=false;}
    public void Dispose(){if(_disposed)return;Clear();_disposed=true;_request.Dispose();_detailRequest.Dispose();}
}
