using Lims.Contracts.Planning;
using Lims.DesignSystem.Controls;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Planning;

/// <summary>Authorized manual assignment from the existing catalogue; Summary remains read only.</summary>
public sealed class PlanningManualTestWindow : Window, IDisposable
{
    private readonly PlanningApiClient _api;
    private readonly Guid _episode;
    private readonly Func<Task> _changed;
    private JobDossier? _dossier;
    private readonly CancellationTokenSource _lifetime=new();
    private readonly Grid _layout=PlanningUi.Rows(GridLength.Auto,GridLength.Auto,new(1,GridUnitType.Star),GridLength.Auto);
    private readonly ContentControl _summary=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch};
    private readonly LimsSearchSelect _catalogue=new(){DisplayMemberPath=nameof(PlanningEquivalence.OriginalTest),PlaceholderText="Selecciona una prueba validada",SearchPlaceholder="Buscar nombre original…"};
    private readonly TextBlock _classification=PlanningUi.Text("El nombre original, equivalencia y laboratorio se conservan por separado.",13,true);
    private readonly TextBlock _message=PlanningUi.Text("Cargando Summary y catálogo…",12,true);
    private readonly TextBox _reason=PlanningUi.Field("Motivo y fuente de asignación",max:1000);
    private readonly Button _save;
    private bool _busy,_disposed;
    public PlanningManualTestWindow(PlanningApiClient api,Guid episode,Func<Task> changed)
    {
        _api=api;_episode=episode;_changed=changed;Title="Agregar pruebas · LIMS";
        _layout.Padding=new(24);_layout.RowSpacing=12;_layout.Background=PlanningUi.Brush("LimsBackgroundBrush");
        var header=new Views.PreparationEditorHeader{Heading="Agregar pruebas",Description="Asignación manual autorizada · catálogo de equivalencias del LIMS"};
        PlanningUi.Place(_layout,header,0);PlanningUi.Place(_layout,_summary,1);
        AutomationProperties.SetName(_catalogue,"Prueba del catálogo");AutomationProperties.SetName(_reason,"Motivo y fuente de asignación");
        _catalogue.SelectionChanged+=(_,_)=>{if(_catalogue.SelectedItem is PlanningEquivalence map)_classification.Text=$"Equivalencia: {map.Equivalence} · Laboratorio: {map.Laboratory}";if(_save is{} save)save.IsEnabled=!_busy&&_catalogue.SelectedItem is not null;};
        var form=PlanningUi.Stack(PlanningUi.Text("Prueba del catálogo",13),_catalogue,_classification,_reason,_message);form.Spacing=10;
        PlanningUi.Place(_layout,new ScrollViewer{Content=form,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},2);
        _save=PlanningUi.Button("Guardar prueba",Save,true,()=>!_busy&&_catalogue.SelectedItem is not null);_save.IsEnabled=false;
        var close=PlanningUi.Button("Cerrar",()=>{Close();return Task.CompletedTask;});
        var footer=PlanningUi.Row(PlanningUi.Button("Actualizar catálogo",Load),close,_save);footer.HorizontalAlignment=HorizontalAlignment.Right;PlanningUi.Place(_layout,footer,3);
        Content=_layout;ConsultationVisuals.FitWindow(this,980,700);AppWindow.SetIcon("Assets/Branding/LIMS.ico");if(AppWindow.Presenter is OverlappedPresenter p){p.PreferredMinimumWidth=680;p.PreferredMinimumHeight=530;}
        _layout.Loaded+=async(_,_)=>{if(Motion.Enabled)Motion.Enter(_layout,y:4,milliseconds:140);await Load();};Closed+=(_,_)=>Dispose();
    }
    private async Task Load()
    {
        if(_busy)return;_busy=true;
        try
        {
            var dossierTask=_api.Dossier(_episode,_lifetime.Token);var catalogueTask=_api.Equivalences(_lifetime.Token);await Task.WhenAll(dossierTask,catalogueTask);
            if(_lifetime.IsCancellationRequested)return;var dossier=await dossierTask;var catalogue=await catalogueTask;
            if(!dossier.IsSuccess||dossier.Value is null){_message.Text=dossier.Error??"Expediente no disponible.";return;}
            _dossier=dossier.Value;Title="Agregar pruebas · "+_dossier.Job;var w=_dossier.Wip;
            var details=PlanningUi.Stack(PlanningUi.Text(_dossier.Job,20),PlanningUi.Text($"Buyer: {w.Buyer??"No informado"} · Vendor: {w.Vendor??"No informado"}",13),PlanningUi.Text($"Ingreso: {w.InDate:yyyy-MM-dd} · Due Date: {w.DueDate:yyyy-MM-dd} · Service: {w.Service??"No informado"} · USD: {w.Usd?.ToString("N2",System.Globalization.CultureInfo.InvariantCulture)??"No informado"}",12,true),PlanningUi.Text($"Estado TIPS: {_dossier.Official?.SampleStatus??_dossier.Official?.JobStatus??"No informado"} · {_dossier.Tests.Count} pruebas guardadas",12,true));details.Spacing=4;
            _summary.Content=PlanningUi.Surface(details);
            if(!catalogue.IsSuccess){_message.Text=catalogue.Error??"Catálogo no disponible.";return;}
            _catalogue.ItemsSource=(catalogue.Value??[]).Where(m=>!m.NoProgram&&!string.IsNullOrWhiteSpace(m.Equivalence)&&!string.IsNullOrWhiteSpace(m.Laboratory)).ToArray();_catalogue.SelectedItem=null;
            _message.Text="Selecciona la prueba y documenta su asignación. Esta operación no modifica el Summary ni marca digitación.";
        }
        catch(OperationCanceledException)when(_lifetime.IsCancellationRequested){ }
        finally{_busy=false;}
    }
    private async Task Save()
    {
        if(_busy||_dossier is not{} dossier||_catalogue.SelectedItem is not PlanningEquivalence map)return;
        if(string.IsNullOrWhiteSpace(_reason.Text)){_message.Text="Escribe el motivo y la fuente de la asignación.";return;}
        var existing=dossier.Tests.Where(t=>string.Equals(t.OriginalTest?.Trim(),map.OriginalTest.Trim(),StringComparison.OrdinalIgnoreCase)).ToArray();
        if(existing.Length>0&&!await PlanningUi.Confirm(_layout.XamlRoot,"Verificar nueva ejecución",
            $"Este episodio ya tiene {existing.Length} ejecución(es) de {map.OriginalTest}. Confirma únicamente si corresponde una repetición o reanálisis distinto. Para vincular la fuente a una ejecución existente usa Conciliar identidad. ¿Crear otra ejecución con identidad propia?"))return;
        _busy=true;_catalogue.IsEnabled=false;_message.Text="Guardando la prueba y su auditoría…";
        try
        {
            var result=await _api.Manual(_episode,new(dossier.Version,map.OriginalTest,map.Equivalence!,map.Laboratory!,_reason.Text,map.Version),_lifetime.Token);
            if(_disposed)return;if(!result.IsSuccess){_message.Text=result.Error??"No se pudo guardar.";return;}
            _busy=false;await Load();await _changed();_reason.Text="";_message.Text="Prueba guardada en PostgreSQL con identidad propia, origen manual y auditoría.";
        }
        catch(OperationCanceledException)when(_lifetime.IsCancellationRequested){ }
        finally{_busy=false;_catalogue.IsEnabled=true;}
    }
    public void Dispose(){if(_disposed)return;_disposed=true;_lifetime.Cancel();_lifetime.Dispose();}
}
