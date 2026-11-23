using System.Globalization;
using System.Text.Json;
using Lims.Contracts.Planning;
using Lims.DesignSystem.Controls;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Planning;

public sealed partial class PlanningImportsView
{
    private readonly Grid _reviewDetailHost=new(){ColumnSpacing=8};
    private readonly Border _reviewDetail=new(){Visibility=Visibility.Collapsed,Width=420,CornerRadius=new(8),Padding=new(14),BorderThickness=new(1)};
    private readonly StackPanel _detailContent=new(){Spacing=12};
    private readonly TextBlock _detailTitle=PlanningUi.Text("Detalle de incidencia",16);
    private CancellationTokenSource? _detailLoad;
    private JobDossier? _detailDossier;
    private bool _detailOpen;
    private string? _detailKey;
    private void BuildDetailHost()
    {
        _reviewDetail.Background=PlanningUi.Brush("LimsSurfaceBrush");_reviewDetail.BorderBrush=PlanningUi.Brush("LimsBorderBrush");
        var close=Action("Cerrar detalle",LimsIconKind.Cancel,()=>{CloseReviewDetail();_reviewCells.FocusTable();return Task.CompletedTask;});
        var grid=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star));
        PlanningUi.Place(grid,Split(_detailTitle,close),0);
        PlanningUi.Place(grid,new ScrollViewer{Content=_detailContent,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},1);
        _reviewDetail.Child=grid;AutomationProperties.SetName(_reviewDetail,"Detalle contextual de incidencia");
        Motion.ConfigureVisibility(_reviewDetail);
        _reviewDetail.KeyDown+=(_,e)=>{if(e.Key==Windows.System.VirtualKey.Escape){CloseReviewDetail();_reviewCells.FocusTable();e.Handled=true;}};
    }
    private void CloseReviewDetail()
    {
        _detailDelay.Stop();_detailLoad?.Cancel();_detailOpen=false;_detailKey=null;_reviewDetail.Visibility=Visibility.Collapsed;
    }
    private async Task ShowReviewDetail()
    {
        if(!_detailOpen||_reviewLoading)return;
        _detailLoad?.Cancel();_detailLoad?.Dispose();_detailLoad=new();var ct=_detailLoad.Token;
        var row=SelectedReview;_detailKey=row?.Key;_detailDossier=null;_detailContent.Children.Clear();
        _reviewDetail.Visibility=Visibility.Visible;
        if(Motion.Enabled)Motion.Enter(_reviewDetail,x:6,milliseconds:140);
        void Add(string title,string value)
        {
            var label=PlanningUi.Text(title,12,true);var text=PlanningUi.Text(value,13);text.IsTextSelectionEnabled=true;
            var block=PlanningUi.Stack(label,text);block.Spacing=3;_detailContent.Children.Add(block);
        }
        if(row is null){Add("Selección","Selecciona una sola fila para consultar sus fuentes e historial.");return;}
        _detailTitle.Text=row.Job;
        Add("Incidencia",PlanningLabels.Category(row.Category)+" · "+row.StateLabel);
        Add("Prueba",row.OriginalTest??"No informada para esta incidencia");
        Add("Motivo",row.Reason??"No informado");
        Add("Decisión",$"{row.DecidedBy??"Sin usuario de decisión informado"} · {row.DecidedAt?.ToOffset(TimeSpan.FromHours(-6)).ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture)??"Sin fecha de decisión informada"}");
        Add("Estado TIPS",row.OfficialStateText+" · independiente de la situación de revisión");
        if(row.EpisodeId is not{} episode)
        {
            Add("Fuentes","Sin episodio vinculado. Abre el lote original desde Acciones para examinar su procedencia.");return;
        }
        var loading=PlanningUi.Text("Consultando fuentes e historial…",12,true);_detailContent.Children.Add(loading);
        try
        {
            var result=await _api.Dossier(episode,ct);if(ct.IsCancellationRequested||_detailKey!=row.Key)return;
            _detailContent.Children.Remove(loading);
            if(result.Value is not{} dossier){Add("Consulta",result.Error??"Expediente no disponible");return;}
            _detailDossier=dossier;UpdateReviewActions();
            // Display exact persisted fields with their file, batch, sheet and row. Never substitute WIP for a source.
            var sources=dossier.Sources.Where(s=>s.BatchId==_reviewBatch).ToArray();
            if(sources.Length==0)sources=dossier.Sources.ToArray();
            var summary=sources.Where(s=>s.File=="Summary").OrderByDescending(s=>s.ImportedAt).ToArray();
            var report=sources.Where(s=>s.File=="Report List").OrderByDescending(s=>s.ImportedAt).ToArray();
            foreach(var (label,entries) in new[]{("Summary",summary),("Report List",report)})
            {
                if(entries.Length==0){Add(label,"Sin información de esta fuente en el expediente.");continue;}
                foreach(var source in entries)
                {
                    var body=SourceFields(source);
                    var expander=new Expander{Header=$"{label} · {source.Sheet} · fila {source.Row}",HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,
                        Content=PlanningUi.Text($"Lote {source.BatchId}\nImportado {source.ImportedAt.ToOffset(TimeSpan.FromHours(-6)):yyyy-MM-dd HH:mm}\n{body}",12,true)};
                    _detailContent.Children.Add(expander);
                }
            }
            var left=Fields(summary.FirstOrDefault());var right=Fields(report.FirstOrDefault());
            var differences=left.Keys.Intersect(right.Keys,StringComparer.OrdinalIgnoreCase).Where(k=>left[k]!=right[k]).Select(k=>$"{k}: Summary = {left[k]} · Report List = {right[k]}").ToArray();
            Add("Diferencias entre fuentes",summary.Length==0||report.Length==0?"Comparación no disponible: falta una fuente.":differences.Length==0?"Sin diferencias entre los campos con el mismo nombre; los demás campos se muestran en su fuente.":string.Join('\n',differences));
            var w=dossier.Wip;Add("WIP actual",$"Buyer: {w.Buyer??"No informado"}\nVendor: {w.Vendor??"No informado"}\nIngreso: {w.InDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??"No informado"}\nDue Date: {w.DueDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??"No informado"}");
            var manual=dossier.Tests.Where(t=>t.Origin=="Manual").Select(t=>$"{t.OriginalTest} · {t.Equivalence??"Sin equivalencia"} · {t.Laboratory??"Sin laboratorio"}\nEjecución {t.ExecutionId}").ToArray();
            Add("Pruebas manuales",manual.Length==0?"Sin pruebas manuales registradas.":string.Join('\n',manual));
            if(dossier.History.Count==0)Add("Historial","Sin intervenciones registradas.");
            foreach(var h in dossier.History.OrderByDescending(h=>h.At))
            {
                var text=$"{h.ActorName??h.ActorId.ToString(CultureInfo.InvariantCulture)} · {h.At.ToOffset(TimeSpan.FromHours(-6)):yyyy-MM-dd HH:mm}\n{h.Reason}\n{h.Details}";
                _detailContent.Children.Add(new Expander{Header=h.Action,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Content=PlanningUi.Text(text,12,true)});
            }
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){ }
    }
    private static Dictionary<string,string> Fields(PlanningSource? source)
    {
        var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);if(source is null)return values;
        try{using var json=JsonDocument.Parse(source.Fields);if(json.RootElement.ValueKind==JsonValueKind.Object)foreach(var p in json.RootElement.EnumerateObject())values[p.Name]=p.Value.ValueKind==JsonValueKind.Null?"No informado":p.Value.ToString();}
        catch(JsonException){ }
        return values;
    }
    private static string SourceFields(PlanningSource source)
    {var values=Fields(source);return values.Count==0?source.Fields:string.Join('\n',values.Select(p=>$"{p.Key}: {p.Value}"));}
    private Flyout ReviewFilters()
    {
        var fly=new Flyout();var panel=PlanningUi.Stack(PlanningUi.Text("Filtros adicionales",17),PlanningUi.Row(_importFrom,_importUntil),_reviewTest,_reviewLab,_reviewOrigin);
        var fields=new Dictionary<string,TextBox>();
        foreach(var (key,title) in new[]{("Buyer","Buyer"),("Vendor","Vendor"),("Service","Servicio"),("OfficialState","Estado TIPS")})
        {var input=PlanningUi.Field(title);AutomationProperties.SetName(input,"Filtro adicional "+title);fields[key]=input;panel.Children.Add(input);}
        fly.Opening+=(_,_)=>{foreach(var (key,input) in fields)input.Text=_columnFilters.GetValueOrDefault(key)??"";};
        var footer=PlanningUi.Row(Action("Restablecer filtros",LimsIconKind.ClearFilters,async()=>
        {
            _columnFilters.Clear();_reviewSearch.Text=_reviewTest.Text=_reviewLab.Text="";_importFrom.Date=_importUntil.Date=null;_reviewOrigin.SelectedIndex=0;
            foreach(var input in fields.Values)input.Text="";fly.Hide();_reviewPage=1;await RefreshReviews();
        }),Action("Aplicar filtros",LimsIconKind.ApplyFilters,async()=>
        {
            if(_importFrom.Date>_importUntil.Date){Message("El inicio no puede ser posterior al fin.");return;}
            foreach(var (key,input) in fields){_columnFilters.Remove(key);if(!string.IsNullOrWhiteSpace(input.Text))_columnFilters[key]=input.Text.Trim();}
            fly.Hide();_reviewPage=1;await RefreshReviews();
        },true));
        fly.Content=PlanningUi.Stack(new ScrollViewer{Content=panel,MaxHeight=370,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},footer);return fly;
    }
    private Flyout ReviewColumns()
    {
        var fly=new Flyout();var panel=PlanningUi.Stack(PlanningUi.Text("Columnas visibles",16));
        foreach(var column in AllReviewColumns)
        {
            var check=new CheckBox{Content=column.Label,IsChecked=_visibleReviewColumns.Contains(column.Key),IsEnabled=column.Key!="Job"};
            check.Click+=(_,_)=>{if(check.IsChecked==true)_visibleReviewColumns.Add(column.Key);else _visibleReviewColumns.Remove(column.Key);BuildReviewTable();};panel.Children.Add(check);
        }
        panel.Children.Add(PlanningUi.Text("Arrastra el borde para redimensionar. Usa el menú del encabezado para mover columnas.",12,true));
        panel.Children.Add(PlanningUi.Text("Teclado: Ctrl+C copia; Ctrl+A selecciona la página cargada; Shift+flechas amplía; Enter abre el expediente.",12,true));
        fly.Content=new ScrollViewer{Content=panel,MaxHeight=500,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};return fly;
    }
}
