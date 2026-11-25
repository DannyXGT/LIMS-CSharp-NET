using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Lims.Desktop.Planning;

namespace Lims.Desktop.Views;

public sealed partial class ShellPage
{
    private ContentControl? _planningHost;
    private PlanningWorkspacePage? _planningPage;
    private NavigationViewItem? _planningItem;
    private readonly List<NavigationViewItem> _planningSections=[];
    private readonly Dictionary<NavigationViewItem,PlanningWorkspacePage.Section> _planningTargets=[];
    private readonly DispatcherTimer _planningAccessTimer=new(){Interval=TimeSpan.FromSeconds(15)};
    private bool _checkingPlanning;
    public event Action<bool>? PlanningVisibilityChanged;
    public void AttachPlanningWorkspace(PlanningWorkspacePage page,PlanningApiClient api)
    {
        _planningPage=page;
        _planningHost=new ContentControl{Content=page,Visibility=Visibility.Collapsed,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,Background=PlanningSheetGrid.Color("#0B1119")};
        Grid.SetRow(_planningHost,1);Workspace.Children.Add(_planningHost);
        _planningItem=AddPlanningNavigationItem("Consulta de Jobs","planning");
        WorkspaceChanged+=()=>
        {
            if(SelectedWorkspace!="planning")
            {
                _planningHost.Visibility=Visibility.Collapsed;
                PlanningVisibilityChanged?.Invoke(false);
            }
        };
        MainNavigation.SelectionChanged+=(sender,args)=>
        {
            if(_restoringNavigation)return;
            if(args.SelectedItemContainer is not NavigationViewItem selected)return;
            if(selected.Tag is string tag&&!IsWorkspaceRouteAllowed(tag))return;
            if(ReferenceEquals(selected,_planningItem)||_planningTargets.ContainsKey(selected))
            {
                if(!CanUsePlanningNavigationItem(selected)){RestoreWorkspaceSelection();return;}
                _workspaceSelection=selected;
                if(ReferenceEquals(selected,_planningItem))OpenPlanningWorkspace();
                else {OpenPlanningWorkspace(false);_=page.ShowSection(_planningTargets[selected]);}
                return;
            }
            if(!_planningSections.Contains(selected)&&selected.Tag is string){_planningHost.Visibility=Visibility.Collapsed;PlanningVisibilityChanged?.Invoke(false);}
        };
        page.SectionsChanged+=()=>
        {
            foreach(var item in _planningSections)RemovePlanningNavigationItem(item);_planningSections.Clear();_planningTargets.Clear();
            foreach(var section in page.Sections.Skip(1))
            {
                var nav=AddPlanningNavigationItem(section.Title,"planning-section");
                _planningTargets.Add(nav,section);
                _planningSections.Add(nav);
            }
            UpdateNavigationGroups(MainNavigation.IsPaneOpen);
        };
        async Task Check()
        {
            if(_checkingPlanning)return;_checkingPlanning=true;
            try{var access=await api.Workspaces(CancellationToken.None);bool allowed=access.IsSuccess&&access.Value?.Planning==true;SetPlanningWorkspaceAccess(allowed);if(!allowed&&_planningHost.Visibility==Visibility.Visible){page.Clear();_planningHost.Visibility=Visibility.Collapsed;}}
            finally{_checkingPlanning=false;}
        }
        Loaded+=async(_,_)=>{await Check();_planningAccessTimer.Start();};Unloaded+=(_,_)=>_planningAccessTimer.Stop();_planningAccessTimer.Tick+=async(_,_)=>await Check();
    }
    public void OpenPlanningWorkspace(bool jobs=true)
    {
        if(SelectedWorkspace!="planning"||_planningHost is null||_planningPage is null||_planningItem is null)return;
        _navigationRevision++;HideAllSections();StandardsNewButton.Visibility=PreparationsNewButton.Visibility=Visibility.Collapsed;
        MainNavigation.Header=null;MainNavigation.AlwaysShowHeader=false;MainNavigation.IsPaneOpen=false;
        _planningHost.Visibility=Visibility.Visible;PlanningVisibilityChanged?.Invoke(true);
        if(jobs)
        {
            _restoringNavigation=true;MainNavigation.SelectedItem=_planningItem;_workspaceSelection=_planningItem;_restoringNavigation=false;
            if(_planningPage.Sections.FirstOrDefault()is{} first)_= _planningPage.ShowSection(first);
        }
    }
    public void AttachWorkspaceSelector(Action open)
    {
        var item=CreateWorkspaceSwitchNavigationItem();
        NavigationViewItem? previous=MainNavigation.SelectedItem as NavigationViewItem;
        MainNavigation.SelectionChanged+=(_,args)=>{if(args.SelectedItemContainer is NavigationViewItem selected&&!ReferenceEquals(selected,item))previous=selected;};
        MainNavigation.ItemInvoked+=async(_,args)=>
        {
            if(!ReferenceEquals(args.InvokedItemContainer,item))return;
            _restoringNavigation=true;MainNavigation.SelectedItem=previous??HomeItem;_restoringNavigation=false;
            if(_permissionsPage is not null&&PermissionsHost.Visibility==Microsoft.UI.Xaml.Visibility.Visible&&!await _permissionsPage.ConfirmDiscardAsync())return;
            open();
        };
        MainNavigation.FooterMenuItems.Add(item);
    }
    public void OpenAdministrationWorkspace(){if(SelectedWorkspace=="administration")MainNavigation.SelectedItem=PermissionsItem;}
}
