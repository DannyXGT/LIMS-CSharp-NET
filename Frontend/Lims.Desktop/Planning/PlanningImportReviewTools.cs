using System.Globalization;
using Lims.Contracts.Planning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Lims.Desktop.Planning;

public sealed partial class PlanningImportsView
{
    private void SetReviewBatch(Guid id)
    {_reviewBatch=id;PaintBatchChoices();}
    private void PaintBatchChoices()
    {
        _paintingScope=true;
        try
        {
            var items=new List<BatchChoice>{new(null,"Histórico acumulado (explícito)")};
            items.AddRange(_batches.Where(b=>b.Status=="Applied").Select((b,i)=>new BatchChoice(b.Id,$"{(i==0?"Lote actual · ":"")}{b.Day:yyyy-MM-dd} · {CutLabel(b.Window)} · {b.Id.ToString()[..8]}")));
            if(_reviewBatch is{} id&&items.All(c=>c.Id!=id))items.Add(new(id,"Lote en revisión · "+id.ToString()[..8]));
            _reviewScope.ItemsSource=items;_reviewScope.SelectedItem=items.FirstOrDefault(c=>c.Id==_reviewBatch)??items[0];
            ToolTipService.SetToolTip(_reviewScope,(_reviewScope.SelectedItem as BatchChoice)?.Label);
        }
        finally{_paintingScope=false;}
    }
    private void ConfigureReviewCells()
    {
        _reviewCells.Model.Columns.Clear();_reviewCells.Model.Columns.AddRange(_reviewColumns.Select(c=>new SheetColumn(c.Key,c.Label,c.Width)));
        _reviewCells.Model.DisplayValue=(row,key)=>row>=_reviewRows.Count?"":ReviewValue(_reviewRows[row],key);
    }
    private static string ReviewValue(PlanningReviewCase r,string key)=>key switch
    {
        "CaseCategory"=>PlanningLabels.Category(r.Category),"Job"=>r.Job,"InDate"=>r.InDateText,"DueDate"=>r.DueDateText,"OfficialState"=>r.OfficialStateText,"Buyer"=>r.BuyerText,"Vendor"=>r.VendorText,
        "Service"=>r.ServiceText,"Usd"=>r.UsdText,"State"=>r.StateLabel,"OriginalTest"=>r.OriginalTest??"No informado","Equivalence"=>r.Equivalence??"No informado",
        "Laboratory"=>r.Laboratory??"No informado","Reason"=>r.Reason??"","Origin"=>r.Origin=="ReportList"?"Report List":r.Origin??"No informado",
        "ImportedAt"=>r.ImportedAt?.ToOffset(TimeSpan.FromHours(-6)).ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture)??"No informado",_=>""
    };
    private void LoadReviewCells()
    {
        _reviewCells.Model.Rows.Clear();
        // UI adapter only: actual episode/execution UUIDs remain unchanged; values are supplied by ReviewValue.
        foreach(var r in _reviewRows)_reviewCells.Model.Rows.Add(new(r.EpisodeId??Guid.Empty,r.ExecutionId,r.Job,r.InDate,r.DueDate,r.OriginalTest,r.Equivalence,r.Laboratory,r.Service,null,null,"Sin información",null,null,r.Origin??"Summary",r.Buyer,r.Vendor,true));
        if(_reviewRows.Count>0)_reviewCells.Model.Select(0,0);_reviewCells.SetSort(_reviewSort,_reviewDescending);_reviewCells.SetFilters(_columnFilters.Keys);_reviewCells.Paint();
    }
    private async Task ExportReviewScope(int mode)
    {
        var query=ReviewQuery();
        if(mode==1)
        {
            var keys=SelectedReviews().Select(r=>r.Key).ToArray();if(keys.Length==0){Message("Selecciona filas para exportar.");return;}
            query=query with{SelectedKeys=System.Text.Json.JsonSerializer.Serialize(keys)};
        }
        else if(mode==2)query=query with{Situation="All",Search=null,ColumnFilters=null,Test=null,Laboratory=null,Origin=null,ImportedFrom=null,ImportedUntil=null};
        var result=await _api.ExportReviews(query,CancellationToken.None);if(!result.IsSuccess||result.Value is null){Message(result.Error??"No se pudo exportar.");return;}
        try{var path=await PlanningUi.PickFile(XamlRoot,true);if(path is null)return;await File.WriteAllBytesAsync(path,result.Value);Message(mode==1?"Selección exportada.":"Listado completo exportado; incluye todas las páginas del alcance seleccionado.");}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){Message(e.Message);}
    }
    private async Task CompleteReviewData()
    {
        if(SelectedReview is not PlanningReviewCase{EpisodeId:{} id})return;
        var result=await _api.Dossier(id,CancellationToken.None);if(result.Value is not{} dossier){Message(result.Error??"No se pudo consultar el expediente.");return;}
        TextBox buyer=PlanningUi.Field("Buyer",dossier.Wip.Buyer),vendor=PlanningUi.Field("Vendor",dossier.Wip.Vendor),reason=PlanningUi.Field("Motivo y fuente verificada",max:1000);
        CalendarDatePicker incoming=new CalendarDatePicker{Header="Fecha ingreso",DateFormat="{year.full}-{month.integer(2)}-{day.integer(2)}"},due=new CalendarDatePicker{Header="Due Date",DateFormat="{year.full}-{month.integer(2)}-{day.integer(2)}"};
        if(dossier.Wip.InDate is{} first)incoming.Date=new(first.ToDateTime(TimeOnly.MinValue),TimeSpan.FromHours(-6));if(dossier.Wip.DueDate is{} last)due.Date=new(last.ToDateTime(TimeOnly.MinValue),TimeSpan.FromHours(-6));
        var dialog=new ContentDialog{XamlRoot=XamlRoot,Title="Completar datos WIP · "+dossier.Job,Content=PlanningUi.Stack(PlanningUi.Text("Corrección manual de WIP con procedencia; no cambia campos oficiales de TIPS.",12,true),buyer,vendor,PlanningUi.Row(incoming,due),reason),PrimaryButtonText="Guardar datos",CloseButtonText="Cancelar"};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        var saved=await _api.SaveWip(id,new(dossier.Version,dossier.Wip with{Buyer=string.IsNullOrWhiteSpace(buyer.Text)?null:buyer.Text.Trim(),Vendor=string.IsNullOrWhiteSpace(vendor.Text)?null:vendor.Text.Trim(),InDate=incoming.Date is{} f?DateOnly.FromDateTime(f.DateTime):null,DueDate=due.Date is{} u?DateOnly.FromDateTime(u.DateTime):null},reason.Text),CancellationToken.None);
        if(saved.IsSuccess){await Changed();Message("Datos WIP guardados con autor, motivo y valores anteriores.");}else Message(saved.Error??"No se guardó.");
    }
}
