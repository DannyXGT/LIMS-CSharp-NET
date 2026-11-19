using System.Text.Json;
using Lims.Contracts.Planning;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Lims.Desktop.Planning;

public sealed partial class PlanningImportsView
{
    private sealed record DifferenceRow(string Action,string Job,string Field,string Before,string After,string Entity,Guid? EpisodeId);
    private sealed record HistoryRow(PlanningBatch Batch,string Created,string Actor,string Cut,string State,string Results,string Files)
    {public Guid Id=>Batch.Id;}
    private readonly ListView _changes=new(){SelectionMode=ListViewSelectionMode.Extended};
    private readonly ListView _history=new(){SelectionMode=ListViewSelectionMode.Single};
    private IReadOnlyList<PlanningBatch> _batches=[];
    private readonly TextBlock _differenceCount=PlanningUi.Text("",12,true);
    private readonly TextBox _differenceSearch=PlanningUi.Field("Filtrar por JOB o campo",max:200);
    private readonly ComboBox _differenceAction=new(){ItemsSource=new[]{"Todos los cambios","JOBs nuevos","Pruebas nuevas","JOBs actualizados","Cambios WIP","Casos de revisión del lote"},SelectedIndex=0,MinWidth=180};
    private IReadOnlyList<DifferenceRow> _differences=[];
    private string _metricKind="All";
    private HashSet<Guid?> _newEpisodes=[];
    private static DataTemplate TableTemplate((string Property,int Width)[] columns)
    {
        var definitions=string.Concat(columns.Select(c=>$"<ColumnDefinition Width='{c.Width}'/>"));
        var cells=string.Concat(columns.Select((c,i)=>$"<TextBlock Grid.Column='{i}' Text='{{Binding {c.Property}}}' ToolTipService.ToolTip='{{Binding {c.Property}}}' TextTrimming='CharacterEllipsis' VerticalAlignment='Center' FontSize='13' Margin='10,5' Foreground='{{ThemeResource LimsTextPrimaryBrush}}'/>"));
        return (DataTemplate)XamlReader.Load($"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid MinHeight='38' BorderBrush='{{ThemeResource LimsBorderBrush}}' BorderThickness='0,0,0,1'><Grid.ColumnDefinitions>{definitions}</Grid.ColumnDefinitions>{cells}</Grid></DataTemplate>");
    }
    private static Grid TableHeader((string Label,int Width)[] columns)
    {
        var grid=new Grid{Background=PlanningUi.Brush("LimsSurfaceSecondaryBrush"),Height=36};
        foreach(var (label,width) in columns)
        {
            var i=grid.ColumnDefinitions.Count;grid.ColumnDefinitions.Add(new(){Width=new(width)});
            var text=PlanningUi.Text(label,12,true);text.VerticalAlignment=VerticalAlignment.Center;text.Margin=new(10,0,10,0);Grid.SetColumn(text,i);grid.Children.Add(text);
        }
        return grid;
    }
    private static void StyleRows(ListView list)
    {
        var rowStyle=new Style{TargetType=typeof(ListViewItem),BasedOn=(Style)Application.Current.Resources["LimsDataRowStyle"]};
        rowStyle.Setters.Add(new Setter(ListViewItem.MinHeightProperty,38d));rowStyle.Setters.Add(new Setter(ListViewItem.PaddingProperty,new Thickness(0)));
        list.ItemContainerStyle=rowStyle;
        list.HorizontalContentAlignment=HorizontalAlignment.Stretch;
    }
    private static Border TableBody(ListView list,Grid header,double width)
    {
        StyleRows(list);
        var grid=PlanningUi.Rows(new GridLength(36),new GridLength(1,GridUnitType.Star));grid.RowSpacing=0;grid.Width=width;grid.HorizontalAlignment=HorizontalAlignment.Left;
        PlanningUi.Place(grid,header,0);PlanningUi.Place(grid,list,1);
        return Surface(new ScrollViewer{Content=grid,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalContentAlignment=HorizontalAlignment.Left,VerticalContentAlignment=VerticalAlignment.Stretch},new Thickness(0));
    }
    private Grid DifferencePanel()
    {
        _changes.ItemTemplate=TableTemplate([("Action",130),("Job",220),("Field",200),("Before",320),("After",360)]);
        AutomationProperties.SetName(_changes,"Diferencias del lote");
        var layout=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star));layout.RowSpacing=8;
        var clear=Action("Todos los cambios",LimsIconKind.ClearFilters,()=>{_metricKind="All";_differenceAction.SelectedIndex=0;_differenceSearch.Text="";FilterDifferences();return Task.CompletedTask;});
        var filters=PlanningUi.Row(_differenceAction,_differenceSearch,clear);filters.Spacing=8;
        PlanningUi.Place(layout,Split(filters,_differenceCount),0);
        PlanningUi.Place(layout,TableBody(_changes,TableHeader([("Acción",130),("JOB",220),("Campo / prueba",200),("Antes",320),("Después",360)]),1230),1);
        _differenceSearch.TextChanged+=(_,_)=>FilterDifferences();
        _differenceAction.SelectionChanged+=(_,_)=>{_metricKind=_differenceAction.SelectedIndex switch{1=>"NewJobs",2=>"NewTests",3=>"Updated",4=>"WIP",5=>"Review",_=>"All"};FilterDifferences();};
        return layout;
    }
    private Grid HistoryPanel()
    {
        _history.ItemTemplate=TableTemplate([("Created",160),("Actor",170),("Cut",170),("State",100),("Results",460),("Files",470)]);
        AutomationProperties.SetName(_history,"Historial de importaciones");
        var layout=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star));layout.RowSpacing=8;
        PlanningUi.Place(layout,PlanningUi.Row(Action("Consultar lote seleccionado",LimsIconKind.ViewDetail,OpenHistory),Action("Actualizar historial",LimsIconKind.Refresh,()=>LoadHistory(CancellationToken.None))),0);
        PlanningUi.Place(layout,TableBody(_history,TableHeader([("Fecha",160),("Usuario",170),("Día / corte",170),("Estado",100),("Resultados del lote",460),("Procedencia",470)]),1530),1);
        _history.DoubleTapped+=async(_,_)=>await OpenHistory();return layout;
    }
    private async Task OpenHistory()
    {
        if(_busy||_history.SelectedItem is not HistoryRow selected)return;
        Busy(true,"Consultando el lote y su procedencia…");
        try{var result=await _api.Batch(selected.Id,CancellationToken.None);if(result.IsSuccess&&result.Value is{} preview){_metricKind="All";Show(preview,true);if(preview.Status=="Applied")await Summary(preview.Id);else await ReviewBeforeImport();}else Message(result.Error??"Lote no disponible.");}
        finally{Busy(false);}
    }
    private async Task LoadHistory(CancellationToken ct)
    {
        var result=await _api.Batches(ct);if(ct.IsCancellationRequested)return;
        if(!result.IsSuccess){Message(result.Error??"No se pudo leer el historial.");return;}
        _batches=result.Value??[];PaintBatchChoices();var selected=(_history.SelectedItem as HistoryRow)?.Id;
        var rows=_batches.Select(b=>new HistoryRow(b,b.CreatedAt.ToOffset(TimeSpan.FromHours(-6)).ToString("g",System.Globalization.CultureInfo.GetCultureInfo("es-GT")),
            b.ActorName??"Usuario "+b.ActorId,b.Day.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)+" · "+CutLabel(b.Window),PlanningLabels.State(b.Status),
            $"{b.NewJobs:N0} JOBs · {b.NewTests:N0} pruebas · {b.UpdatedJobs:N0} actualizados · {b.WipChanges:N0} WIP",b.SummaryName+" · "+b.ReportName)).ToArray();
        _history.ItemsSource=rows;if(selected is not null)_history.SelectedItem=rows.FirstOrDefault(r=>r.Id==selected);
    }
    private void ShowDifferences(PlanningImportPreview preview)
    {
        var rows=new List<DifferenceRow>();_newEpisodes=preview.Changes.Where(c=>c.Entity=="WIP"&&c.Action is "Create" or "CreateWithoutSummary").Select(c=>c.EpisodeId).ToHashSet();
        foreach(var change in preview.Changes)
        {
            Dictionary<string,JsonElement>? before=null,after=null;
            try{before=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(change.Before);}catch(JsonException){ }
            try{after=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(change.After);}catch(JsonException){ }
            var action=change.Action switch{"Create"=>"Nuevo","CreateWithoutSummary"=>"Solo Report List","UpdateSource"=>"Actualizar fuente","LinkManual"=>"Vincular manual",_=>change.Action};
            if(after is not null)
            {
                foreach(var (field,value) in after)
                {
                    var old=before?.GetValueOrDefault(field).ToString()??"";
                    if(old!=value.ToString())rows.Add(new(action,change.Job,field,old,value.ToString(),change.Entity,change.EpisodeId));
                }
            }
            else rows.Add(new(action,change.Job,change.OriginalTest??change.Entity,change.Before,change.After,change.Entity,change.EpisodeId));
        }
        _differences=rows;FilterDifferences();
    }
    private void FilterDifferences()
    {
        var search=_differenceSearch.Text.Trim();
        var source=_metricKind=="Review"?(_preview?.Reviews??[]).Where(r=>r.Category is not ("BothFiles" or "ManualManaged")).Select(r=>new DifferenceRow("Revisar",r.Job,PlanningLabels.Category(r.Category),r.OriginalTest??"",r.Description,"Review",r.EpisodeId)):_differences;
        var rows=source.Where(r=>(_metricKind switch{
            "NewJobs"=>r.Entity=="WIP"&&_newEpisodes.Contains(r.EpisodeId),
            "NewTests"=>r.Entity=="Test"&&r.Action=="Nuevo",
            "Updated"=>r.Entity is "WIP" or "OfficialState"&&!_newEpisodes.Contains(r.EpisodeId),
            "WIP"=>r.Entity=="WIP"&&r.Action=="Actualizar fuente",_=>true
        })&&(search.Length==0||r.Job.Contains(search,StringComparison.OrdinalIgnoreCase)||r.Field.Contains(search,StringComparison.OrdinalIgnoreCase))).ToArray();
        _changes.ItemsSource=rows;_differenceCount.Text=$"{rows.Length:N0} diferencias · "+(_preview?.Status=="Applied"?"Lote confirmado":"Vista previa");
    }
}
