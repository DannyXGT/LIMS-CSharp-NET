using Lims.Contracts.Planning;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Planning;

public sealed class PlanningWorkspacePage : Page, IDisposable
{
    private readonly PlanningApiClient _api;
    private readonly Action _back;
    private readonly ContentControl _content=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
    private readonly StackPanel _tabs=new(){Orientation=Orientation.Horizontal,Spacing=8};
    private readonly TextBlock _message=PlanningUi.Text("Verificando permisos...",13,true);
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromSeconds(15)};
    private readonly PlanningJobsView _jobs;
    private readonly PlanningImportsView _imports;
    private readonly PlanningReportListView _reportList;
    private readonly List<PlanningDossierWindow> _windows=[];
    private CancellationTokenSource? _access;
    private WorkspaceAccess? _current;
    private bool _refreshing;
    public sealed record Section(string Title,FrameworkElement View,Func<Task> Load);
    public List<Section> Sections{get;}=[];
    public event Action? SectionsChanged;
    public async Task ShowSection(Section section){_content.Content=section.View;Motion.Enter(section.View,y:4,milliseconds:140);await section.Load();}
    public PlanningWorkspacePage(PlanningApiClient api,Action back,Func<int?>? userId=null)
    {
        _api=api;_back=back;Background=PlanningUi.Brush("LimsBackgroundBrush");
        _jobs=new(api,OpenDossier,()=>_current?.Permissions??[]);_imports=new(api,OpenDossier,()=>_current?.Permissions??[]);
        _reportList=new(api,OpenDossier,()=>_current?.Permissions??[],()=>_current?.Administration==true,userId);
        _reportList.Changed+=async()=>{await _jobs.RefreshPreservingSelection();foreach(var window in _windows.ToArray())await window.RefreshData();};
        _imports.OpenImportedJobs+=async id=>{_content.Content=_jobs;await _jobs.OpenBatch(id);};
        _imports.Applied+=async()=>{await _jobs.RefreshPreservingSelection();foreach(var window in _windows.ToArray())await window.RefreshData();};
        var layout=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star));layout.RowSpacing=0;
        PlanningUi.Place(layout,_message,0);PlanningUi.Place(layout,_content,1);Content=layout;
        Loaded+=async(_,_)=>{await RefreshAccess();_timer.Start();};
        Unloaded+=(_,_)=>{_timer.Stop();_access?.Cancel();_jobs.Cancel();_imports.Cancel();};
        _timer.Tick+=async(_,_)=>await RefreshAccess();
    }
    private async Task RefreshAccess()
    {
        if(_refreshing)return;_refreshing=true;
        _access?.Cancel();_access?.Dispose();_access=new();var ct=_access.Token;
        try
        {
            var result=await _api.Workspaces(ct);if(ct.IsCancellationRequested)return;
            if(!result.IsSuccess||result.Value is not {Planning:true} access){Clear();_message.Text=result.Error??"Ya no tiene acceso a Planificación. Vuelva a Espacios.";return;}
            var signature=string.Join('|',access.Permissions.OrderBy(p=>p,StringComparer.Ordinal));
            var changed=_current is null||signature!=string.Join('|',_current.Permissions.OrderBy(p=>p,StringComparer.Ordinal));_current=access;
            if(changed)
            {
                _tabs.Children.Clear();Sections.Clear();Tab("Consulta de Jobs",_jobs,()=>_jobs.Load());
                Tab("Report List / Digitación",_reportList,()=>_reportList.Load());
                if(access.Permissions.Contains(PlanningPermissions.Import,StringComparer.Ordinal))Tab("Importaciones",_imports,()=>_imports.Load());
                Tab("Bandejas de revisión",_imports,()=>_imports.OpenReviewWorkspace());
                if(access.Permissions.Contains(PlanningPermissions.Equivalences,StringComparer.Ordinal))
                {var view=new PlanningEquivalencesView(_api);Tab("Equivalencias",view,()=>view.Load());}
                if(access.Administration){var view=new PlanningAccessView(_api);Tab("Accesos",view,()=>view.Load());}
                foreach(var window in _windows.ToArray())window.Close();
                _content.Content=_jobs;SectionsChanged?.Invoke();await _jobs.Load();
            }
            _message.Text="";_message.Visibility=Visibility.Collapsed;
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){ }
        finally{_refreshing=false;}
    }
    private void Tab(string title,FrameworkElement view,Func<Task> load)=>Sections.Add(new(title,view,load));
    private Task OpenDossier(Guid id)
    {
        if(_current is null)return Task.CompletedTask;
        var window=new PlanningDossierWindow(_api,id,_current.Permissions);window.ControlChanged+=()=>ReferenceEquals(_content.Content,_jobs)?_jobs.RefreshPreservingSelection():ReferenceEquals(_content.Content,_imports)?_imports.Load():Task.CompletedTask;_windows.Add(window);PlanningUi.Track(window);window.Closed+=(_,_)=>_windows.Remove(window);window.Activate();return Task.CompletedTask;
    }
    public void Clear()
    {
        _timer.Stop();_access?.Cancel();_jobs.Cancel();_imports.Cancel();_jobs.Clear();_imports.Clear();_reportList.Clear();_content.Content=null;_tabs.Children.Clear();_current=null;
        Sections.Clear();SectionsChanged?.Invoke();_message.Visibility=Visibility.Visible;
        foreach(var window in _windows.ToArray())window.Close();
    }
    public void Dispose(){Clear();_access?.Dispose();_access=null;_jobs.Dispose();_imports.Dispose();_reportList.Dispose();}
}
