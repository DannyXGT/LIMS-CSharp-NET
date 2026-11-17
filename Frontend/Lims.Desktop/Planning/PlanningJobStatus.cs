using System.Globalization;
using System.Text.Json;
using Lims.Contracts.Planning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Lims.Desktop.Planning;

public static class PlanningStatusText
{
    public static bool Digitized(PlanningRow row)=>row.DigitizedAt is not null;
    public static bool Pending(PlanningRow row)=>!Digitized(row)&&row.DigitizationState=="Pendiente";
    public static bool Unknown(PlanningRow row)=>!Digitized(row)&&row.DigitizationState=="Sin información";
    public static string Teams(JobDossier d)=>$"JOB: {d.Job}\nCliente: {d.Wip.Buyer??"No informado"}\nIngreso: {d.Wip.InDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??"No informado"}\nVencimiento: {d.Wip.DueDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??"No informado"}\nPruebas registradas: {d.Tests.Count}\nDigitadas: {d.Tests.Count(Digitized)}\nPendientes: {d.Tests.Count(Pending)}\nSin información: {d.Tests.Count(Unknown)}\nPruebas pendientes: {Names(d.Tests.Where(Pending))}\nPruebas sin información: {Names(d.Tests.Where(Unknown))}\nEstado oficial: No verificado\nLos conteos de digitación no certifican el cierre oficial del JOB.";
    private static string Names(IEnumerable<PlanningRow> rows)=>string.Join(", ",rows.Select(t=>t.OriginalTest??"Prueba no informada"))is{Length:>0} value?value:"Ninguna registrada";
    public static string Action(string action)=>action switch
    {"ConsultationNotesEdited"=>"Responsable / observación actualizados","WipImported"=>"WIP importado","WipSourceObserved"=>"Fuente WIP conciliada","WipEdited"=>"WIP corregido","ManualTestAdded"=>"Prueba manual agregada","ManualSourceLinked"=>"Prueba manual vinculada al archivo","TestImported"=>"Prueba importada","IncidentDecision"=>"Decisión de incidencia","IncidentsReconciled"=>"Incidencias conciliadas","ExecutionIdentityResolved"=>"Ejecución conciliada","EquivalenceApplied"=>"Equivalencia aplicada",_=>"Actualización de Planificación"};
    private static string Label(string key)=>key.ToLowerInvariant() switch{"responsible"=>"Responsable","observation"=>"Observación","buyer"=>"Buyer / marca","vendor"=>"Vendor","service"=>"Servicio","indate"=>"Ingreso","duedate"=>"Vencimiento","amount"=>"Importe fuente","currency"=>"Moneda fuente","usd"=>"USD",_=>key};
    public static string Changes(string details)
    {
        try
        {
            using var doc=JsonDocument.Parse(details);var root=doc.RootElement;
            if(root.TryGetProperty("before",out var before)&&root.TryGetProperty("after",out var after)&&after.ValueKind==JsonValueKind.Object)
            {var values=new List<string>();foreach(var field in after.EnumerateObject()){var old=before.ValueKind==JsonValueKind.Object&&before.TryGetProperty(field.Name,out var b)?b.ToString():"";if(old!=field.Value.ToString())values.Add($"{Label(field.Name)}: {(old.Length==0?"Sin valor":old)} → {(field.Value.ValueKind==JsonValueKind.Null?"Sin valor":field.Value.ToString())}");}return string.Join("\n",values);}
            if(root.TryGetProperty("source",out var source))return string.Join(" · ",source.EnumerateObject().Where(p=>p.Value.ValueKind!=JsonValueKind.Null).Select(p=>$"{Label(p.Name)}: {p.Value}"));
            if(root.TryGetProperty("state",out var state))return "Estado: "+PlanningLabels.State(state.GetString()??"");
            if(root.TryGetProperty("originalTest",out var test))return "Prueba: "+test.GetString();
            return "Cambio registrado con su fuente en procedencia.";
        }
        catch(JsonException){return "Detalle histórico disponible en procedencia técnica.";}
    }
}

