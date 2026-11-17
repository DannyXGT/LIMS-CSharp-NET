using Lims.Desktop.Services;
using Lims.Desktop.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Planning;

public sealed class WorkspaceNavigationService(PlanningApiClient api,ISessionService session,IAuthenticationGateway authentication) : INavigationService, IDisposable
{
    private ContentControl? _host;
    private FrameworkElement? _login;
    private FrameworkElement? _laboratory;
    private WorkspaceSelectorPage? _selector;
    private PlanningWorkspacePage? _planning;
    public void Initialize(ContentControl host,FrameworkElement loginView,FrameworkElement shellView)
    {
        _host=host;_login=loginView;_laboratory=shellView;
        _selector=new WorkspaceSelectorPage(api,session,Open,async()=>{await authentication.LogoutAsync(CancellationToken.None);ShowLogin();});
        _planning=new PlanningWorkspacePage(api,()=>ShowAuthenticatedShell());
        if(shellView is ShellPage shell)shell.AttachWorkspaceSelector(ShowAuthenticatedShell);
        if(shellView is ShellPage planningShell)
        {
            planningShell.AttachPlanningWorkspace(_planning,api);
            planningShell.PlanningVisibilityChanged+=visible=>LaboratoryVisibilityChanged?.Invoke(!visible);
        }
        host.Loaded+=(_,_)=>{if(AppWindowForHost is { } window)PlanningUi.Track(window);};
    }
    public Window? AppWindowForHost { get; set; }
    public event Action<bool>? LaboratoryVisibilityChanged;
    public void ShowLogin(){_planning?.Clear();Navigate(_login);}
    public void ShowAuthenticatedShell(){_planning?.Clear();Navigate(_selector);}
    private void Navigate(FrameworkElement? page)
    {if(_host is null||page is null)throw new InvalidOperationException("La navegación no se ha inicializado.");_host.Content=page;LaboratoryVisibilityChanged?.Invoke(ReferenceEquals(page,_laboratory));}
    private void Open(string workspace)
    {
        if(workspace=="planning"){Navigate(_laboratory);if(_laboratory is ShellPage shell)shell.OpenPlanningWorkspace();}
        else {Navigate(_laboratory);if(workspace=="administration"&&_laboratory is ShellPage shell)shell.OpenAdministrationWorkspace();}
    }
    public void Dispose(){_planning?.Dispose();_selector?.Dispose();}
}
