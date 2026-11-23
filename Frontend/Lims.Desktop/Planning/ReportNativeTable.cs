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
        header.Height=40;AutomationProperties.SetName(_list,name);
        _list.ItemsPanel=(ItemsPanelTemplate)XamlReader.Load("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsStackPanel ItemsUpdatingScrollMode='KeepScrollOffset'/></ItemsPanelTemplate>");
        _list.ItemTemplate=(DataTemplate)XamlReader.Load($"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Height='32' Width='{width}'/></DataTemplate>");
        var style=new Style(typeof(ListViewItem)){BasedOn=(Style)Application.Current.Resources["LimsDataRowStyle"]};style.Setters.Add(new Setter(ListViewItem.PaddingProperty,new Thickness(0)));style.Setters.Add(new Setter(ListViewItem.MinHeightProperty,32d));style.Setters.Add(new Setter(ListViewItem.MarginProperty,new Thickness(0)));_list.ItemContainerStyle=style;
        void Populate(ListViewBase sender,ContainerContentChangingEventArgs args)
        {
            if(args.InRecycleQueue)return;
            if(args.Phase==0&&args.ItemContainer.ContentTemplateRoot is not Grid){args.RegisterUpdateCallback(1,Populate);return;}
            if(args.InRecycleQueue||args.Item is not T row||args.ItemContainer.ContentTemplateRoot is not Grid grid)return;
            if(grid.Children.Count==0)
                for(int i=0;i<columns.Length;i++){grid.ColumnDefinitions.Add(new(){Width=new(columns[i].Width)});var text=PlanningUi.Text("",12);text.Margin=new(10,0,6,0);text.VerticalAlignment=VerticalAlignment.Center;text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetColumn(text,i);grid.Children.Add(text);}
            var values=cells(row);
            for(int i=0;i<columns.Length;i++){var text=(TextBlock)grid.Children[i];text.Text=values[i];ToolTipService.SetToolTip(text,values[i]);}
            args.Handled=true;
        }
        _list.ContainerContentChanging+=Populate;
        _list.SelectionChanged+=(_,_)=>{if(Selected is {} row)SelectedChanged?.Invoke(row);};
        Children.Add(new ReportTableViewport(header,_list));
        Background=PlanningUi.Brush("LimsSurfaceBrush");BorderBrush=PlanningUi.Brush("LimsBorderBrush");BorderThickness=new(1);CornerRadius=new(6);
    }
    public void SetItems(object? items)=>_list.ItemsSource=items;
}
