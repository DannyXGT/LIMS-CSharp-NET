using System.Globalization;
using Lims.Contracts.Planning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Planning;

public sealed class PlanningImportsView : Grid, IDisposable
{
    private readonly PlanningApiClient _api;
    private readonly Func<Guid,Task> _open;
    private readonly Func<IReadOnlyList<string>> _permissions;
    private string? _summary,_report;
    private PlanningImportPreview? _preview;
    private readonly TextBlock _files=PlanningUi.Text("Selecciona ambos archivos XLSX.",13,true);
    private readonly TextBlock _status=PlanningUi.Text("",13,true);
    private readonly ComboBox _window=new(){Header="Corte",ItemsSource=new[]{"Primera carga","Mediodía · 12:00","Última carga · 17:50"},SelectedIndex=0,Width=210};
    private readonly TextBox _day=PlanningUi.Field("Día operativo · yyyy-MM-dd",DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Central America Standard Time").DateTime).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),10);
    private static readonly string[] Categories=["","SummaryWithoutReport","MissingTests","MissingVendor","MissingEquivalence","ManualManaged","ExecutionIdentity","BothFiles","ReportWithoutSummary","ExternalClosed","Episode"];
    private readonly ComboBox _category=new(){Header="Bandeja",ItemsSource=Categories.Select(c=>c.Length==0?"Todas":PlanningLabels.Category(c)).ToArray(),SelectedIndex=0,MinWidth=210};
    private readonly ListView _reviews=new(){SelectionMode=ListViewSelectionMode.Single};
    private readonly ListView _changes=new(){SelectionMode=ListViewSelectionMode.Single};
    private readonly ListView _history=new(){SelectionMode=ListViewSelectionMode.Single};
    private readonly Button _confirm;
    private IReadOnlyList<ImportReview> _allReviews=[];
    private CancellationTokenSource? _load;
    public PlanningImportsView(PlanningApiClient api,Func<Guid,Task> open,Func<IReadOnlyList<string>> permissions)
    {
        _api=api;_open=open;_permissions=permissions;RowSpacing=10;
        RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        _confirm=PlanningUi.Button("Confirmar lote revisado",Confirm,true,()=>_preview is {Status:"Preview"} p&&!p.Issues.Any(i=>i.Blocking));_confirm.IsEnabled=false;
        var instructions=PlanningUi.Text("Primera: Summary del día anterior; Report List ≈ -5/+3 meses. Mediodía: archivos del día. Última: Summary del día; Report List ≈ -3/+3 meses. La cobertura se comprueba con los campos disponibles.",12,true);
        PlanningUi.Place(this,PlanningUi.Surface(PlanningUi.Stack(instructions,PlanningUi.Row(_window,_day,PlanningUi.Button("Summary XLSX",async()=>await Pick(true)),PlanningUi.Button("Report List XLSX",async()=>await Pick(false))),_files,
            PlanningUi.Row(PlanningUi.Button("Validar y conciliar",Preview,true),_confirm,PlanningUi.Button("Bandejas persistidas",Load)),_status)),0);
        PlanningUi.Place(this,PlanningUi.Row(_category,PlanningUi.Button("Abrir expediente seleccionado",OpenReview),PlanningUi.Button("Conciliar fila ambigua",Resolve)),1);
        var pivot=new Pivot();pivot.Items.Add(new PivotItem{Header="Conciliación",Content=_reviews});pivot.Items.Add(new PivotItem{Header="Cambios y errores",Content=_changes});pivot.Items.Add(new PivotItem{Header="Historial de lotes · últimos 200",Content=_history});PlanningUi.Place(this,pivot,2);
        _reviews.ItemTemplate=ReviewTemplate();_changes.ItemTemplate=TextTemplate();
        _category.SelectionChanged+=(_,_)=>Filter();
    }
    private static DataTemplate TextTemplate()=>(DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><TextBlock Text='{Binding}' TextWrapping='Wrap' Margin='8' Foreground='{ThemeResource LimsTextPrimaryBrush}'/></DataTemplate>");
    private static DataTemplate ReviewTemplate()=>(DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Margin='8' Spacing='4'><StackPanel Orientation='Horizontal' Spacing='14'><TextBlock Text='{Binding CategoryLabel}' FontWeight='SemiBold'/><TextBlock Text='{Binding Job}'/><TextBlock Text='{Binding OriginalTest}'/></StackPanel><TextBlock Text='{Binding Description}' TextWrapping='Wrap' Foreground='{ThemeResource LimsTextSecondaryBrush}'/></StackPanel></DataTemplate>");
    private async Task Pick(bool summary)
    {
        try{var path=await PlanningUi.PickFile(XamlRoot);if(path is null)return;if(summary)_summary=path;else _report=path;_preview=null;_confirm.IsEnabled=false;_files.Text=$"Summary: {Path.GetFileName(_summary)??"sin seleccionar"} · Report List: {Path.GetFileName(_report)??"sin seleccionar"}";}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){_status.Text=e.Message;}
    }
    private async Task Preview()
    {
        _confirm.IsEnabled=false;_preview=null;
        if(_summary is null||_report is null){_status.Text="Selecciona Summary y Report List.";return;}
        if(!DateOnly.TryParseExact(_day.Text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day)){_status.Text="Usa fecha yyyy-MM-dd.";return;}
        foreach(var path in new[]{_summary,_report})if(new FileInfo(path).Length>5*1024*1024){_status.Text="Máximo 5 MiB por archivo.";return;}
        Cancel();_load=new();var ct=_load.Token;_status.Text="Validando archivos y preparando conciliación...";
        try
        {
            var result=await _api.Preview(Path.GetFileName(_summary),await File.ReadAllBytesAsync(_summary,ct),Path.GetFileName(_report),await File.ReadAllBytesAsync(_report,ct),new(_window.SelectedIndex switch{1=>"Noon",2=>"Last",_=>"First"},day),ct);
            if(ct.IsCancellationRequested)return;if(!result.IsSuccess||result.Value is null){_status.Text=result.Error??"No se pudo validar.";return;}
            Show(result.Value);
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){ }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){_status.Text=e.Message;}
    }
    private void Show(PlanningImportPreview preview)
    {
        _preview=preview;_allReviews=preview.Reviews.Select(r=>r with{BatchId=preview.Id}).ToArray();Filter();
        _status.Text=$"{PlanningLabels.State(preview.Status)} · {preview.NewJobs:N0} JOBs nuevos · {preview.NewTests:N0} pruebas nuevas · {preview.UpdatedJobs:N0} cambios WIP · {preview.LinkedManualTests:N0} vínculos a pruebas manuales.";
        _changes.ItemsSource=preview.Files.Select(f=>$"{f.File} · {f.Name} · {f.Rows:N0} filas · {f.Earliest:yyyy-MM-dd} / {f.Latest:yyyy-MM-dd} · {f.CoverageBasis} · SHA256 {f.Sha256}")
            .Concat(preview.Issues.Select(i=>$"{(i.Blocking?"ERROR":"Aviso")} · {i.File}:{i.Row} · {i.Code} · {i.Message}"))
            .Concat(preview.Changes.Select(c=>$"{c.Action} · {c.Entity} · {c.Job} · {c.OriginalTest}\nAntes: {c.Before}\nDespués: {c.After}")).ToArray();
        _confirm.IsEnabled=preview.Status=="Preview"&&!preview.Issues.Any(i=>i.Blocking);
    }
    private async Task Confirm()
    {
        if(_preview is not {Status:"Preview"} preview)return;
        if(!await PlanningUi.Confirm(XamlRoot,"Confirmar importación",$"Se guardarán {preview.NewJobs:N0} JOBs y {preview.NewTests:N0} pruebas nuevas, {preview.UpdatedJobs:N0} cambios WIP y {preview.LinkedManualTests:N0} vínculos manuales. Las excepciones sin resolver quedarán en revisión."))return;
        _confirm.IsEnabled=false;var result=await _api.Confirm(preview.Id,CancellationToken.None);
        _status.Text=result.IsSuccess?$"Lote confirmado · {result.Value?.NewJobs} JOBs · {result.Value?.NewTests} pruebas · {result.Value?.LinkedManualTests} vínculos manuales. Historial conservado.":result.Error??"No se pudo confirmar.";
        if(result.IsSuccess){_preview=preview with{Status="Applied"};await RefreshReviews();}
    }
    public async Task Load()
    {
        Cancel();_load=new();var ct=_load.Token;
        try
        {
            await RefreshReviews(ct);var result=await _api.Batches(ct);if(ct.IsCancellationRequested)return;
            if(!result.IsSuccess){_status.Text=result.Error??"No se pudo leer el historial.";return;}
            _history.Items.Clear();foreach(var batch in result.Value??[])
            {var button=PlanningUi.Button($"{batch.CreatedAt:g} · {PlanningLabels.State(batch.Status)} · {batch.SummaryName} / {batch.ReportName}",async()=>{var detail=await _api.Batch(batch.Id,CancellationToken.None);if(detail.IsSuccess&&detail.Value is {} p)Show(p);else _status.Text=detail.Error??"Lote no disponible.";});_history.Items.Add(button);}
            if(_preview is null)_status.Text="Bandejas calculadas con decisiones persistidas. Se muestran hasta 2000 incidencias; filtra Consulta para revisar cada expediente.";
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){ }
    }
    private async Task RefreshReviews(CancellationToken ct=default)
    {var result=await _api.Reviews(ct);if(ct.IsCancellationRequested)return;if(result.IsSuccess){_allReviews=result.Value??[];Filter();}else _status.Text=result.Error??"No se pudieron consultar las bandejas.";}
    private void Filter()=>_reviews.ItemsSource=_category.SelectedIndex<=0?_allReviews:_allReviews.Where(r=>r.Category==Categories[_category.SelectedIndex]).ToArray();
    private async Task OpenReview()
    {if(_reviews.SelectedItem is ImportReview{EpisodeId:{} id})await _open(id);else _status.Text="Seleccione una incidencia con episodio; las filas sin episodio requieren conciliación.";}
    private async Task Resolve()
    {
        if(!_permissions().Contains(PlanningPermissions.ManualTests,StringComparer.Ordinal)){_status.Text="Necesita permiso de conciliación manual.";return;}
        if(_reviews.SelectedItem is not ImportReview{SourceRow:>0,BatchId:{} batch} review||_preview?.Status=="Preview"){_status.Text="Seleccione una fila ambigua de un lote confirmado en las bandejas persistidas.";return;}
        var result=await _api.Jobs(new(Search:review.Job,PageSize:500),CancellationToken.None);if(!result.IsSuccess){_status.Text=result.Error??"No se pudieron consultar los episodios.";return;}
        var episodes=(result.Value?.Items??[]).Where(r=>r.Job==review.Job).DistinctBy(r=>r.EpisodeId).ToArray();
        if(episodes.Length==0){_status.Text="Primero cargue un Summary con WIP del JOB; no se inventa el episodio.";return;}
        var combo=new ComboBox{Header="Episodio de ingreso",ItemsSource=episodes.Select(e=>$"{e.Job} · {e.InDate:yyyy-MM-dd} · {e.EpisodeId}").ToArray(),SelectedIndex=0,MinWidth=500};
        var existing=PlanningUi.Field("UUID de ejecución existente (vacío para crear una nueva)",max:36);var reason=PlanningUi.Field("Motivo / verificación de identidad",max:1000);
        var dialog=new ContentDialog{XamlRoot=XamlRoot,Title="Conciliar identidad fuente",Content=PlanningUi.Stack(combo,existing,reason),PrimaryButtonText="Guardar conciliación",CloseButtonText="Cancelar",DefaultButton=ContentDialogButton.Close};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        var episode=episodes[combo.SelectedIndex];var dossier=await _api.Dossier(episode.EpisodeId,CancellationToken.None);if(!dossier.IsSuccess||dossier.Value is null){_status.Text=dossier.Error??"Expediente no disponible.";return;}
        Guid? execution=null;if(!string.IsNullOrWhiteSpace(existing.Text)){if(!Guid.TryParse(existing.Text,out var parsed)){_status.Text="UUID de ejecución inválido.";return;}execution=parsed;}
        var saved=await _api.Resolve(new(batch,review.SourceRow,episode.EpisodeId,execution,execution is null,dossier.Value.Version,reason.Text),CancellationToken.None);
        _status.Text=saved.IsSuccess?"Identidad conciliada y auditada.":saved.Error??"No se pudo conciliar.";if(saved.IsSuccess)await RefreshReviews();
    }
    public void Cancel(){_load?.Cancel();_load?.Dispose();_load=null;}
    public void Clear(){Cancel();_preview=null;_allReviews=[];_reviews.ItemsSource=null;_changes.ItemsSource=null;_history.Items.Clear();_status.Text="";_confirm.IsEnabled=false;_summary=_report=null;_files.Text="Selecciona ambos archivos XLSX.";}
    public void Dispose()=>Clear();
}
