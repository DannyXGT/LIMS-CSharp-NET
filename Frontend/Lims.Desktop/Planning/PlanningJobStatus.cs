using System.Globalization;
using System.Text.Json;
using Lims.Contracts.Planning;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Lims.Desktop.Planning;

public sealed partial class PlanningDossierWindow
{
    private void BuildStatus(JobDossier dossier)
    {
        var body=PlanningUi.Rows(new(1,GridUnitType.Star),GridLength.Auto);var pivot=new Pivot();pivot.Resources["PivotHeaderItemFontSize"]=16d;
        var summary=PlanningUi.Stack(JobControlPanel(dossier));
        var counts=new Grid{ColumnSpacing=8};for(int i=0;i<4;i++)counts.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        var metrics=new[]{(dossier.Tests.Count,"Pruebas registradas"),(dossier.Tests.Count(PlanningStatusText.Digitized),"Digitadas"),(dossier.Tests.Count(PlanningStatusText.Pending),"Pendientes"),(dossier.Tests.Count(PlanningStatusText.Unknown),"Sin información")};
        var tones=new[]{"LimsTextPrimaryBrush","LimsSuccessBrush","LimsWarningBrush","LimsTextSecondaryBrush"};
        for(int i=0;i<metrics.Length;i++){var metric=ConsultationVisuals.Metric(PlanningUi.Text(metrics[i].Item1.ToString("N0",CultureInfo.CurrentCulture),22),metrics[i].Item2,tones[i]);Grid.SetColumn(metric,i);counts.Children.Add(metric);}
        summary.Children.Add(ConsultationVisuals.Section("RESUMEN · DATOS DISPONIBLES",counts));
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Central America Standard Time").DateTime);
        int overdue=dossier.Wip.DueDate is{} due&&due<=today?dossier.Tests.Count(PlanningStatusText.Pending):0;
        summary.Children.Add(PlanningUi.Text($"Estado TIPS: {dossier.Official?.SampleStatus??dossier.Official?.JobStatus??"No informado"} · {overdue} pendientes vencidas (incluye hoy y excluye On Hold). Sin información de digitación y Pendiente operativo son dimensiones independientes. La cobertura heredada no está verificada.",12,true));
        summary.Children.Add(PlanningUi.Text("Pruebas sin digitación informada",16));
        summary.Children.Add(StructuredTable([("Prueba",300),("Estado",150),("Origen",120)],dossier.Tests.Where(t=>!PlanningStatusText.Digitized(t)).Select(t=>new[]{t.PlanningTest,PlanningSheetModel.Value(t,"DigitizationState"),t.OriginLabel}),260));
        foreach(var incident in dossier.Incidents.Where(i=>i.State!="Resolved"))
        {var row=PlanningUi.Stack(PlanningUi.Row(new StatusBadge{Text=PlanningLabels.State(incident.State),Tone="Warning"},PlanningUi.Text(PlanningLabels.Category(incident.Kind),14)),PlanningUi.Text(incident.Reason??"Sin detalle adicional.",13,true));if(_permissions.Contains(PlanningPermissions.EditWip,StringComparer.Ordinal))row.Children.Add(PlanningUi.Button(incident.Kind=="SummaryWithoutReport"?"Decidir: pendiente, retener o resolver":"Retener / devolver a revisión",()=>Decide(incident)));summary.Children.Add(PlanningUi.Surface(row));}
        if(dossier.Tests.Count==0)summary.Children.Add(PlanningUi.Text("No hay pruebas registradas. Consulte la incidencia de asignación; no se inventan pruebas faltantes.",13));
        pivot.Items.Add(new PivotItem{Header="Resumen y estado",Content=new ScrollViewer{Content=summary}});
        var tests=StructuredTable([("Prueba",245),("Nombre técnico original",245),("Laboratorio",120),("Digitación",150),("Hora",100),("Fecha Prueba",110),("Digitado por",125),("Chequeo",165),("Chequeado por",150),("Fecha de chequeo",175),("Ejecución",285),("Tipo",120),("Origen",110),("Responsable",150),("Observación",240)],
            dossier.Tests.Select(t=>new[]{t.PlanningTest,t.OriginalTest??"—",t.Laboratory??"—",PlanningSheetModel.Value(t,"DigitizationState"),PlanningSheetModel.Value(t,"DigitizedAt"),PlanningSheetModel.Value(t,"TestedOn"),t.DigitizedBy??"—",PlanningSheetModel.Value(t,"ScheduleReviewState"),t.ScheduleReviewedBy??"—",PlanningSheetModel.Value(t,"ScheduleReviewedAt"),t.ExecutionId?.ToString()??"—",t.ExecutionType??"—",t.OriginLabel,t.Responsible??"—",t.Observation??"—"}));
        pivot.Items.Add(new PivotItem{Header="Pruebas",Content=tests});
        var w=dossier.Wip;var wip=PlanningUi.Stack();var values=new Grid{ColumnSpacing=10,RowSpacing=10};for(int i=0;i<2;i++)values.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        var fields=new[]{("Buyer / Marca",w.Buyer),("Vendor",w.Vendor),("Service",w.Service),("USD",w.Usd?.ToString("N4",System.Globalization.CultureInfo.InvariantCulture)),("In Date",w.InDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),("Due Date",w.DueDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),("Importe fuente",w.Amount?.ToString(System.Globalization.CultureInfo.InvariantCulture)),("Moneda fuente",w.Currency)};
        for(int i=0;i<4;i++)values.RowDefinitions.Add(new(){Height=GridLength.Auto});
        for(int i=0;i<fields.Length;i++){var tile=PlanningUi.Stack(PlanningUi.Text(fields[i].Item1,12,true),PlanningUi.Text(fields[i].Item2??"No informado",14));tile.Spacing=4;tile.Margin=new Thickness(0,0,0,8);Grid.SetColumn(tile,i%2);Grid.SetRow(tile,i/2);values.Children.Add(tile);}wip.Children.Add(ConsultationVisuals.Section("INFORMACIÓN WIP",values));
        var summarySource=dossier.Sources.FirstOrDefault(s=>s.File=="Summary");
        wip.Children.Add(PlanningUi.Text(summarySource is null?"No hay una fila WIP / Summary de origen disponible en la procedencia consultada. Los valores conocidos y las correcciones autorizadas se muestran arriba.":$"Fuente: Summary · hoja {summarySource.Sheet} · fila {summarySource.Row} · importado {summarySource.ImportedAt.ToOffset(TimeSpan.FromHours(-6)):g}. Las correcciones manuales se detallan en Historial.",12,true));
        var actions=PlanningUi.Row();if(_permissions.Contains(PlanningPermissions.EditWip,StringComparer.Ordinal))actions.Children.Add(PlanningUi.Button("Editar WIP",Edit));if(_permissions.Contains(PlanningPermissions.ManualTests,StringComparer.Ordinal))actions.Children.Add(PlanningUi.Button("Agregar prueba manual",Manual));wip.Children.Add(actions);
        var technical=PlanningUi.Stack();
        technical.Children.Add(Selectable("Episodio: "+dossier.EpisodeId+"\n"+string.Join("\n",dossier.Tests.Select(t=>$"{t.OriginalTest} · ejecución {t.ExecutionId}"))));
        foreach(var source in dossier.Sources)technical.Children.Add(new Expander{Header=$"{source.ImportedAt.ToOffset(TimeSpan.FromHours(-6)):g} · {source.File} · {source.Sheet}:{source.Row}",Content=Selectable($"Lote: {source.BatchId}\nSHA256: {source.Sha256}\n{source.Fields}")});
        wip.Children.Add(new Expander{Header="Procedencia e identificadores técnicos · últimas 2000 filas",Content=technical});
        pivot.Items.Add(new PivotItem{Header="WIP",Content=new ScrollViewer{Content=wip}});
        var history=new ListView{SelectionMode=ListViewSelectionMode.None};
        foreach(var h in dossier.History)history.Items.Add(PlanningUi.Surface(PlanningUi.Stack(PlanningUi.Text($"{h.At.ToOffset(TimeSpan.FromHours(-6)):g} · {h.ActorName??"Usuario no informado"} · {PlanningStatusText.Action(h.Action)}",13),PlanningUi.Text(h.Reason,12,true),Selectable(PlanningStatusText.Changes(h.Details)))));
        var historyContent=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star));PlanningUi.Place(historyContent,PlanningUi.Text("Historial · últimos 1000 eventos · más recientes primero",12,true),0);PlanningUi.Place(historyContent,history,1);
        pivot.Items.Add(new PivotItem{Header="Historial",Content=historyContent});PlanningUi.Place(body,pivot,0);
        var copy=ConsultationVisuals.Action("Copiar estado para Teams",LimsIconKind.Reports,()=>{try{var data=new DataPackage();data.SetText(PlanningStatusText.Teams(dossier));Clipboard.SetContent(data);Clipboard.Flush();_message.Text=dossier.Job+" · Estado copiado para Teams";}catch(System.Runtime.InteropServices.COMException){_message.Text="El portapapeles está ocupado; vuelva a copiar.";}return Task.CompletedTask;});
        PlanningUi.Place(body,ConsultationVisuals.Split(PlanningUi.Text($"Estado TIPS: {dossier.Official?.SampleStatus??dossier.Official?.JobStatus??"No informado"}",12,true),copy),1);
        pivot.SelectedIndex=Math.Clamp(_selectedTab,0,pivot.Items.Count-1);pivot.SelectionChanged+=(_,_)=>_selectedTab=pivot.SelectedIndex;_body.Content=body;
        _layout.KeyDown-=CloseOnEscape;_layout.KeyDown+=CloseOnEscape;
    }
    private static Grid GeneralInformation(JobDossier dossier)
    {
        var grid=new Grid{ColumnSpacing=24};for(int i=0;i<4;i++)grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        var fields=new[]{("Cliente / Buyer",dossier.Wip.Buyer),("Vendor",dossier.Wip.Vendor),("Ingreso",dossier.Wip.InDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),("Vencimiento",dossier.Wip.DueDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture))};
        for(int i=0;i<fields.Length;i++){var value=PlanningUi.Text(fields[i].Item2??"No informado",14);value.IsTextSelectionEnabled=true;var field=PlanningUi.Stack(PlanningUi.Text(fields[i].Item1,12,true),value);field.Spacing=3;Grid.SetColumn(field,i);grid.Children.Add(field);}return grid;
    }
    private void CloseOnEscape(object sender,Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e){if(e.Key==VirtualKey.Escape&&e.OriginalSource is not TextBox){Close();e.Handled=true;}}
    private static ScrollViewer StructuredTable((string,int)[] columns,IEnumerable<string[]> rows,double? maxHeight=null)
    {
        var grid=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star));grid.RowSpacing=0;double width=columns.Sum(c=>c.Item2);
        Grid Line(string[] values,bool header,int number)
        {
            var line=new Grid{Width=width,MinHeight=header?37:32,Background=PlanningUi.Brush(header?"LimsSurfaceSecondaryBrush":number%2==0?"LimsSurfaceBrush":"LimsInputBackgroundBrush"),BorderBrush=PlanningUi.Brush("LimsBorderBrush"),BorderThickness=new Thickness(0,0,0,1)};
            for(int c=0;c<columns.Length;c++){line.ColumnDefinitions.Add(new(){Width=new(columns[c].Item2)});var cell=PlanningUi.Text(c<values.Length?values[c]:"",13,header);if(header)cell.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;cell.TextWrapping=TextWrapping.NoWrap;cell.TextTrimming=TextTrimming.CharacterEllipsis;cell.Margin=new Thickness(10,7,8,7);ToolTipService.SetToolTip(cell,cell.Text);Grid.SetColumn(cell,c);line.Children.Add(cell);}return line;
        }
        PlanningUi.Place(grid,Line(columns.Select(c=>c.Item1).ToArray(),true,0),0);
        var list=new ListView{SelectionMode=ListViewSelectionMode.None,Padding=new Thickness(0)};int index=0;foreach(var row in rows)list.Items.Add(Line(row,false,index++));if(index==0)list.Items.Add(PlanningUi.Text("Sin registros para este estado.",12,true));
        if(maxHeight is{} max)list.MaxHeight=max;PlanningUi.Place(grid,list,1);
        return new ScrollViewer{Content=grid,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalContentAlignment=VerticalAlignment.Stretch};
    }
}