public sealed partial class PlanningDossierWindow
{
    private void BuildStatus(JobDossier dossier)
    {
        var body=PlanningUi.Rows(new(1,GridUnitType.Star),GridLength.Auto);var pivot=new Pivot();
        var summary=PlanningUi.Stack();
        var counts=new Grid{ColumnSpacing=8};for(int i=0;i<4;i++)counts.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        var metrics=new[]{(dossier.Tests.Count,"Pruebas registradas"),(dossier.Tests.Count(PlanningStatusText.Digitized),"Digitadas"),(dossier.Tests.Count(PlanningStatusText.Pending),"Pendientes"),(dossier.Tests.Count(PlanningStatusText.Unknown),"Sin información")};
        for(int i=0;i<metrics.Length;i++){var tile=PlanningUi.Surface(PlanningUi.Stack(PlanningUi.Text(metrics[i].Item1.ToString(CultureInfo.InvariantCulture),22),PlanningUi.Text(metrics[i].Item2,11,true)));Grid.SetColumn(tile,i);counts.Children.Add(tile);}summary.Children.Add(counts);
        summary.Children.Add(PlanningUi.Text($"Cliente: {dossier.Wip.Buyer??"No informado"} · Servicio: {dossier.Wip.Service??"No informado"}\nIngreso: {dossier.Wip.InDate:yyyy-MM-dd} · Vencimiento: {dossier.Wip.DueDate:yyyy-MM-dd}",13));
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Central America Standard Time").DateTime);
        int overdue=dossier.Wip.DueDate is{} due&&due<today?dossier.Tests.Count(t=>!PlanningStatusText.Digitized(t)&&t.DigitizationState!="NoProgramar"):0;
        summary.Children.Add(PlanningUi.Text($"Estado oficial TIPS: No verificado · {overdue} ejecuciones sin digitación informada con Due Date vencido. El vencimiento no acredita un cierre oficial.",12,true));
        summary.Children.Add(PlanningUi.Text("¿Qué falta para este JOB?",15));
        summary.Children.Add(StructuredTable([("Prueba",300),("Estado",150),("Origen",120)],dossier.Tests.Where(t=>!PlanningStatusText.Digitized(t)).Select(t=>new[]{t.OriginalTest??"—",PlanningSheetModel.Value(t,"DigitizationState"),t.OriginLabel}),260));
        foreach(var incident in dossier.Incidents.Where(i=>i.State!="Resolved"))
        {var row=PlanningUi.Stack(PlanningUi.Text($"{PlanningLabels.Category(incident.Kind)} · {PlanningLabels.State(incident.State)}",13),PlanningUi.Text(incident.Reason??"",12,true));if(_permissions.Contains(PlanningPermissions.EditWip,StringComparer.Ordinal))row.Children.Add(PlanningUi.Button("Retener / devolver a revisión",()=>Decide(incident)));summary.Children.Add(row);}
        if(dossier.Tests.Count==0)summary.Children.Add(PlanningUi.Text("No hay pruebas registradas. Consulte la incidencia de asignación; no se inventan pruebas faltantes.",13));
        pivot.Items.Add(new PivotItem{Header="Resumen y estado",Content=new ScrollViewer{Content=summary}});
        var tests=StructuredTable([("Prueba original",245),("Equivalencia",180),("Laboratorio",120),("Digitación",130),("Fecha / hora",170),("Usuario",125),("Tipo",120),("Origen",110)],
            dossier.Tests.Select(t=>new[]{t.OriginalTest??"—",t.Equivalence??"—",t.Laboratory??"—",PlanningSheetModel.Value(t,"DigitizationState"),t.DigitizedAt?.ToOffset(TimeSpan.FromHours(-6)).ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture)??"—",t.DigitizedBy??"—",t.ExecutionType??"—",t.OriginLabel}));
        pivot.Items.Add(new PivotItem{Header="Pruebas",Content=tests});
        var w=dossier.Wip;var wip=PlanningUi.Stack();var values=new Grid{ColumnSpacing=10,RowSpacing=10};for(int i=0;i<2;i++)values.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        var fields=new[]{("Buyer / Marca",w.Buyer),("Vendor",w.Vendor),("Service",w.Service),("USD",w.Usd?.ToString("N4",System.Globalization.CultureInfo.InvariantCulture)),("In Date",w.InDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),("Due Date",w.DueDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),("Importe fuente",w.Amount?.ToString(System.Globalization.CultureInfo.InvariantCulture)),("Moneda fuente",w.Currency)};
        for(int i=0;i<4;i++)values.RowDefinitions.Add(new(){Height=GridLength.Auto});
        for(int i=0;i<fields.Length;i++){var tile=PlanningUi.Surface(PlanningUi.Stack(PlanningUi.Text(fields[i].Item1,12,true),PlanningUi.Text(fields[i].Item2??"No informado",14)));Grid.SetColumn(tile,i%2);Grid.SetRow(tile,i/2);values.Children.Add(tile);}wip.Children.Add(values);
        var summarySource=dossier.Sources.FirstOrDefault(s=>s.File=="Summary");
        wip.Children.Add(PlanningUi.Text(summarySource is null?"No hay una fila WIP / Summary de origen disponible en la procedencia consultada. Los valores conocidos y las correcciones autorizadas se muestran arriba.":$"Fuente: Summary · hoja {summarySource.Sheet} · fila {summarySource.Row} · importado {summarySource.ImportedAt.ToOffset(TimeSpan.FromHours(-6)):g}. Las correcciones manuales se detallan en Historial.",12,true));
        var actions=PlanningUi.Row();if(_permissions.Contains(PlanningPermissions.EditWip,StringComparer.Ordinal))actions.Children.Add(PlanningUi.Button("Editar WIP",Edit));if(_permissions.Contains(PlanningPermissions.ManualTests,StringComparer.Ordinal))actions.Children.Add(PlanningUi.Button("Agregar prueba manual",Manual));wip.Children.Add(actions);
        var technical=PlanningUi.Stack();
        technical.Children.Add(Selectable("Episodio: "+dossier.EpisodeId+"\n"+string.Join("\n",dossier.Tests.Select(t=>$"{t.OriginalTest} · ejecución {t.ExecutionId}"))));
        foreach(var source in dossier.Sources)technical.Children.Add(new Expander{Header=$"{source.ImportedAt.ToOffset(TimeSpan.FromHours(-6)):g} · {source.File} · {source.Sheet}:{source.Row}",Content=Selectable($"Lote: {source.BatchId}\nSHA256: {source.Sha256}\n{source.Fields}")});
        wip.Children.Add(new Expander{Header="Procedencia e identificadores técnicos · últimas 2000 filas",Content=technical});
        pivot.Items.Add(new PivotItem{Header="WIP",Content=new ScrollViewer{Content=wip}});
        var history=new ListView{SelectionMode=ListViewSelectionMode.None};
        foreach(var h in dossier.History)history.Items.Add(PlanningUi.Surface(PlanningUi.Stack(PlanningUi.Text($"{h.At.ToOffset(TimeSpan.FromHours(-6)):g} · {h.ActorName??("Usuario "+h.ActorId)} · {PlanningStatusText.Action(h.Action)}",13),PlanningUi.Text(h.Reason,12,true),Selectable(PlanningStatusText.Changes(h.Details)))));
        var historyContent=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star));PlanningUi.Place(historyContent,PlanningUi.Text("Historial · últimos 1000 eventos · más recientes primero",12,true),0);PlanningUi.Place(historyContent,history,1);
        pivot.Items.Add(new PivotItem{Header="Historial",Content=historyContent});PlanningUi.Place(body,pivot,0);
        var copy=PlanningUi.Button("Copiar estado para Teams",()=>{try{var data=new DataPackage();data.SetText(PlanningStatusText.Teams(dossier));Clipboard.SetContent(data);Clipboard.Flush();_message.Text=dossier.Job+" · Estado copiado para Teams";}catch(System.Runtime.InteropServices.COMException){_message.Text="El portapapeles está ocupado; vuelva a copiar.";}return Task.CompletedTask;},true);
        PlanningUi.Place(body,PlanningUi.Row(PlanningUi.Text("Digitación se registra únicamente en Report List.",11,true),copy),1);_body.Content=body;
        _layout.KeyDown-=CloseOnEscape;_layout.KeyDown+=CloseOnEscape;
    }
    private void CloseOnEscape(object sender,Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e){if(e.Key==VirtualKey.Escape&&e.OriginalSource is not TextBox){Close();e.Handled=true;}}
    private static ScrollViewer StructuredTable((string,int)[] columns,IEnumerable<string[]> rows,double? maxHeight=null)
    {
        var grid=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star));grid.RowSpacing=0;double width=columns.Sum(c=>c.Item2);
        Grid Line(string[] values,bool header,int number)
        {
            var line=new Grid{Width=width,MinHeight=header?37:32,Background=PlanningSheetGrid.Color(header?"#1A3044":number%2==0?"#101D29":"#13212E")};
            for(int c=0;c<columns.Length;c++){line.ColumnDefinitions.Add(new(){Width=new(columns[c].Item2)});var cell=PlanningUi.Text(c<values.Length?values[c]:"",12);cell.TextWrapping=TextWrapping.NoWrap;cell.TextTrimming=TextTrimming.CharacterEllipsis;cell.Margin=new Thickness(10,7,8,7);ToolTipService.SetToolTip(cell,cell.Text);Grid.SetColumn(cell,c);line.Children.Add(cell);}return line;
        }
        PlanningUi.Place(grid,Line(columns.Select(c=>c.Item1).ToArray(),true,0),0);
        var list=new ListView{SelectionMode=ListViewSelectionMode.None,Padding=new Thickness(0)};int index=0;foreach(var row in rows)list.Items.Add(Line(row,false,index++));if(index==0)list.Items.Add(PlanningUi.Text("Sin registros para este estado.",12,true));
        if(maxHeight is{} max)list.MaxHeight=max;PlanningUi.Place(grid,list,1);
        return new ScrollViewer{Content=grid,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalContentAlignment=VerticalAlignment.Stretch};
    }
}
