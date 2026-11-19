using System.Globalization;
using Lims.Contracts.Planning;
using Lims.DesignSystem.Controls;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Lims.Desktop.Planning;

public sealed partial class PlanningDossierWindow : Window, IDisposable
{
    private readonly PlanningApiClient _api;
    private readonly Guid _id;
    private readonly IReadOnlyList<string> _permissions;
    private readonly Grid _layout=PlanningUi.Rows(GridLength.Auto,GridLength.Auto,new(1,GridUnitType.Star));
    private readonly TextBlock _message=PlanningUi.Text("Cargando expediente...",13,true);
    private readonly ContentControl _body=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromSeconds(15)};
    private readonly CancellationTokenSource _lifetime=new();
    private JobDossier? _dossier;
    private bool _loading;
    private bool _disposed;
    private readonly TextBlock _title=PlanningUi.Text("Estado del JOB",24);
    private readonly ProgressBar _progress=new(){IsIndeterminate=false,Height=3,Visibility=Visibility.Collapsed};
    private readonly ContentControl _identity=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch};
    private readonly Button _refresh;
    private int _selectedTab;
    public event Func<Task>? ControlChanged;
    public Task RefreshData()=>Load();
    public PlanningDossierWindow(PlanningApiClient api,Guid id,IReadOnlyList<string> permissions)
    {
        _api=api;_id=id;_permissions=permissions;Title="LIMS · Expediente del JOB";_layout.Padding=new Thickness(24);_layout.Background=PlanningUi.Brush("LimsBackgroundBrush");
        _title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
        _refresh=ConsultationVisuals.Action("Actualizar",LimsIconKind.Refresh,Load);
        var close=ConsultationVisuals.Action("Cerrar",LimsIconKind.Cancel,async()=>{if(Motion.Enabled)await Motion.ExitAsync(_layout,milliseconds:90,scale:1);Close();});
        var caption=PlanningUi.Stack(PlanningUi.Text("Planificación / Estado del JOB",12,true),_title);caption.Spacing=3;
        var header=PlanningUi.Stack(ConsultationVisuals.Split(caption,PlanningUi.Row(_refresh,close)),_identity);
        var surface=ConsultationVisuals.Surface(header);surface.Background=ConsultationVisuals.Panel(true);surface.Padding=new Thickness(20,12,20,16);
        PlanningUi.Place(_layout,surface,0);PlanningUi.Place(_layout,PlanningUi.Stack(_progress,_message),1);PlanningUi.Place(_layout,_body,2);Content=_layout;
        ConsultationVisuals.FitWindow(this,1100,860);AppWindow.SetIcon("Assets/Branding/LIMS.ico");if(AppWindow.Presenter is OverlappedPresenter p){p.PreferredMinimumWidth=780;p.PreferredMinimumHeight=600;}
        _layout.Loaded+=async(_,_)=>{await Load();if(Motion.Enabled)Motion.Enter(_layout,y:4,milliseconds:140);_timer.Start();};_timer.Tick+=async(_,_)=>await CheckAccess();
        Closed+=(_,_)=>Dispose();
    }
    private async Task CheckAccess()
    {
        try{var access=await _api.Workspaces(_lifetime.Token);if(!access.IsSuccess||access.Value is not {Planning:true}){_body.Content=null;_dossier=null;_message.Text=access.Error??"Acceso revocado. Cierre el expediente.";}}
        catch(OperationCanceledException)when(_lifetime.IsCancellationRequested){ }
    }
    private async Task Load()
    {
        if(_loading)return;_loading=true;_refresh.IsEnabled=false;_progress.Visibility=Visibility.Visible;_progress.IsIndeterminate=true;_message.Text="Actualizando expediente…";
        try
        {
            var response=await _api.Dossier(_id,_lifetime.Token);if(_lifetime.IsCancellationRequested)return;
            if(!response.IsSuccess||response.Value is not {} dossier){_body.Content=null;_dossier=null;_message.Text=response.Error??"Expediente no disponible.";return;}
            _dossier=dossier;Title=$"Estado del JOB · {dossier.Job}";_title.Text=dossier.Job;_message.Text="Expediente actualizado";
            _identity.Content=GeneralInformation(dossier);
            Build(dossier);
        }
        catch(OperationCanceledException)when(_lifetime.IsCancellationRequested){ }
        finally{_loading=false;_refresh.IsEnabled=true;_progress.IsIndeterminate=false;_progress.Visibility=Visibility.Collapsed;}
    }
    private void Build(JobDossier dossier)=>BuildStatus(dossier);
    private static TextBlock Selectable(string text){var block=PlanningUi.Text(text,12,true);block.IsTextSelectionEnabled=true;return block;}
    private async Task Edit()
    {
        if(_dossier is not {} dossier)return;var w=dossier.Wip;
        var buyer=PlanningUi.Field("Buyer / marca",w.Buyer);var vendor=PlanningUi.Field("Vendor",w.Vendor);var service=PlanningUi.Field("Service",w.Service);
        var inDate=PlanningUi.Field("In Date · yyyy-MM-dd",w.InDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),10);var due=PlanningUi.Field("Due Date · yyyy-MM-dd",w.DueDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),10);
        var amount=PlanningUi.Field("Importe fuente (punto decimal)",w.Amount?.ToString(CultureInfo.InvariantCulture),24);var currency=PlanningUi.Field("Moneda fuente",w.Currency,20);var usd=PlanningUi.Field("USD verificado (vacío = desconocido)",w.Usd?.ToString(CultureInfo.InvariantCulture),24);
        var evidence=PlanningUi.Field("Fuente verificable de una corrección USD",max:1000);var reason=PlanningUi.Field("Motivo obligatorio",max:1000);
        var dialog=new ContentDialog{XamlRoot=_layout.XamlRoot,Title=$"Editar WIP · {dossier.Job}",Content=new ScrollViewer{MaxHeight=480,Content=PlanningUi.Stack(buyer,vendor,service,PlanningUi.Row(inDate,due),PlanningUi.Row(amount,currency),usd,evidence,reason)},PrimaryButtonText="Guardar WIP",CloseButtonText="Cancelar",DefaultButton=ContentDialogButton.Close};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        try
        {
            var values=new WipValues(PlanningUi.Optional(buyer.Text),PlanningUi.Optional(vendor.Text),PlanningUi.Optional(service.Text),Date(inDate.Text),Date(due.Text),Money(amount.Text),PlanningUi.Optional(currency.Text)?.ToUpperInvariant(),Money(usd.Text));
            var saved=await _api.SaveWip(_id,new(dossier.Version,values,reason.Text,evidence.Text),_lifetime.Token);if(saved.IsSuccess){await Load();if(ControlChanged is{} changed)await changed();}else _message.Text=saved.Error??"No se pudo guardar.";
        }
        catch(FormatException e){_message.Text=e.Message;}
    }
    private static DateOnly? Date(string text)=>string.IsNullOrWhiteSpace(text)?null:DateOnly.TryParseExact(text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)?date:throw new FormatException("Fecha inválida; use yyyy-MM-dd.");
    private static decimal? Money(string text)=>string.IsNullOrWhiteSpace(text)?null:decimal.TryParse(text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value)?value:throw new FormatException("Importe inválido; use punto decimal sin separadores de miles.");
    private async Task Manual()
    {
        if(_dossier is not {} dossier)return;var test=PlanningUi.Field("Prueba original",max:2048);var equivalent=PlanningUi.Field("Equivalencia verificada");var lab=PlanningUi.Field("Laboratorio");var reason=PlanningUi.Field("Motivo y fuente de asignación",max:1000);
        var dialog=new ContentDialog{XamlRoot=_layout.XamlRoot,Title="Agregar prueba manual",Content=PlanningUi.Stack(test,equivalent,lab,reason),PrimaryButtonText="Guardar prueba",CloseButtonText="Cancelar",DefaultButton=ContentDialogButton.Close};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        var saved=await _api.Manual(_id,new(dossier.Version,test.Text,equivalent.Text,lab.Text,reason.Text),_lifetime.Token);if(saved.IsSuccess){await Load();if(ControlChanged is{} changed)await changed();}else _message.Text=saved.Error??"No se pudo guardar.";
    }
    private async Task Decide(PlanningIncident incident)
    {
        var state=new ComboBox{Header="Decisión",ItemsSource=new[]{"Retener","Volver a revisión"},SelectedIndex=0};var reason=PlanningUi.Field("Motivo obligatorio",max:1000);
        var dialog=new ContentDialog{XamlRoot=_layout.XamlRoot,Title=PlanningLabels.Category(incident.Kind),Content=PlanningUi.Stack(state,reason),PrimaryButtonText="Guardar decisión",CloseButtonText="Cancelar",DefaultButton=ContentDialogButton.Close};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        var saved=await _api.Incident(incident.Id,new(incident.Version,state.SelectedIndex switch{0=>"Held",2=>"Resolved",_=>"Pending"},reason.Text),_lifetime.Token);if(saved.IsSuccess){await Load();if(ControlChanged is{} changed)await changed();}else _message.Text=saved.Error??"No se pudo guardar.";
    }
    public void Dispose(){if(_disposed)return;_disposed=true;_timer.Stop();_lifetime.Cancel();_lifetime.Dispose();_body.Content=null;_dossier=null;}
}
