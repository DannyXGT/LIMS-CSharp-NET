using Lims.Contracts.Planning;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Lims.Desktop.Planning;

internal sealed class PlanningClosuresPage : Grid,IDisposable
{
    private readonly PlanningClosuresView _view;
    public event Func<Task>? Changed;
    public PlanningClosuresPage(PlanningApiClient api,Func<Guid,Task> dossier,Func<IReadOnlyList<string>> permissions)
    {
        RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});RowSpacing=12;
        _view=new(api,dossier,()=>permissions().Contains(PlanningPermissions.ReportReview,StringComparer.Ordinal),actionsInHeader:true);
        var title=PlanningUi.Text("Cierres",25);title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
        AutomationProperties.SetHeadingLevel(title,Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1);
        var subtitle=PlanningUi.Text("Seguimiento de JOBs con todas sus pruebas digitadas.",12,true);
        var captions=PlanningUi.Stack(title,subtitle);captions.Spacing=4;captions.VerticalAlignment=VerticalAlignment.Center;
        var fill=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(1,1)};fill.GradientStops.Add(new(){Color=PlanningSheetGrid.Color("#18598D").Color,Offset=0});fill.GradientStops.Add(new(){Color=PlanningSheetGrid.Color("#102E49").Color,Offset=1});
        var icon=new Border{Width=56,Height=56,CornerRadius=new(10),BorderBrush=PlanningSheetGrid.Color("#167AC8"),BorderThickness=new(1),Background=fill,Child=new LimsIcon{Icon=LimsIconKind.Check,Size=30,Foreground=PlanningSheetGrid.Color("#D9F1FF")},VerticalAlignment=VerticalAlignment.Center};
        var identity=new Grid{ColumnSpacing=14};identity.ColumnDefinitions.Add(new(){Width=GridLength.Auto});identity.ColumnDefinitions.Add(new(){Width=GridLength.Auto});identity.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        identity.Children.Add(icon);var accent=new Border{Width=2,Height=54,Background=PlanningSheetGrid.Color("#178CDD"),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(accent,1);identity.Children.Add(accent);Grid.SetColumn(captions,2);identity.Children.Add(captions);
        var actions=_view.HeaderActions;actions.VerticalAlignment=VerticalAlignment.Center;
        var heading=ConsultationVisuals.Split(identity,actions);heading.Padding=new(16);heading.MinHeight=90;
        var hero=new Grid{Background=ConsultationVisuals.Panel(true),BorderBrush=ConsultationVisuals.Edge,BorderThickness=new(1),CornerRadius=new(12)};
        var decoration=new Canvas{IsHitTestVisible=false};decoration.Children.Add(new Polygon{Points=new(){new Point(0,0),new Point(630,0),new Point(562,108),new Point(0,108)},Fill=PlanningSheetGrid.Color("#124477"),Opacity=.24});hero.Children.Add(decoration);hero.Children.Add(heading);
        hero.SizeChanged+=(_,_)=>{hero.Clip=new RectangleGeometry{Rect=new Rect(0,0,hero.ActualWidth,hero.ActualHeight)};var compact=hero.ActualWidth<900;title.FontSize=compact?22:25;icon.Width=icon.Height=compact?46:56;};
        AutomationProperties.SetName(hero,"Encabezado de Cierres");PlanningUi.Place(this,hero,0);
        _view.Changed+=async()=>{if(Changed is {} changed)await changed();};PlanningUi.Place(this,_view,1);
    }
    public Task Load()=>_view.Load();
    public void Clear()=>_view.Clear();
    public void Dispose()=>_view.Dispose();
}
