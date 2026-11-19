using Lims.Contracts.Planning;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Planning;

public sealed partial class PlanningDossierWindow
{
    private Border JobControlPanel(JobDossier dossier)
    {
        var control=dossier.Control??new(dossier.Job,false,null,null,0);
        var content=PlanningUi.Stack(PlanningUi.Text($"{(control.OnHold?"ON HOLD":"Sin On Hold registrado en LIMS")} · Etiqueta: {control.Label??"Sin etiqueta registrada"}",14),
            PlanningUi.Text(control.OnHold?"Motivo: "+control.HoldReason:"Las listas heredadas de On Hold/etiquetas no están integradas. Este estado corresponde a los controles persistidos del nuevo LIMS.",12,true));
        var official=dossier.Official??new();
        content.Children.Add(PlanningUi.Text($"Estado TIPS · {official.SampleStatus??official.JobStatus??"No informado"}",14));
        content.Children.Add(PlanningUi.Text($"Sample Status: {official.SampleStatus??"No informado"} · Job Status: {official.JobStatus??"No informado"} · Close Date: {official.CloseDate?.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)??"No informada"}",12,true));
        content.Children.Add(PlanningUi.Text($"Customer_status: {official.CustomerStatus??"No informado"} · PI ExRate: {official.ExchangeRate?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"No informada"}",12,true));
        var source=dossier.Sources.FirstOrDefault(s=>s.File=="Summary");
        if(source is not null)content.Children.Add(Selectable($"Origen: Summary · {source.Sheet}, fila {source.Row} · lote {source.BatchId} · actualizado {source.ImportedAt.ToLocalTime():g}. Usuario y antes/después en Historial; campos originales en Procedencia."));
        content.Spacing=6;
        if(_permissions.Contains(PlanningPermissions.EditWip,StringComparer.Ordinal))content.Children.Add(ConsultationVisuals.Action("Gestionar On Hold y etiqueta",LimsIconKind.Lock,EditControl));
        return ConsultationVisuals.Section("CONTROL DEL JOB",content);
    }
    private async Task EditControl()
    {
        if(_dossier is not{} dossier)return;var current=dossier.Control??new(dossier.Job,false,null,null,0);
        var state=new ComboBox{Header="Estado operativo",ItemsSource=new[]{"Activo","On Hold"},SelectedIndex=current.OnHold?1:0};AutomationProperties.SetName(state,"Estado operativo del JOB");
        var label=PlanningUi.Field("Etiqueta del JOB",current.Label,80);AutomationProperties.SetName(label,"Etiqueta del JOB");
        var reason=PlanningUi.Field("Motivo obligatorio del control",max:1000);AutomationProperties.SetName(reason,"Motivo obligatorio del control");
        var dialog=new ContentDialog{XamlRoot=_layout.XamlRoot,Title="On Hold / etiqueta · "+dossier.Job,
            Content=PlanningUi.Stack(PlanningUi.Text("Decisión del nuevo LIMS para este JOB y todos sus episodios. No modifica información oficial de TIPS.",13,true),state,label,reason),PrimaryButtonText="Guardar control",CloseButtonText="Cancelar",DefaultButton=ContentDialogButton.Close};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        var result=await _api.SaveControl(_id,new(current.Version,state.SelectedIndex==1,label.Text,reason.Text),_lifetime.Token);
        if(!result.IsSuccess){_message.Text=result.Error??"No se pudo guardar el control.";return;}
        await Load();if(ControlChanged is{} changed)await changed();_message.Text="Control del JOB guardado y auditado.";
    }
}
