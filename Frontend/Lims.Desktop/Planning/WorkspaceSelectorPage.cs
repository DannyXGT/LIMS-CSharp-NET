using Lims.Desktop.Services;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Planning;

public sealed class WorkspaceSelectorPage : Page, IDisposable
{
    private readonly Grid _choices=new(){ColumnSpacing=14,RowSpacing=14};
    private readonly TextBlock _message=PlanningUi.Text("Verificando espacios autorizados...",14,true);
    private readonly PlanningApiClient _api;
    private readonly Action<string> _open;
    private CancellationTokenSource? _load;
    public WorkspaceSelectorPage(PlanningApiClient api,ISessionService session,Action<string> open,Func<Task> logout)
    {
        _api=api;_open=open;Background=PlanningUi.Brush("LimsBackgroundBrush");
        _choices.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});_choices.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        _choices.RowDefinitions.Add(new(){Height=GridLength.Auto});_choices.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var stack=PlanningUi.Stack(PlanningUi.Text("LIMS",16,true),PlanningUi.Text("Elegir espacio de trabajo",30),
            PlanningUi.Text("Continúa con tu sesión y los permisos de tu cuenta.",14,true),_message,_choices,
            PlanningUi.Row(PlanningUi.Button("Volver a verificar",Load),PlanningUi.Button("Cerrar sesión",logout)));
        stack.MaxWidth=860;stack.HorizontalAlignment=HorizontalAlignment.Center;stack.VerticalAlignment=VerticalAlignment.Center;stack.Margin=new Thickness(32);
        Content=new ScrollViewer{Content=stack,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        Loaded+=async(_,_)=>{_message.Text=$"{session.Profile?.Name} · verificando acceso...";await Load();Motion.Enter(stack,y:6,milliseconds:160);};
        Unloaded+=(_,_)=>{_load?.Cancel();_choices.Children.Clear();};
    }
    private async Task Load()
    {
        _load?.Cancel();_load?.Dispose();_load=new();var ct=_load.Token;_choices.Children.Clear();
        try
        {
            var result=await _api.Workspaces(ct);if(ct.IsCancellationRequested)return;
            if(!result.IsSuccess||result.Value is not {} access){_message.Text=result.Error??"No se pudo verificar el acceso.";return;}
            _message.Text=access.Notice??"Selecciona un espacio autorizado.";
            if(access.Laboratory)Choice("Laboratorio","Estándares, Preparaciones y módulos actuales.","laboratory");
            if(access.Planning)Choice("Planificación","Consulta de Jobs, expediente WIP e importaciones.","planning");
            if(access.Laboratory)Choice("Laboratorio Analítico","Acceso a los módulos de laboratorio existentes según tus permisos.","laboratory");
            if(access.Administration)Choice("Usuarios y Administración","Gestión de permisos existente. Los accesos de Planificación se administran dentro de ese espacio.","administration");
            if(_choices.Children.Count==0)_message.Text="Tu cuenta no tiene espacios habilitados. Solicita una revisión al administrador.";
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){ }
    }
    private void Choice(string title,string description,string target)
    {
        var button=PlanningUi.Button(title,()=>{_open(target);return Task.CompletedTask;});button.HorizontalAlignment=HorizontalAlignment.Stretch;
        var surface=PlanningUi.Surface(PlanningUi.Stack(button,PlanningUi.Text(description,13,true)));Grid.SetRow(surface,_choices.Children.Count/2);Grid.SetColumn(surface,_choices.Children.Count%2);_choices.Children.Add(surface);
    }
    public void Dispose(){_load?.Cancel();_load?.Dispose();_load=null;}
}
