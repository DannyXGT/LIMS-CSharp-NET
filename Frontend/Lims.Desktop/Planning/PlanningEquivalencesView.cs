using Lims.Contracts.Planning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Planning;

public sealed class PlanningEquivalencesView : Grid
{
    private readonly PlanningApiClient _api;
    private readonly TextBox _test=PlanningUi.Field("Prueba original",max:2048);
    private readonly TextBox _name=PlanningUi.Field("Equivalencia de Planificación");
    private readonly TextBox _lab=PlanningUi.Field("Laboratorio");
    private readonly TextBox _reason=PlanningUi.Field("Motivo y fuente de la equivalencia",max:1000);
    private readonly CheckBox _no=new(){Content="NO PROGRAMAR"};
    private readonly TextBlock _message=PlanningUi.Text("",13,true);
    private readonly ListView _list=new(){SelectionMode=ListViewSelectionMode.Single};
    private IReadOnlyList<PlanningEquivalence> _mappings=[];
    public PlanningEquivalencesView(PlanningApiClient api)
    {
        _api=api;RowSpacing=12;RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        PlanningUi.Place(this,PlanningUi.Surface(PlanningUi.Stack(PlanningUi.Text("Equivalencias verificadas",20),PlanningUi.Row(_test,_name,_lab),_no,_reason,
            PlanningUi.Row(PlanningUi.Button("Guardar decisión",Save,true),PlanningUi.Button("Actualizar",Load),PlanningUi.Button("Revisar catálogo anterior",Legacy)),_message)),0);PlanningUi.Place(this,_list,1);
        _list.SelectionChanged+=(_,_)=>{if(_list.SelectedIndex>=0&&_list.SelectedIndex<_mappings.Count){var m=_mappings[_list.SelectedIndex];_test.Text=m.OriginalTest;_name.Text=m.Equivalence??"";_lab.Text=m.Laboratory??"";_no.IsChecked=m.NoProgram;}};
    }
    public async Task Load()
    {var result=await _api.Equivalences(CancellationToken.None);if(!result.IsSuccess){_message.Text=result.Error??"No se pudo cargar.";return;}_mappings=result.Value??[];_list.ItemsSource=_mappings.Select(m=>$"{m.OriginalTest} → {(m.NoProgram?"NO PROGRAMAR":m.Equivalence)} · {m.Laboratory}").ToArray();}
    private async Task Save()
    {
        var old=_mappings.SingleOrDefault(m=>string.Equals(m.OriginalTest.Trim(),_test.Text.Trim(),StringComparison.OrdinalIgnoreCase));
        if(!await PlanningUi.Confirm(XamlRoot,"Guardar equivalencia",_no.IsChecked==true?"Se conservarán las pruebas y su historia con decisión NO PROGRAMAR. No equivale a una retención.":"Se aplicará la equivalencia a las solicitudes importadas coincidentes. Las pruebas manuales conservarán sus datos."))return;
        var result=await _api.SaveEquivalence(new(_test.Text,_name.Text,_lab.Text,_no.IsChecked==true,old?.Version,_reason.Text),CancellationToken.None);_message.Text=result.IsSuccess?"Decisión guardada y auditada.":result.Error??"No se pudo guardar.";if(result.IsSuccess)await Load();
    }
    private async Task Legacy()
    {
        var response=await _api.LegacyEquivalences(CancellationToken.None);if(!response.IsSuccess||response.Value is not {} preview){_message.Text=response.Error??"Catálogo anterior no disponible.";return;}
        var items=new ListView{MaxHeight=320,ItemsSource=preview.Items.Select(i=>$"{i.OriginalTest} → {(i.NoProgram?"NO PROGRAMAR":i.Equivalence)} · {i.Laboratory}").Concat(preview.Conflicts.Select(c=>"Revisión: "+c)).ToArray()};
        var reason=PlanningUi.Field("Motivo y validación del catálogo",max:1000);
        var dialog=new ContentDialog{XamlRoot=XamlRoot,Title=$"Catálogo anterior · {preview.Items.Count} propuestas",Content=PlanningUi.Stack(PlanningUi.Text($"Se omiten {preview.Conflicts.Count} conflictos. Las decisiones ya existentes se conservan.",13,true),items,reason),PrimaryButtonText="Agregar equivalencias revisadas",CloseButtonText="Cancelar",DefaultButton=ContentDialogButton.Close};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        var saved=await _api.ImportLegacyEquivalences(new(preview.Fingerprint,reason.Text),CancellationToken.None);_message.Text=saved.IsSuccess?"Catálogo incorporado con procedencia y auditoría.":saved.Error??"No se pudo incorporar.";if(saved.IsSuccess)await Load();
    }
}
