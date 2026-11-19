using System.Globalization;
using Lims.Contracts.Planning;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Planning;

public sealed partial class PlanningImportsView
{
    private string? _summary,_report;
    private int _step;
    private bool _updatingFlow;
    private readonly Grid _flowBody=new(){RowSpacing=8};
    private readonly Grid _steps=new(){ColumnSpacing=12};
    private readonly ComboBox _window=new(){Header="Corte operativo",ItemsSource=new[]{"Primera carga","Mediodía · 12:00","Última carga · 17:50"},SelectedIndex=0,MinWidth=200};
    private readonly CalendarDatePicker _day=new(){Header="Día operativo",Date=DateTimeOffset.Now,DateFormat="{year.full}-{month.integer(2)}-{day.integer(2)}",MinWidth=170};
    private readonly TextBlock _summaryName=PlanningUi.Text("Selecciona el archivo Summary XLSX",13);
    private readonly TextBlock _reportName=PlanningUi.Text("Selecciona el archivo Report List XLSX",13);
    private readonly TextBlock _summaryCoverage=PlanningUi.Text("Pendiente de selección",12,true);
    private readonly TextBlock _reportCoverage=PlanningUi.Text("Pendiente de selección",12,true);
    private readonly ListView _fileIssues=new(){MaxHeight=85,SelectionMode=ListViewSelectionMode.None};
    private readonly TextBlock _validationSummary=PlanningUi.Text("",12,true);
    private Button _validate=null!,_toConfirmation=null!;
    private void BuildFlow()
    {
        _window.Style=(Style)Application.Current.Resources["LimsCompactComboBoxStyle"];
        AutomationProperties.SetName(_day,"Día operativo");AutomationProperties.SetName(_window,"Corte operativo");
        foreach(var height in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto})_flowBody.RowDefinitions.Add(new(){Height=height});
        PlanningUi.Place(_flowBody,_steps,0);
        var files=new Grid{ColumnSpacing=10};files.ColumnDefinitions.Add(new());files.ColumnDefinitions.Add(new());
        files.Children.Add(FileCard("Summary",_summaryName,_summaryCoverage,()=>Pick(true)));
        var report=FileCard("Report List",_reportName,_reportCoverage,()=>Pick(false));Grid.SetColumn(report,1);files.Children.Add(report);PlanningUi.Place(_flowBody,files,1);
        _validate=Action("Validar archivos",LimsIconKind.Check,Preview,true);
        _toConfirmation=Action("Revisa antes de agregar",LimsIconKind.ChevronRight,ReviewBeforeImport);
        _confirm=PlanningUi.Button("Confirmar lote revisado",Confirm,true,()=>!_busy&&_step==3&&_preview is{Status:"Preview"} p&&!p.Issues.Any(i=>i.Blocking));
        AutomationProperties.SetName(_confirm,"Confirmar lote revisado");LimsAction.SetIcon(_confirm,LimsIconKind.Save);
        var formActions=PlanningUi.Row(_validate,_toConfirmation,_confirm);formActions.Spacing=8;
        var options=PlanningUi.Row(_window,_day);options.Spacing=10;
        var controls=Split(options,formActions);PlanningUi.Place(_flowBody,controls,2);
        var validation=PlanningUi.Stack(_validationSummary,_fileIssues);validation.Spacing=2;PlanningUi.Place(_flowBody,validation,3);
        _fileIssues.ItemTemplate=TableTemplate([("File",95),("Code",180),("Message",740)]);
        _flow.Content=_flowBody;_flowBody.Margin=new(0,0,0,4);SetStep(0);
        _window.SelectionChanged+=(_,_)=>InvalidateCut();_day.DateChanged+=(_,_)=>InvalidateCut();
    }
    private static Border FileCard(string label,TextBlock filename,TextBlock coverage,Func<Task> pick)
    {
        filename.MaxLines=1;filename.TextTrimming=TextTrimming.CharacterEllipsis;
        var button=Action(label+" XLSX",LimsIconKind.Import,pick);var title=PlanningUi.Text(label,13);title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
        var stack=PlanningUi.Stack(Split(title,button),filename,coverage);stack.Spacing=3;return Surface(stack,new Thickness(10,6,10,8));
    }
    private void InvalidateCut()
    {if(_updatingFlow||_busy||_preview?.Status!="Preview")return;_preview=null;SetStep(0);Message("El corte cambió. Vuelve a validar los archivos antes de confirmar.");}
    private void BeginImport()
    {
        if(_busy)return;_preview=null;_summary=_report=null;_updatingFlow=true;
        _day.Date=DateTimeOffset.Now;_window.SelectedIndex=0;_updatingFlow=false;
        _summaryName.Text="Selecciona el archivo Summary XLSX";_reportName.Text="Selecciona el archivo Report List XLSX";
        _summaryCoverage.Text=_reportCoverage.Text="Pendiente de selección";_validationSummary.Text="";_fileIssues.ItemsSource=null;
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
        _validate.Visibility=step<2?Visibility.Visible:Visibility.Collapsed;
    }
    private async Task Pick(bool summary)
    {
        if(_busy)return;
        try
        {
            var path=await PlanningUi.PickFile(XamlRoot);if(path is null)return;
            if(summary){_summary=path;_summaryName.Text=Path.GetFileName(path);_summaryCoverage.Text="Seleccionado · pendiente de validar";ToolTipService.SetToolTip(_summaryName,path);}
            else{_report=path;_reportName.Text=Path.GetFileName(path);_reportCoverage.Text="Seleccionado · pendiente de validar";ToolTipService.SetToolTip(_reportName,path);}
            _preview=null;SetStep(0);_fileIssues.ItemsSource=null;_validationSummary.Text="";
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){Message(e.Message);}
    }
    private async Task Preview()
    {
        if(_busy)return;
        if(_summary is null||_report is null){Message("Selecciona Summary y Report List.");return;}
        if(_day.Date is not{} selected){Message("Selecciona el día operativo.");return;}
        _preview=null;SetStep(1);Busy(true,"Validando estructura y conciliando los archivos reales…");
        try
        {
            foreach(var path in new[]{_summary,_report})if(new FileInfo(path).Length>5*1024*1024){Message("Máximo 5 MiB por archivo.");SetStep(0);return;}
            var result=await _api.Preview(Path.GetFileName(_summary),await File.ReadAllBytesAsync(_summary),Path.GetFileName(_report),await File.ReadAllBytesAsync(_report),new(_window.SelectedIndex switch{1=>"Noon",2=>"Last",_=>"First"},DateOnly.FromDateTime(selected.DateTime)),CancellationToken.None);
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
            var name=file.File=="Summary"?_summaryName:_reportName;var coverage=file.File=="Summary"?_summaryCoverage:_reportCoverage;
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
        if(_summary is null||_report is null||_day.Date is not{} day)return null;
        var refreshed=await _api.Preview(Path.GetFileName(_summary),await File.ReadAllBytesAsync(_summary),Path.GetFileName(_report),await File.ReadAllBytesAsync(_report),new(_window.SelectedIndex switch{1=>"Noon",2=>"Last",_=>"First"},DateOnly.FromDateTime(day.DateTime)),CancellationToken.None);
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
