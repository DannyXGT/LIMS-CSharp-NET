using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Lims.Desktop.Planning;

/// <summary>Native virtualized compact table using the shared LIMS row and surface resources.</summary>
internal sealed class ReportNativeTable<T> : Grid where T:class
{
    private readonly ListView _list=new(){SelectionMode=ListViewSelectionMode.Single,Padding=new(0),BorderThickness=new(0)};
    public T? Selected=>_list.SelectedItem as T;
    public event Action<T>? SelectedChanged;
    public ReportNativeTable(string name,(string Label,int Width)[] columns,Func<T,string[]> cells)
    {
        var width=columns.Sum(c=>c.Width);var header=new StackPanel{Orientation=Orientation.Horizontal,Background=PlanningUi.Brush("LimsSurfaceSecondaryBrush")};
        foreach(var column in columns){var label=PlanningUi.Text(column.Label,12,true);label.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;label.Margin=new(10,10,6,10);header.Children.Add(new Border{Width=column.Width,Child=label,BorderBrush=PlanningUi.Brush("LimsBorderBrush"),BorderThickness=new(0,0,0,1)});}
        _list.Width=width;_list.HorizontalAlignment=HorizontalAlignment.Left;AutomationProperties.SetName(_list,name);
        _list.ItemsPanel=(ItemsPanelTemplate)XamlReader.Load("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsStackPanel ItemsUpdatingScrollMode='KeepScrollOffset'/></ItemsPanelTemplate>");
        _list.ItemTemplate=(DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Height='38'/></DataTemplate>");
        var style=new Style(typeof(ListViewItem)){BasedOn=(Style)Application.Current.Resources["LimsDataRowStyle"]};style.Setters.Add(new Setter(ListViewItem.PaddingProperty,new Thickness(0)));style.Setters.Add(new Setter(ListViewItem.MinHeightProperty,38d));style.Setters.Add(new Setter(ListViewItem.MarginProperty,new Thickness(0)));_list.ItemContainerStyle=style;
        _list.ContainerContentChanging+=(_,args)=>
        {
            if(args.InRecycleQueue||args.Item is not T row||args.ItemContainer.ContentTemplateRoot is not Grid grid)return;
            grid.Children.Clear();grid.ColumnDefinitions.Clear();var values=cells(row);
            for(int i=0;i<columns.Length;i++){grid.ColumnDefinitions.Add(new(){Width=new(columns[i].Width)});var text=PlanningUi.Text(values[i],12);text.Margin=new(10,0,6,0);text.VerticalAlignment=VerticalAlignment.Center;text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;ToolTipService.SetToolTip(text,values[i]);Grid.SetColumn(text,i);grid.Children.Add(text);}
        };
        _list.SelectionChanged+=(_,_)=>{if(Selected is {} row)SelectedChanged?.Invoke(row);};
        var layout=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star));layout.RowSpacing=0;PlanningUi.Place(layout,header,0);PlanningUi.Place(layout,_list,1);
        Children.Add(new ScrollViewer{Content=layout,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalContentAlignment=VerticalAlignment.Stretch});
        Background=PlanningUi.Brush("LimsSurfaceBrush");BorderBrush=PlanningUi.Brush("LimsBorderBrush");BorderThickness=new(1);CornerRadius=new(6);
    }
    public void SetItems(object? items)=>_list.ItemsSource=items;
}
