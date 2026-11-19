using Lims.DesignSystem.Controls;
using Lims.Contracts.Planning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Markup;
using Windows.Foundation;

namespace Lims.Desktop.Planning;

/// <summary>The approved header, built from native WinUI elements, with live API counters.</summary>
internal sealed class ConsultationHeader : Grid
{
    private readonly Grid _content=new(){ColumnSpacing=16};
    private readonly StackPanel _metrics=new(){Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
    private readonly List<Grid> _metricIcons=[];
    private readonly List<TextBlock> _values=[];
    private readonly TextBlock _title=PlanningUi.Text("Consulta de Jobs",25);
    private readonly TextBlock _subtitle=PlanningUi.Text("Planificación / Consulta · Seguimiento de ejecuciones y estado de digitación.",12,true);
    private readonly Border _module;
    private TextBlock? _holdCaption;
    private bool _tableOnly;
    private bool _short;
    public ConsultationHeader(TextBlock[] counters,Button solo,Button status)
    {
        Background=ConsultationVisuals.Panel(true);BorderBrush=ConsultationVisuals.Edge;BorderThickness=new Thickness(1);CornerRadius=new CornerRadius(12);
        var root=new Grid();
        var band=new Polygon{Points=new(){new Point(0,0),new Point(630,0),new Point(562,108),new Point(0,108)},Fill=PlanningSheetGrid.Color("#124477"),Opacity=.24,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Stretch,IsHitTestVisible=false};
        var decoration=new Canvas{IsHitTestVisible=false};decoration.Children.Add(band);root.Children.Add(decoration);
        _content.Padding=new Thickness(16);_content.MinHeight=90;
        _content.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});_content.ColumnDefinitions.Add(new(){Width=GridLength.Auto});_content.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var moduleFill=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(1,1)};moduleFill.GradientStops.Add(new(){Color=PlanningSheetGrid.Color("#18598D").Color,Offset=0});moduleFill.GradientStops.Add(new(){Color=PlanningSheetGrid.Color("#102E49").Color,Offset=1});
        _module=new Border{Width=56,Height=56,CornerRadius=new CornerRadius(10),BorderBrush=PlanningSheetGrid.Color("#167AC8"),BorderThickness=new Thickness(1),Background=moduleFill,Child=new LimsIcon{Icon=LimsIconKind.Preparations,Size=30,Foreground=PlanningSheetGrid.Color("#D9F1FF")},VerticalAlignment=VerticalAlignment.Center};
        _title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;_title.TextWrapping=TextWrapping.NoWrap;
        var captions=PlanningUi.Stack(_title,_subtitle);captions.Spacing=4;captions.VerticalAlignment=VerticalAlignment.Center;
        var identity=new Grid{ColumnSpacing=14};identity.ColumnDefinitions.Add(new(){Width=GridLength.Auto});identity.ColumnDefinitions.Add(new(){Width=GridLength.Auto});identity.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        identity.Children.Add(_module);var accent=new Border{Width=2,Height=54,Background=PlanningSheetGrid.Color("#178CDD"),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(accent,1);identity.Children.Add(accent);Grid.SetColumn(captions,2);identity.Children.Add(captions);_content.Children.Add(identity);
        var icons=new[]{LimsIconKind.ViewDetail,LimsIconKind.Alerts,LimsIconKind.Alerts,LimsIconKind.Lock};
        var labels=new[]{"Registros","Pendientes","Vencidas","Pruebas On Hold (— JOBs)"};
        var colors=new[]{"#E9F5FF","#F8BA54","#F08A8A","#A9BDCF"};
        for(int i=0;i<4;i++)
        {
            if(i>0)_metrics.Children.Add(new Border{Width=1,Height=34,Background=ConsultationVisuals.Edge,Margin=new Thickness(2,0,2,0)});
            var icon=MetricIcon(i==0||i==3?0:2,icons[i],i==0?"#60BFFF":colors[i]);_metricIcons.Add(icon);
            var value=counters[i];value.FontSize=23;value.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;value.Foreground=PlanningSheetGrid.Color(colors[i]);_values.Add(value);
            var caption=PlanningUi.Text(labels[i],11,true);caption.TextWrapping=TextWrapping.NoWrap;
            if(i==3)_holdCaption=caption;
            var metric=PlanningUi.Row(icon,PlanningUi.Stack(value,caption));metric.Spacing=6;((StackPanel)metric.Children[1]).Spacing=1;metric.Margin=new Thickness(8,0,8,0);_metrics.Children.Add(metric);
        }
        Grid.SetColumn(_metrics,1);_content.Children.Add(_metrics);
        var actions=PlanningUi.Row(solo,status);actions.Spacing=8;actions.VerticalAlignment=VerticalAlignment.Center;actions.Padding=new Thickness(14,0,0,0);actions.BorderBrush=ConsultationVisuals.Edge;actions.BorderThickness=new Thickness(1,0,0,0);Grid.SetColumn(actions,2);_content.Children.Add(actions);
        root.Children.Add(_content);Children.Add(root);
        SizeChanged+=(_,_)=>{root.Clip=new RectangleGeometry{Rect=new Rect(0,0,ActualWidth,ActualHeight)};Adapt();};
    }
    private static Grid MetricIcon(int index,LimsIconKind fallback,string color)
    {
        var stroke=PlanningSheetGrid.Color(color);
        var grid=new Grid{Width=28,Height=28,VerticalAlignment=VerticalAlignment.Center};
        grid.Children.Add(new Ellipse{Fill=stroke,Opacity=.10});
        if(index is 1 or 2 or 4)
        {
            var data=index==1?"M4,9 L14,4 L24,9 L14,14 Z M4,14 L14,19 L24,14 M4,19 L14,24 L24,19":index==2?"M14,7 L14,16 M14,19 L14,21":"M9,10 C9,4 20,4 20,10 C20,14 14,13 14,18 M14,21 L14,23";
            if(index!=1)grid.Children.Add(new Ellipse{Stroke=stroke,StrokeThickness=1.4,Margin=new Thickness(2)});
            grid.Children.Add(new Microsoft.UI.Xaml.Shapes.Path{Data=(Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry),data),Stroke=stroke,StrokeThickness=1.5,IsHitTestVisible=false});
        }
        else
        {
            if(index==3)grid.Children.Add(new Ellipse{Stroke=stroke,StrokeThickness=1.4,Margin=new Thickness(2)});
            grid.Children.Add(new LimsIcon{Icon=fallback,Size=22,Foreground=stroke});
        }
        return grid;
    }
    public void TableOnly(bool value){_tableOnly=value;_metrics.Visibility=_subtitle.Visibility=value?Visibility.Collapsed:Visibility.Visible;Adapt();}
    public void Context(PlanningPage page,Guid? batch=null)
    {
        if(_holdCaption is{} caption)caption.Text=$"Pruebas On Hold ({page.Counts?.OnHoldJobs.ToString("N0",System.Globalization.CultureInfo.InvariantCulture)??"—"} {(page.Counts?.OnHoldJobs==1?"JOB":"JOBs")})";
        _subtitle.Text=page.Scope is{} scope?(scope.Mode=="History"?"Histórico completo":$"Período operativo · Due Date desde {scope.DueFrom:yyyy-MM-dd}")+" · Cobertura parcial":"Alcance no informado por la API";
        if(batch is{} id)_subtitle.Text+=" · Lote "+id.ToString()[..8];
        ToolTipService.SetToolTip(_subtitle,page.Scope?.Notice??"No se pudo verificar la cobertura de datos.");
    }
    public void CompactHeight(bool value){_short=value;Adapt();}
    private void Adapt()
    {
        bool compact=ActualWidth<1180;
        _title.FontSize=compact?20:25;_subtitle.FontSize=compact?11:12;
        _module.Width=_module.Height=_tableOnly?40:compact?46:56;
        _content.MinHeight=_tableOnly?64:_short?72:90;_content.Padding=_short?new Thickness(12,8,12,8):new Thickness(compact?12:16);
        foreach(var icon in _metricIcons)icon.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
        foreach(var value in _values)value.FontSize=compact?20:23;
    }
}
