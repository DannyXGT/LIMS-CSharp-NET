using System.Globalization;
using Lims.Contracts.Planning;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Planning;

public sealed partial class PlanningImportsView
{
    private string? _summary,_report,_supplement;
    private bool _importingSide2;
    private int _step;
    private bool _updatingFlow;
    private readonly Grid _flowBody=new(){RowSpacing=8};
    private readonly ScrollViewer _flowScroll=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=650};
    private readonly Grid _steps=new(){ColumnSpacing=12};
    private readonly ComboBox _window=new(){Header="Corte operativo",ItemsSource=new[]{"Primera carga","Mediodía · 12:00","Última carga · 17:50"},SelectedIndex=0,MinWidth=200};
    private readonly CalendarDatePicker _day=new(){Header="Día operativo",Date=DateTimeOffset.Now,DateFormat="{year.full}-{month.integer(2)}-{day.integer(2)}",MinWidth=170};
    private readonly CalendarDatePicker _targetInDate=new(){Header="Ingreso de pruebas · vacío: más reciente",DateFormat="{year.full}-{month.integer(2)}-{day.integer(2)}",MinWidth=250};
    private readonly TextBlock _summaryName=PlanningUi.Text("Selecciona el archivo Summary XLSX",13);
    private readonly TextBlock _reportName=PlanningUi.Text("Selecciona el archivo Report List XLSX",13);
    private readonly TextBlock _summaryCoverage=PlanningUi.Text("Pendiente de selección",12,true);
    private readonly TextBlock _reportCoverage=PlanningUi.Text("Pendiente de selección",12,true);
    private readonly TextBlock _side2Name=PlanningUi.Text("Sin archivo seleccionado",13);
    private readonly TextBlock _side2Coverage=PlanningUi.Text("Pendiente de selección",12,true);
    private Button _importSide2=null!;
    private readonly ListView _fileIssues=new(){MaxHeight=85,SelectionMode=ListViewSelectionMode.None};
    private readonly TextBlock _validationSummary=PlanningUi.Text("",12,true);
    private Button _validate=null!,_toConfirmation=null!;
    private void BuildFlow()
    {
        _window.Style=(Style)Application.Current.Resources["LimsCompactComboBoxStyle"];
        AutomationProperties.SetName(_day,"Día operativo");AutomationProperties.SetName(_window,"Corte operativo");
        foreach(var height in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto})_flowBody.RowDefinitions.Add(new(){Height=height});
        _steps.Visibility=Visibility.Collapsed;
        _validate=PlanningUi.Button("Importar Side 1",async()=>{_importingSide2=false;await Preview();},true,()=>!_busy&&_summary is not null&&_report is not null);
        LimsAction.SetIcon(_validate,LimsIconKind.Import);AutomationProperties.SetName(_validate,"Importar Side 1");
        _toConfirmation=Action("Revisa antes de agregar",LimsIconKind.ChevronRight,ReviewBeforeImport);
        _confirm=PlanningUi.Button("Confirmar lote revisado",Confirm,true,()=>!_busy&&_step==3&&_preview is{Status:"Preview"} p&&!p.Issues.Any(i=>i.Blocking));
        AutomationProperties.SetName(_confirm,"Confirmar lote revisado");LimsAction.SetIcon(_confirm,LimsIconKind.Save);
        var clear=Action("Limpiar",LimsIconKind.Cancel,()=>{BeginImport();return Task.CompletedTask;});
        var side1=PlanningUi.Stack(PlanningUi.Text("Side 1",20),PlanningUi.Text("Selecciona Report List y Summary. Revisa nuevos JOBs, emergencias y equivalencias antes de agregar.",13,true),
            FileCard("Report List · obligatorio",_reportName,_reportCoverage,()=>Pick(false)),FileCard("Summary · obligatorio",_summaryName,_summaryCoverage,()=>Pick(true)));
        var formActions=PlanningUi.Row(clear,_validate,_toConfirmation,_confirm);formActions.HorizontalAlignment=HorizontalAlignment.Right;
        var options=PlanningUi.Row(_window,_day,_targetInDate);options.Spacing=10;
        side1.Children.Add(new ScrollViewer{Content=options,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled});
        side1.Children.Add(ResponsiveSplit(PlanningUi.Text("La revisión se abrirá antes de guardar.",12,true),formActions));
        PlanningUi.Place(_flowBody,Surface(side1),0);
        _importSide2=PlanningUi.Button("Importar Side 2",async()=>{_importingSide2=true;await Preview();},true,()=>!_busy&&_supplement is not null);
        LimsAction.SetIcon(_importSide2,LimsIconKind.Import);AutomationProperties.SetName(_importSide2,"Importar Side 2");
        _importSide2.HorizontalAlignment=HorizontalAlignment.Right;
        var side2=PlanningUi.Stack(PlanningUi.Text("Side 2",20),PlanningUi.Text("Importa el archivo de planificación con fecha, JOB, prueba y laboratorio.",13,true),
            FileCard("Archivo de planificación · obligatorio",_side2Name,_side2Coverage,PickSupplement),_importSide2);
        PlanningUi.Place(_flowBody,Surface(side2),1);
        var validation=PlanningUi.Stack(_validationSummary,_fileIssues);validation.Spacing=2;PlanningUi.Place(_flowBody,validation,4);
        _fileIssues.ItemTemplate=TableTemplate([("File",95),("Code",180),("Message",740)]);
        _flowScroll.Content=_flowBody;_flow.Content=_flowScroll;_flowBody.Margin=new(0,0,0,4);_flowBody.MaxWidth=1080;SetStep(0);
        _window.SelectionChanged+=(_,_)=>InvalidateCut();_day.DateChanged+=(_,_)=>InvalidateCut();
        _targetInDate.DateChanged+=(_,_)=>InvalidateCut();
    }
    private static Border FileCard(string label,TextBlock filename,TextBlock coverage,Func<Task> pick)
    {
        filename.MaxLines=1;filename.TextTrimming=TextTrimming.CharacterEllipsis;
        var button=Action("Seleccionar archivo",LimsIconKind.Import,pick);AutomationProperties.SetName(button,"Seleccionar archivo "+label.Split('·')[0].Trim());var title=PlanningUi.Text(label,14);title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
        var stack=PlanningUi.Stack(title,filename,coverage);stack.Spacing=2;button.VerticalAlignment=VerticalAlignment.Center;
        return Surface(Split(stack,button),new Thickness(12,8,12,8));
    }
    private void InvalidateCut()
    {if(_updatingFlow||_busy||_preview?.Status!="Preview")return;InvalidatePreview();Message("El corte cambió. Vuelve a validar los archivos antes de confirmar.");}
    private void InvalidatePreview()
    {
        _preview=null;SetStep(0);_differencePanel.Visibility=Visibility.Collapsed;_changes.ItemsSource=null;
        _flow.Header="Nueva importación";_fileIssues.ItemsSource=null;_validationSummary.Text="";Message("");
    }
    private void BeginImport()
    {
        if(_busy)return;_preview=null;_summary=_report=_supplement=null;_updatingFlow=true;
        _importingSide2=false;
        _day.Date=DateTimeOffset.Now;_targetInDate.Date=null;_window.SelectedIndex=0;_updatingFlow=false;
        _summaryName.Text="Selecciona el archivo Summary XLSX";_reportName.Text="Selecciona el archivo Report List XLSX";
        _summaryCoverage.Text=_reportCoverage.Text="Pendiente de selección";_validationSummary.Text="";_fileIssues.ItemsSource=null;
        _side2Name.Text="Sin archivo seleccionado";_side2Coverage.Text="Pendiente de selección";
        _changes.ItemsSource=null;_differenceCount.Text="";_differencePanel.Visibility=Visibility.Collapsed;
        _flow.Header="Nueva importación";_flow.IsExpanded=true;_resultHost.Content=null;_previewTab.IsEnabled=true;_tabs.SelectedIndex=0;SetStep(0);Message("");
    }
    private void SetStep(int step)
    {
        _step=step;_steps.Children.Clear();_steps.ColumnDefinitions.Clear();var labels=new[]{"Archivos","Validación","Conciliación","Confirmación"};
        for(var i=0;i<labels.Length;i++)
        {
            _steps.ColumnDefinitions.Add(new());var complete=step>i;var active=step==i;var caption=PlanningUi.Text((complete?"✓ ":(i+1).ToString(CultureInfo.InvariantCulture)+"  ")+labels[i],13,!active&&!complete);
            if(active||complete)caption.Foreground=PlanningUi.Brush("LimsAccentBrush");if(active)caption.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
            var border=new Border{Child=caption,Padding=new(4,4,4,7),BorderThickness=new(0,0,0,active?2:1),BorderBrush=PlanningUi.Brush(active?"LimsAccentBrush":"LimsBorderBrush")};
            AutomationProperties.SetName(border,labels[i]+(complete?" completada":active?" activa":" pendiente"));Grid.SetColumn(border,i);_steps.Children.Add(border);
        }
        _toConfirmation.Visibility=step==2&&_preview is{Status:"Preview"} p&&!p.Issues.Any(i=>i.Blocking)?Visibility.Visible:Visibility.Collapsed;
        _confirm.Visibility=step==3?Visibility.Visible:Visibility.Collapsed;_confirm.IsEnabled=step==3&&!_busy;
        _validate.Visibility=Visibility.Visible;
        UpdateImportActions();
    }
    private void UpdateImportActions()
    {
        _validate.IsEnabled=!_busy&&_summary is not null&&_report is not null;
        if(_importSide2 is not null)_importSide2.IsEnabled=!_busy&&_supplement is not null;
    }
    private async Task Pick(bool summary)
    {
        if(_busy)return;
        try
        {
            var path=await PlanningUi.PickFile(XamlRoot);if(path is null)return;
            if(summary){_summary=path;_summaryName.Text=Path.GetFileName(path);_summaryCoverage.Text="Seleccionado · pendiente de validar";ToolTipService.SetToolTip(_summaryName,path);}
            else{_report=path;_reportName.Text=Path.GetFileName(path);_reportCoverage.Text="Seleccionado · pendiente de validar";ToolTipService.SetToolTip(_reportName,path);}
            InvalidatePreview();
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){Message(e.Message);}
    }
    private async Task Preview()
    {
        if(_busy)return;
        if(_importingSide2?_supplement is null:_summary is null||_report is null){Message(_importingSide2?"Selecciona el archivo de planificación de Side 2.":"Selecciona Summary y Report List para importar Side 1.");return;}
        if(_day.Date is not{} selected){Message("Selecciona el día operativo.");return;}
        _preview=null;SetStep(1);Busy(true,"Validando estructura y conciliando los archivos reales…");
        try
        {
            foreach(var path in (_importingSide2?new[]{_supplement}:new[]{_summary,_report}).OfType<string>())if(new FileInfo(path).Length>5*1024*1024){Message("Máximo 5 MiB por archivo.");SetStep(0);return;}
            var result=await RequestPreview(selected);
            if(!result.IsSuccess||result.Value is not{} preview){Message(result.Error??"No se pudo validar.");SetStep(0);return;}
            Show(preview);Message(preview.Status=="Applied"?"Este lote ya está confirmado; se conserva su identidad y no se duplica.":preview.Issues.Any(i=>i.Blocking)?"Hay errores de importación. Corrige los archivos antes de confirmar.":"Archivos validados. Revisa las diferencias antes de confirmar.");
            if(preview.Status=="Applied")await Summary(preview.Id);else await ReviewBeforeImport();
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){Message(e.Message);SetStep(0);}
        finally{Busy(false);}
    }
    private void UpdateFiles(PlanningImportPreview preview)
    {
        foreach(var file in preview.Files)
        {
            var name=_importingSide2?_side2Name:file.File=="Summary"?_summaryName:_reportName;var coverage=_importingSide2?_side2Coverage:file.File=="Summary"?_summaryCoverage:_reportCoverage;
            name.Text=file.Name;ToolTipService.SetToolTip(name,file.Name+"\nSHA256: "+file.Sha256);
            var range=file.Earliest is null?"Cobertura temporal no informada":$"{file.Earliest:yyyy-MM-dd} – {file.Latest:yyyy-MM-dd}";
            coverage.Text=$"{file.Rows:N0} filas · {range} · {(preview.Issues.Any(i=>i.File==file.File&&i.Blocking)?"Con errores":"Estructura válida")}";ToolTipService.SetToolTip(coverage,file.CoverageBasis);
        }
        var errors=preview.Issues.Count(i=>i.Blocking);var notices=preview.Issues.Count-errors;
        _validationSummary.Text=$"Validación: {errors} errores estructurales · {notices} avisos de fuente o cobertura. Los avisos no son incidencias operativas.";
        _fileIssues.ItemsSource=preview.Issues;
    }
    private async Task Confirm()
    {
        await ReviewBeforeImport();
    }
    private async Task<PlanningImportInspection?> ReloadInspection()
    {
        if((_importingSide2?_supplement is null:_summary is null||_report is null)||_day.Date is not{} day)return null;
        var refreshed=await RequestPreview(day);
        if(refreshed.Value is not{} p){Message(refreshed.Error??"No se pudo recalcular.");return null;}_preview=p;Show(p);
        return (await _api.InspectImport(p.Id,CancellationToken.None)).Value;
    }
    private async Task ReviewBeforeImport()
    {
        if(_preview is not{} preview)return;
        var result=await _api.InspectImport(preview.Id,CancellationToken.None);
        if(result.Value is not{} data){Message(result.Error??"No se pudo abrir la revisión.");return;}
        TrackImportWindow(new PlanningImportReviewWindow(_api,data,_permissions().Contains(PlanningPermissions.Equivalences,StringComparer.Ordinal),ReloadInspection,ConfirmReviewed,_open));
    }
    private ImportCut CurrentCut(DateTimeOffset day)=>new(_window.SelectedIndex switch{1=>"Noon",2=>"Last",_=>"First"},
        DateOnly.FromDateTime(day.DateTime),"HtmlReact",_targetInDate.Date is{} target?DateOnly.FromDateTime(target.DateTime):null);
    private async Task<PlanningCall<PlanningImportPreview>> RequestPreview(DateTimeOffset day)
    {
        if(_importingSide2&&_supplement is{} file)return await _api.PreviewSupplement(Path.GetFileName(file),await File.ReadAllBytesAsync(file),DateOnly.FromDateTime(day.DateTime),CancellationToken.None);
        return await _api.Preview(Path.GetFileName(_summary!),await File.ReadAllBytesAsync(_summary!),Path.GetFileName(_report!),await File.ReadAllBytesAsync(_report!),CurrentCut(day),CancellationToken.None);
    }
    private async Task PickSupplement()
    {
        if(_busy)return;
        try
        {
            var path=await PlanningUi.PickFile(XamlRoot);if(path is null)return;
            _supplement=path;InvalidatePreview();
            _side2Name.Text=Path.GetFileName(path);_side2Coverage.Text="Seleccionado · pendiente de importar";
            _fileIssues.ItemsSource=null;Message("La carga complementaria conserva el origen manual y sus fechas quedan protegidas.");
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){Message(e.Message);}
    }
    private async Task<bool> ConfirmReviewed(PlanningImportInspection inspection)
    {
        var preview=inspection.Preview;if(_busy||preview.Status!="Preview"||preview.Issues.Any(i=>i.Blocking))return false;
        Busy(true,"Confirmando el lote y actualizando las bandejas…");
        try
        {
            var result=await _api.Confirm(preview.Id,CancellationToken.None);
            if(!result.IsSuccess){_preview=null;SetStep(0);Message((result.Error??"No se pudo confirmar.")+" Consulta el historial y vuelve a validar.");return false;}
            Show(preview with{Status="Applied"});await Changed();await LoadHistory(CancellationToken.None);Message("Lote confirmado. Consulta de Jobs y expediente actualizados; procedencia conservada.");await Summary(preview.Id);return true;
        }
        finally{Busy(false);}
    }
}
