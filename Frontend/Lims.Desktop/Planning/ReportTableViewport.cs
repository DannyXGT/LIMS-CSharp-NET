using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Lims.Desktop.Planning;

/// <summary>One native scroll owner, with a clipped header above the virtualized rows.</summary>
internal sealed class ReportTableViewport : Grid
{
    private readonly ListView _list;
    private readonly TranslateTransform _headerOffset=new();
    private double _lastVerticalOffset=double.NaN;
    public ScrollViewer? Scroll {get;private set;}
    public event Action? Scrolled;
    public ReportTableViewport(FrameworkElement header,ListView list)
    {
        _list=list;
        RowDefinitions.Add(new(){Height=GridLength.Auto});
        RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        header.RenderTransform=_headerOffset;
        header.HorizontalAlignment=HorizontalAlignment.Left;
        var clip=new RectangleGeometry();
        var head=new Grid{Clip=clip,MinHeight=40,Background=PlanningUi.Brush("LimsSurfaceSecondaryBrush")};
        head.Children.Add(header);
        head.SizeChanged+=(_,_)=>clip.Rect=new Rect(0,0,head.ActualWidth,head.ActualHeight);
        Children.Add(head);SetRow(list,1);Children.Add(list);
        list.HorizontalAlignment=HorizontalAlignment.Stretch;
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollMode(list,ScrollMode.Enabled);
        ScrollViewer.SetVerticalScrollMode(list,ScrollMode.Enabled);
        ScrollViewer.SetIsHorizontalRailEnabled(list,true);
        ScrollViewer.SetIsVerticalRailEnabled(list,true);
        list.Loaded+=(_,_)=>Attach();
        list.Unloaded+=(_,_)=>Detach();
    }
    private void Attach()
    {
        Detach();Scroll=FindScroll(_list);
        if(Scroll is null)return;
        Scroll.ViewChanged+=ViewChanged;SyncHeader();
    }
    private void Detach(){if(Scroll is not null)Scroll.ViewChanged-=ViewChanged;Scroll=null;_lastVerticalOffset=double.NaN;}
    private void ViewChanged(object? sender,ScrollViewerViewChangedEventArgs args)
    {
        SyncHeader();
        var vertical=Scroll?.VerticalOffset??0;
        if(vertical==_lastVerticalOffset)return;
        _lastVerticalOffset=vertical;Scrolled?.Invoke();
    }
    private void SyncHeader()=>_headerOffset.X=-(Scroll?.HorizontalOffset??0);
    public void Reveal(double left,double right)
    {
        if(Scroll is not {} scroll)return;
        if(left<scroll.HorizontalOffset)scroll.ChangeView(left,null,null,true);
        else if(right>scroll.HorizontalOffset+scroll.ViewportWidth)scroll.ChangeView(right-scroll.ViewportWidth,null,null,true);
    }
    private static ScrollViewer? FindScroll(DependencyObject parent)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is ScrollViewer scroll)return scroll;
            if(FindScroll(child) is{} found)return found;
        }
        return null;
    }
}
