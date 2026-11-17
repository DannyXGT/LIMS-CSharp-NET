using System.Globalization;
using Lims.Contracts.Planning;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Lims.Desktop.Planning;

public sealed class PlanningJobsView : Grid,IDisposable
{
    private readonly PlanningApiClient _api;private readonly Func<Guid,Task> _open;private readonly Func<IReadOnlyList<string>> _permissions;
    private readonly PlanningSheetGrid _sheet=new();
    private readonly TextBox _search=Field("Buscar JOB",330,20000),_test=Field("Filtrar prueba",200),_lab=Field("Laboratorio",155),_service=Field("Servicio",155),_buyer=Field("Buyer / marca",155),_vendor=Field("Vendor",155),_equivalence=Field("Equivalencia",155);
    private readonly TextBox _from=Field("Vence desde · yyyy-MM-dd",165,10),_until=Field("Vence hasta · yyyy-MM-dd",165,10),_inFrom=Field("Ingreso desde · yyyy-MM-dd",165,10),_inUntil=Field("Ingreso hasta · yyyy-MM-dd",165,10);
    private readonly ComboBox _mode=new(){ItemsSource=new[]{"Contiene","Exacto","Prefijo"},SelectedIndex=0,Width=110,MinHeight=32};
    private readonly ComboBox _state=new(){Header="Digitación",ItemsSource=new[]{"Todos","Sin digitación informada","Digitadas","Incidencias","NO PROGRAMAR","Sin información"},SelectedIndex=0,Width=200};
    private readonly ComboBox _incident=new(){Header="Incidencia",ItemsSource=new[]{"Todas","Pruebas faltantes","Vendor faltante","Equivalencia faltante"},SelectedIndex=0,Width=200};
    private readonly CheckBox _overdue=new(){Content="Vencidos sin digitación informada"};
    private readonly TextBlock _message=PlanningUi.Text("",11,true),_selection=PlanningUi.Text("Selecciona una celda",11,true),_pageLabel=PlanningUi.Text("",11,true);
    private readonly TextBlock[] _counters=[PlanningUi.Text("— JOBs",12),PlanningUi.Text("— pruebas",12),PlanningUi.Text("— pendientes",12),PlanningUi.Text("— digitadas",12)];
    private readonly FrameworkElement _tools,_counterRow;
    private CancellationTokenSource? _load;private int _page=1,_total;private string _sort="job";private bool _descending,_solo,_editing,_saving;
    public PlanningJobsView(PlanningApiClient api,Func<Guid,Task> open,Func<IReadOnlyList<string>>? permissions=null)
    {
        _api=api;_open=open;_permissions=permissions??(()=>[]);RowSpacing=8;
        foreach(var height in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})RowDefinitions.Add(new(){Height=height});
        var title=PlanningUi.Text("Consulta de Jobs",22);title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;var subtitle=PlanningUi.Text("Buscá un JOB, revisá su estado y trabajá directamente con la tabla.",12,true);
        var solo=Button("Solo tabla",()=>{_solo=!_solo;if(_tools is{} tools&&_counterRow is{} counters)tools.Visibility=counters.Visibility=_solo?Visibility.Collapsed:Visibility.Visible;return Task.CompletedTask;});
        solo.Click+=(_,_)=>solo.Content=_solo?"Recuperar herramientas":"Solo tabla";
        var heading=new Grid();heading.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var actions=PlanningUi.Row(solo,Button("Estado del JOB",Open,true));actions.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(actions,1);var captions=PlanningUi.Stack(title,subtitle);captions.Spacing=3;heading.Children.Add(captions);heading.Children.Add(actions);PlanningUi.Place(this,heading,0);
        _search.PlaceholderText="Buscar JOB o pegar varios separados por coma…";_test.PlaceholderText="Filtrar prueba o equivalencia…";
        _search.AcceptsReturn=true;_search.Height=34;
        _search.TextChanged+=(_,_)=>
        {
            if(_search.Text.IndexOfAny(['\r','\n','\t'])<0)return;
            _search.Text=_search.Text.Replace("\r\n",",",StringComparison.Ordinal).Replace('\r',',').Replace('\n',',').Replace('\t',',').Trim(',');_search.SelectionStart=_search.Text.Length;
        };
        AutomationProperties.SetName(_mode,"Modo de búsqueda JOB");
        var filter=Button("Filtros",()=>Task.CompletedTask);filter.Flyout=new Flyout{Content=PlanningUi.Stack(PlanningUi.Row(_lab,_service,_equivalence),PlanningUi.Row(_buyer,_vendor),PlanningUi.Row(_from,_until),PlanningUi.Row(_inFrom,_inUntil),PlanningUi.Row(_state,_incident),_overdue,Button("Aplicar filtros",async()=>{filter.Flyout.Hide();_page=1;await Load();},true))};
        var columns=Button("Columnas",()=>Task.CompletedTask);var choices=new StackPanel{Spacing=4};
        foreach(var column in PlanningSheetModel.AvailableColumns)
        {
            var check=new CheckBox{Content=column.Title,IsChecked=column.InitiallyVisible,IsEnabled=column.Key!="Job"};
            check.Click+=(_,_)=>{if(check.IsChecked==true&&!_sheet.Model.Columns.Contains(column))_sheet.Model.Columns.Add(column);if(check.IsChecked!=true)_sheet.Model.Columns.Remove(column);_sheet.Model.Columns.Sort((a,b)=>PlanningSheetModel.AvailableColumns.ToList().IndexOf(a).CompareTo(PlanningSheetModel.AvailableColumns.ToList().IndexOf(b)));_sheet.RebuildColumns();};choices.Children.Add(check);
        }
        columns.Flyout=new Flyout{Content=new ScrollViewer{Content=choices,MaxHeight=500}};
        var toolsSurface=PlanningUi.Surface(Horizontal(PlanningUi.Row(_search,_mode,_test,Button("Buscar",Search,true),Button("Limpiar",ClearFilters),Button("Pegar JOBs",PasteJobs),filter,columns)));toolsSurface.Padding=new Thickness(8);_tools=toolsSurface;
        PlanningUi.Place(this,_tools,1);
        var counter=new Grid();counter.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});counter.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var metrics=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};foreach(var label in _counters)metrics.Children.Add(new Border{Child=label,Padding=new Thickness(8,6,8,6),CornerRadius=new CornerRadius(6),Background=PlanningSheetGrid.Color("#12202E"),BorderBrush=PlanningSheetGrid.Color("#2B4154"),BorderThickness=new Thickness(1)});counter.Children.Add(metrics);
        var editing=PlanningUi.Row(Button("Copiar selección",Copy),Button("Pegar en celdas editables",Paste),Button("Exportar CSV",Export));Grid.SetColumn(editing,1);counter.Children.Add(editing);_counterRow=Horizontal(counter);PlanningUi.Place(this,_counterRow,2);
        var table=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star),GridLength.Auto);table.RowSpacing=0;
        var tableHeader=new Border{Background=PlanningSheetGrid.Color("#132434"),Padding=new Thickness(12,7,12,7),Child=PlanningUi.Row(PlanningUi.Text("RESULTADOS · vista tipo Excel",12),PlanningUi.Text("Clic y arrastre · Shift + flechas · Ctrl+C / Ctrl+V · doble clic para editar",11,true))};
        PlanningUi.Place(table,tableHeader,0);PlanningUi.Place(table,_sheet,1);
        var bottom=new Border{Background=PlanningSheetGrid.Color("#132434"),Padding=new Thickness(12,7,12,7),Child=PlanningUi.Row(_selection,PlanningUi.Text("Ctrl+C Copiar · Ctrl+V Pegar · Enter Estado del JOB · Ctrl+Espacio Columna",11,true))};PlanningUi.Place(table,bottom,2);
        var surface=PlanningUi.Surface(table);surface.Padding=new Thickness(0);PlanningUi.Place(this,surface,3);
        PlanningUi.Place(this,PlanningUi.Row(Button("Anterior",async()=>{if(_page>1){_page--;await Load();}}),_pageLabel,Button("Siguiente",async()=>{if(_page*100<_total){_page++;await Load();}}),_message),4);
        _sheet.SelectionChanged+=()=>{var r=_sheet.Model.Range;_selection.Text=$"Selección: {r.Bottom-r.Top+1} fila(s) × {r.Right-r.Left+1} columna(s) · inicio F{_sheet.Model.Active.Row+1} C{_sheet.Model.Active.Column+1}";};
        _sheet.CopyRequested+=Copy;_sheet.PasteRequested+=Paste;_sheet.OpenRequested+=Open;_sheet.EditRequested+=Edit;
        _sheet.SortRequested+=async key=>{_descending=_sort==SortKey(key)&&!_descending;_sort=SortKey(key);_page=1;await Load();};
        foreach(var field in new[]{_search,_test})field.AddHandler(KeyDownEvent,new KeyEventHandler(async(_,e)=>{if(e.Key==VirtualKey.Enter){e.Handled=true;await Search();}}),true);
    }
    private static TextBox Field(string name,int width,int max=512)
    {var field=PlanningUi.Field(name,max:max);field.Header=null;field.PlaceholderText=name;field.Width=width;field.MinHeight=32;AutomationProperties.SetName(field,name);return field;}
    private static Button Button(string name,Func<Task> action,bool primary=false){var button=PlanningUi.Button(name,action,primary);button.FontSize=12;button.Padding=new Thickness(10,5,10,5);button.MinHeight=32;return button;}
    private static ScrollViewer Horizontal(UIElement content)=>new ScrollViewer{Content=content,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalContentAlignment=HorizontalAlignment.Stretch};
    private static string SortKey(string key)=>key switch{"OriginalTest"=>"test","DigitizationState"=>"state",_=>char.ToLowerInvariant(key[0])+key[1..]};
    private Task Search(){_page=1;return Load();}
    private async Task ClearFilters()
    {foreach(var field in new[]{_search,_test,_lab,_service,_buyer,_vendor,_equivalence,_from,_until,_inFrom,_inUntil})field.Text="";_state.SelectedIndex=_incident.SelectedIndex=_mode.SelectedIndex=0;_overdue.IsChecked=false;_page=1;await Load();}
    private PlanningQuery Query()
    {
        static DateOnly? Date(TextBox field)=>string.IsNullOrWhiteSpace(field.Text)?null:DateOnly.TryParseExact(field.Text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)?date:throw new FormatException("Use fechas yyyy-MM-dd en los filtros.");
        return new(_search.Text,_test.Text,_lab.Text,_service.Text,_state.SelectedIndex switch{1=>"Pending",2=>"Digitado",3=>"Exceptions",4=>"NoProgramar",5=>"Unknown",_=>null},Date(_from),Date(_until),_sort,_descending,_page,100,_buyer.Text,_vendor.Text,_equivalence.Text,
            _incident.SelectedIndex switch{1=>"MissingTests",2=>"MissingVendor",3=>"MissingEquivalence",_=>null},_mode.SelectedIndex switch{1=>"Exact",2=>"Prefix",_=>"Contains"},Date(_inFrom),Date(_inUntil),_overdue.IsChecked==true);
    }
    public async Task Load()
    {
        if(_saving||_editing){Notify("Termine o cancele la edición antes de cambiar la consulta.");return;}
        Cancel();_load=new();var ct=_load.Token;_message.Text="Consultando…";
        try
        {
            var result=await _api.Jobs(Query(),ct);if(ct.IsCancellationRequested)return;
            if(!result.IsSuccess||result.Value is not{} page){_sheet.Model.Rows.Clear();Notify(result.Error??"No se pudo consultar.");return;}
            _sheet.Model.Rows.Clear();foreach(var row in page.Items)_sheet.Model.Rows.Add(row);_total=page.Total;_page=page.Page;
            _sheet.Select(0,0);_pageLabel.Text=$"Página {page.Page} · {page.Items.Count:N0} / {page.Total:N0} filas";
            if(page.Counts is{} c){_counters[0].Text=$"{c.Jobs:N0} JOBs";_counters[1].Text=$"{c.Executions:N0} pruebas";_counters[2].Text=$"{c.Pending:N0} pendientes";_counters[3].Text=$"{c.Digitized:N0} digitadas";}
            else{_counters[0].Text=$"{page.Total:N0} registros";foreach(var label in _counters.Skip(1))label.Text="—";}
            _message.Text=page.Total==0?"Sin resultados.":"Resultados completos filtrados en servidor · selección sobre la página cargada.";
        }
        catch(FormatException e){Notify(e.Message);}catch(OperationCanceledException)when(ct.IsCancellationRequested){ }
    }
    private Task Open()=>_sheet.Model.Rows.Count==0?Task.CompletedTask:_open(_sheet.Model.Rows[_sheet.Model.Active.Row].EpisodeId);
    private void Notify(string message){_message.Text=message;Motion.Enter(_message,y:2,milliseconds:120);}
    private static void ClipboardText(string text){var content=new DataPackage();content.SetText(text);Clipboard.SetContent(content);Clipboard.Flush();}
    private async Task Copy()
    {try{if(_sheet.Model.Rows.Count==0){Notify("Seleccione una celda.");return;}ClipboardText(_sheet.Model.Copy());Notify("Selección copiada al portapapeles de Windows.");}catch(Exception e)when(e is System.Runtime.InteropServices.COMException){Notify("El portapapeles está ocupado; vuelva a copiar.");}await Task.CompletedTask;}
    private async Task<string?> ReadClipboard()
    {try{var data=Clipboard.GetContent();return data.Contains(StandardDataFormats.Text)?await data.GetTextAsync():null;}catch(System.Runtime.InteropServices.COMException){Notify("El portapapeles no está disponible.");return null;}}
    private async Task PasteJobs()
    {
        var text=Field("Lista de JOBs",510,20000);text.AcceptsReturn=true;text.Height=130;text.Text=await ReadClipboard()??"";
        var dialog=new ContentDialog{XamlRoot=XamlRoot,Title="Buscar varios JOBs",Content=PlanningUi.Stack(PlanningUi.Text("Uno por línea o separados por coma, punto y coma o tabulaciones.",12,true),text),PrimaryButtonText="Buscar JOBs",CloseButtonText="Cancelar"};
        if(await dialog.ShowAsync()==ContentDialogResult.Primary){_search.Text=text.Text;await Search();}
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
    public void Clear(){Cancel();_sheet.Model.Rows.Clear();foreach(var label in _counters)label.Text="—";_message.Text="";}
    public void Dispose()=>Clear();
}
