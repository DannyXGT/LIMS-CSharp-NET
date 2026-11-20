using System.Globalization;
using Lims.Contracts.Planning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Lims.Desktop.Planning;

public sealed class PlanningReportListView : UserControl,IDisposable
{
    private readonly PlanningApiClient _api;
    private readonly Func<IReadOnlyList<string>> _permissions;
    private readonly Func<Guid,Task> _dossier;
    private readonly Func<bool> _administrator;
    private readonly TextBox _search=PlanningUi.Field("",max:20000),_scan=PlanningUi.Field("",max:200);
    private readonly TextBox _test=PlanningUi.Field("Prueba de Planificación"),_lab=PlanningUi.Field("Laboratorio"),_service=PlanningUi.Field("Servicio"),_buyer=PlanningUi.Field("Cliente / Buyer");
    private readonly CheckBox _fixedClock=new(){Content="Fijar fecha/hora operativa"};
    private readonly TextBox _clockDate=PlanningUi.Field("Fecha · yyyy-MM-dd",DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),10);
    private readonly TextBox _clockTime=PlanningUi.Field("Hora · HH:mm",DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).ToString("HH:mm",CultureInfo.InvariantCulture),5);
    private readonly ComboBox _view=new(){ItemsSource=new[]{"Pendientes","Chequeados","Activas","Archivadas","Todas"},SelectedIndex=0,MinWidth=145,Style=(Style)Application.Current.Resources["LimsCompactComboBoxStyle"]};
    private readonly ReportListGrid _grid=new();
    private readonly TextBlock _status=PlanningUi.Text("",12,true),_result=PlanningUi.Text("",12),_connection=PlanningUi.Text("Conectando…",12,true),_scanner=PlanningUi.Text("Escáner: comprobando…",12,true),_selection=PlanningUi.Text("",12,true);
    private readonly ProgressRing _progress=new(){Width=18,Height=18,IsActive=false};
    private readonly Button _pause,_mark,_correct;
    private readonly StackPanel _scanRow,_intro;
    private CancellationTokenSource _request=new();
    private ReportListRows? _rows;
    private string _sort="Default",_verified="";
    private long? _block;private DateOnly? _date;
    private bool _descending,_mutating,_paused=true,_scannerAvailable,_onlyTable;
    public event Func<Task>? Changed;
    public PlanningReportListView(PlanningApiClient api,Func<Guid,Task> dossier,Func<IReadOnlyList<string>> permissions,Func<bool> administrator)
    {
        _api=api;_permissions=permissions;_dossier=dossier;_administrator=administrator;
        _search.PlaceholderText="Buscar JOB, prueba o cliente · varios JOBs separados por ;";_search.MinWidth=200;
        AutomationProperties.SetName(_search,"Buscar JOB en Report List");AutomationProperties.SetName(_view,"Vista");
        _scan.PlaceholderText="Código AR · Enter para registrar";AutomationProperties.SetName(_scan,"Código AR de Report List");
        _search.KeyDown+=async(_,e)=>{if(e.Key==VirtualKey.Enter){e.Handled=true;await Load();}};
        _scan.KeyDown+=async(_,e)=>{if(e.Key==VirtualKey.Enter){e.Handled=true;await Scan();}};
        _view.SelectionChanged+=async(_,_)=>{if(IsLoaded)await Load();};
        _grid.CanCheck=item=>!_mutating&&Has(item.Execution.Complete?PlanningPermissions.ReportReview:PlanningPermissions.ReportMark)&&(item.CheckMode=="manual"||_administrator());
        _grid.CanKeep=()=>!_mutating&&Has(PlanningPermissions.ReportReview);
        _grid.CheckRequested+=item=>Check(!item.Execution.Complete,[item]);_grid.KeepRequested+=Keep;_grid.OpenRequested+=item=>_dossier(item.Execution.EpisodeId);
        _grid.SortRequested+=async key=>{_descending=_sort==key&&!_descending;_sort=key;_grid.SetSort(key,_descending);await Load();};
        _grid.FilterRequested+=ColumnFilter;_grid.SelectionChanged+=UpdateSelection;
        _intro=PlanningUi.Stack(PlanningUi.Text("Report List",23),PlanningUi.Text("Verificación de pruebas y seguimiento de marcados",12,true));_intro.Spacing=1;
        var only=Compact("Solo tabla",()=>{_onlyTable=!_onlyTable;_intro.Visibility=_onlyTable?Visibility.Collapsed:Visibility.Visible;return Task.CompletedTask;});
        only.Click+=(_,_)=>only.Content=_onlyTable?"Mostrar encabezado":"Solo tabla";
        var indicators=PlanningUi.Row(_connection,_scanner,only);indicators.Spacing=14;indicators.VerticalAlignment=VerticalAlignment.Center;
        var header=ConsultationVisuals.Split(_intro,indicators);
        var find=Compact("Buscar",Load);AutomationProperties.SetName(find,"Buscar Report List");
        var refresh=Compact("Refrescar",Load);AutomationProperties.SetName(refresh,"Actualizar Report List");
        var filters=Compact("Filtros",()=>Task.CompletedTask);AutomationProperties.SetName(filters,"Filtros de Report List");
        var clock=PlanningUi.Stack(_fixedClock,_clockDate,_clockTime);clock.Spacing=6;
        var filterPanel=PlanningUi.Stack(_test,_lab,_service,_buyer,new Expander{Header="Fecha y hora operativas",Content=clock});filterPanel.Width=290;
        var filterFlyout=new Flyout{Content=new ScrollViewer{Content=filterPanel,MaxHeight=520}};filters.Flyout=filterFlyout;
        filterPanel.Children.Add(Compact("Aplicar filtros",async()=>{if(!OperationalTime(out _))return;filterFlyout.Hide();await Load();}));
        filterPanel.Children.Add(Compact("Limpiar filtros",async()=>{_test.Text=_lab.Text=_service.Text=_buyer.Text=_verified="";_block=null;_date=null;filterFlyout.Hide();await Load();}));
        _pause=Compact("Reanudar escáner",()=>{_paused=!_paused;UpdateScanner();if(!_paused)_scan.Focus(FocusState.Programmatic);return Task.CompletedTask;});_pause.IsEnabled=false;
        var tools=PlanningUi.Row(_view,filters,refresh,_pause);tools.Spacing=6;
        var searchBar=new Grid{ColumnSpacing=8};searchBar.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});searchBar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});searchBar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        searchBar.Children.Add(_search);Grid.SetColumn(find,1);searchBar.Children.Add(find);Grid.SetColumn(tools,2);searchBar.Children.Add(tools);
        _mark=Compact("Marcar seleccionadas",()=>Check(true,_grid.Selected),true);_correct=Compact("Corregir marcado",()=>Check(false,_grid.Selected));
        var dossierButton=Compact("Expediente",()=>_grid.FirstSelected is {} item?_dossier(item.Execution.EpisodeId):Task.CompletedTask);
        var copy=Compact("Copiar",()=>{var data=new DataPackage();data.SetText("Bloque\tJOB\tPrueba\tLaboratorio\tCliente\tMantener\tVerificado por\tFecha\tHora\tCHK\tNombre técnico\tEjecución\n"+string.Join('\n',_grid.Selected.Select(x=>$"{x.BlockNumber}\t{x.Execution.Job}\t{x.Execution.PlanningTest}\t{x.Execution.Laboratory}\t{x.Execution.Buyer}\t{x.KeepLabel}\t{x.Execution.DigitizedBy}\t{x.CheckedDateText}\t{x.CheckedTimeText}\t{x.Execution.Complete}\t{x.Execution.OriginalTest}\t{x.Execution.ExecutionId}")));Clipboard.SetContent(data);return Task.CompletedTask;});
        var actions=PlanningUi.Row(_mark,_correct,dossierButton,copy);actions.Spacing=6;
        var more=new MenuFlyout();var selectItem=new MenuFlyoutItem{Text="Seleccionar cargadas"};selectItem.Click+=(_,_)=>_grid.SelectAll();more.Items.Add(selectItem);
        var clearItem=new MenuFlyoutItem{Text="Limpiar selección"};clearItem.Click+=(_,_)=>_grid.ClearSelection();more.Items.Add(clearItem);
        var selectionButton=Compact("Selección",()=>Task.CompletedTask);selectionButton.Flyout=more;
        var actionRow=ConsultationVisuals.Split(PlanningUi.Row(selectionButton,_selection),actions);
        _scanRow=PlanningUi.Row(PlanningUi.Text("Captura AR",12,true),_scan);_scanRow.Visibility=Visibility.Collapsed;_result.Visibility=Visibility.Collapsed;
        var top=PlanningUi.Stack(header,searchBar,actionRow,_scanRow,_result);top.Spacing=8;
        var table=new Border{Child=_grid,Background=PlanningUi.Brush("LimsSurfaceBrush"),BorderBrush=PlanningUi.Brush("LimsBorderBrush"),BorderThickness=new(1),CornerRadius=new(6)};
        var layout=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star),GridLength.Auto);layout.RowSpacing=8;
        PlanningUi.Place(layout,top,0);PlanningUi.Place(layout,table,1);PlanningUi.Place(layout,PlanningUi.Row(_progress,_status),2);Content=layout;
        Unloaded+=(_,_)=>_request.Cancel();
    }
    private static Button Compact(string title,Func<Task> action,bool primary=false)
    {var b=PlanningUi.Button(title,action,primary);b.FontSize=12;b.MinHeight=34;b.Padding=new(10,5,10,5);return b;}
    private bool Has(string permission)=>_permissions().Contains(permission,StringComparer.Ordinal);
    private void Feedback(string? message){_result.Text=message??"";_result.Visibility=string.IsNullOrEmpty(message)?Visibility.Collapsed:Visibility.Visible;}
    private void UpdateSelection()
    {
        var count=_grid.Selected.Count;_selection.Text=$"{count:N0} seleccionadas";
        _mark.Visibility=Has(PlanningPermissions.ReportMark)?Visibility.Visible:Visibility.Collapsed;_correct.Visibility=Has(PlanningPermissions.ReportReview)?Visibility.Visible:Visibility.Collapsed;
        _mark.IsEnabled=_correct.IsEnabled=!_mutating&&count is >0 and <=500;
    }
    private void UpdateScanner()
    {
        var allowed=Has(PlanningPermissions.ReportMark);_pause.Visibility=allowed?Visibility.Visible:Visibility.Collapsed;
        _pause.IsEnabled=allowed&&_scannerAvailable&&!_mutating;_pause.Content=_paused?"Reanudar escáner":"Pausar escáner";
        _scanner.Text=!_scannerAvailable?"Escáner: sin tracking en esta base":_paused?"Escáner: captura pausada":"Escáner: captura AR lista";
        _scanRow.Visibility=allowed&&_scannerAvailable&&!_paused?Visibility.Visible:Visibility.Collapsed;
    }
    public async Task Load()
    {
        _request.Cancel();_request.Dispose();_request=new();var ct=_request.Token;
        var query=new ReportListQuery(_search.Text,_view.SelectedIndex switch{1=>"Checked",2=>"Active",3=>"Archived",4=>"All",_=>"Pending"},_lab.Text,_test.Text,_service.Text,_buyer.Text,PageSize:500,Sort:_sort,Descending:_descending,VerifiedBy:_verified,Block:_block,CheckedDate:_date);
        var rows=new ReportListRows(async(page,revision,cancellation)=>
        {
            var response=await _api.ReportList(query with{Page=page,Revision=revision},cancellation);
            if(!response.IsSuccess||response.Value is null)throw new InvalidOperationException(response.Error??"No se pudo consultar Report List.");return response.Value;
        },ct);
        _rows=rows;_progress.IsActive=true;_connection.Text="Consultando API…";_status.Text="Cargando ejecuciones…";_grid.SetSource(null);
        rows.Updated+=error=>
        {
            if(!ReferenceEquals(_rows,rows))return;_progress.IsActive=false;
            if(error is not null){_connection.Text="Consulta interrumpida";_status.Text=error.Message;return;}
            _connection.Text="● API conectada";_status.Text=$"{rows.Count:N0} de {rows.Total:N0} ejecuciones · Carga al desplazar · "+(_fixedClock.IsChecked==true?$"Fecha operativa: {_clockDate.Text} {_clockTime.Text} (Guatemala)":"Reloj del servidor · Guatemala (UTC−6)");
            _scannerAvailable=rows.Capabilities?.ScannerAvailable==true;UpdateScanner();
        };
        _grid.SetSort(_sort,_descending);_grid.SetFilters(new[]{string.IsNullOrEmpty(_search.Text)?"":"Job",string.IsNullOrEmpty(_test.Text)?"":"Test",string.IsNullOrEmpty(_lab.Text)?"":"Laboratory",string.IsNullOrEmpty(_buyer.Text)?"":"Buyer",string.IsNullOrEmpty(_verified)?"":"VerifiedBy",_block is null?"":"Block",_date is null?"":"Date"});
        await rows.LoadNext();if(!ct.IsCancellationRequested){_grid.SetSource(rows);UpdateSelection();}
    }
    private void ColumnFilter(string key,FrameworkElement anchor)
    {
        var value=key switch{"Job"=>_search.Text,"Test"=>_test.Text,"Laboratory"=>_lab.Text,"Buyer"=>_buyer.Text,"VerifiedBy"=>_verified,"Block"=>_block?.ToString(CultureInfo.InvariantCulture),"Date"=>_date?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),_=>""};
        var field=PlanningUi.Field(key=="Date"?"Fecha de marcado · yyyy-MM-dd":"Contiene",value);field.Width=255;
        var error=PlanningUi.Text("",12,true);var panel=PlanningUi.Stack(field,error);var flyout=new Flyout{Content=panel};
        async Task Apply()
        {
            if(key=="Block"&&!string.IsNullOrWhiteSpace(field.Text)&&(!long.TryParse(field.Text,out var n)||n<1)){error.Text="Escriba el número de bloque.";return;}
            if(key=="Date"&&!string.IsNullOrWhiteSpace(field.Text)&&!DateOnly.TryParseExact(field.Text,"yyyy-MM-dd",out _)){error.Text="Use yyyy-MM-dd.";return;}
            switch(key){case "Job":_search.Text=field.Text;break;case "Test":_test.Text=field.Text;break;case "Laboratory":_lab.Text=field.Text;break;case "Buyer":_buyer.Text=field.Text;break;case "VerifiedBy":_verified=field.Text;break;case "Block":_block=long.TryParse(field.Text,out var b)?b:null;break;case "Date":_date=DateOnly.TryParseExact(field.Text,"yyyy-MM-dd",out var d)?d:null;break;}
            flyout.Hide();await Load();
        }
        panel.Children.Add(Compact("Aplicar columna",Apply));panel.Children.Add(Compact("Quitar filtro",async()=>{field.Text="";await Apply();}));
        field.KeyDown+=async(_,e)=>{if(e.Key==VirtualKey.Enter){e.Handled=true;await Apply();}};flyout.ShowAt(anchor);
    }
    private async Task Check(bool check,IReadOnlyList<ReportListItem> selection)
    {
        if(_mutating||!OperationalTime(out var checkedAt))return;var selected=selection.Where(x=>x.Execution.Complete!=check).ToArray();
        if(selected.Length is <1 or >500){Feedback("Seleccione de 1 a 500 ejecuciones con un cambio pendiente.");return;}
        if(!Has(check?PlanningPermissions.ReportMark:PlanningPermissions.ReportReview)){Feedback("No tiene permiso para esta acción.");return;}
        _mutating=true;_grid.SetBusy(true);UpdateSelection();
        try
        {
            var reason=PlanningUi.Field("Motivo obligatorio",max:1000);var modeOverride=new CheckBox{Content="Excepción administrativa del modo de marcado"};
            var detail=PlanningUi.Text(string.Join('\n',selected.Select(x=>$"{x.Execution.Job} · {x.Execution.PlanningTest} · {x.Execution.OriginalTest}")),12);
            var content=PlanningUi.Stack(new ScrollViewer{Content=detail,MaxHeight=230},reason);if(_administrator())content.Children.Add(modeOverride);
            var dialog=new ContentDialog{XamlRoot=XamlRoot,Title=check?$"Marcar {selected.Length} ejecuciones":"Corregir y restaurar ejecuciones",Content=content,PrimaryButtonText="Guardar marcado",CloseButtonText="Cancelar",DefaultButton=ContentDialogButton.Close};
            dialog.PrimaryButtonClick+=(_,e)=>{if(string.IsNullOrWhiteSpace(reason.Text)){reason.PlaceholderText="Escriba el motivo";e.Cancel=true;reason.Focus(FocusState.Programmatic);}};
            if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
            var response=await _api.CheckReport(new(selected.Select(x=>new ReportExecutionVersion(x.Execution.ExecutionId!.Value,x.Version)).ToArray(),check,reason.Text,modeOverride.IsChecked==true,checkedAt),!check||modeOverride.IsChecked==true,_request.Token);
            if(!response.IsSuccess){Feedback(response.Error);return;}
            Feedback(response.Value?.ReadyJobs.Count>0?"Última prueba pendiente completada: "+string.Join(", ",response.Value.ReadyJobs):"Marcado guardado en el historial.");
            await Load();if(Changed is {} changed)await changed();
        }
        finally{_mutating=false;_grid.SetBusy(false);UpdateSelection();UpdateScanner();}
    }
    private async Task Keep(ReportListItem item)
    {
        if(_mutating||!Has(PlanningPermissions.ReportReview))return;_mutating=true;_grid.SetBusy(true);
        try
        {
            var keep=new CheckBox{Content="Mantener en programación",IsChecked=item.KeepInPlanning};var reason=PlanningUi.Field("Motivo obligatorio",max:1000);
            var dialog=new ContentDialog{XamlRoot=XamlRoot,Title=item.Execution.Job+" · "+item.Execution.PlanningTest,Content=PlanningUi.Stack(keep,reason),PrimaryButtonText="Guardar permanencia",CloseButtonText="Cancelar"};
            dialog.PrimaryButtonClick+=(_,e)=>e.Cancel=string.IsNullOrWhiteSpace(reason.Text);if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
            var response=await _api.KeepReport(new(new(item.Execution.ExecutionId!.Value,item.Version),keep.IsChecked==true,reason.Text),_request.Token);
            Feedback(response.IsSuccess?"Permanencia guardada.":response.Error);await Load();if(response.IsSuccess&&Changed is {} changed)await changed();
        }
        finally{_mutating=false;_grid.SetBusy(false);UpdateSelection();}
    }
    private async Task Scan()
    {
        if(_mutating||_paused||!_scannerAvailable||!Has(PlanningPermissions.ReportMark)||!OperationalTime(out var checkedAt))return;_mutating=true;_grid.SetBusy(true);UpdateScanner();
        try
        {
            var response=await _api.ScanReport(new(_scan.Text,"Marcado mediante código AR en Report List",checkedAt),_request.Token);Feedback(response.IsSuccess?"Código verificado y ejecución marcada.":response.Error);
            if(response.IsSuccess){_scan.Text="";await Load();if(Changed is {} changed)await changed();}
        }
        finally{_mutating=false;_grid.SetBusy(false);UpdateScanner();}
    }
    private bool OperationalTime(out DateTimeOffset? value)
    {
        value=null;if(_fixedClock.IsChecked!=true)return true;
        if(!DateTime.TryParseExact(_clockDate.Text+" "+_clockTime.Text,"yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var local))
        {Feedback("Revise la fecha y hora operativas en Filtros: yyyy-MM-dd y HH:mm.");return false;}
        value=new DateTimeOffset(DateTime.SpecifyKind(local,DateTimeKind.Unspecified),TimeSpan.FromHours(-6));return true;
    }
    public void Clear(){_request.Cancel();_grid.SetSource(null);_rows=null;_search.Text="";_scan.Text="";Feedback(null);}
    public void Dispose(){Clear();_request.Dispose();}
}
