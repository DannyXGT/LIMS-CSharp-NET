using System.Globalization;
using Lims.Contracts.Planning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Lims.Desktop.Planning;

internal sealed record ClosureGridRow(ReportClosure? Closure,string Label="",string Date="",string Time="",string Count="");

/// <summary>Virtualized closure rows and date/time bands; both modes use the same records.</summary>
internal sealed partial class ReportClosureGrid : Grid
{
    private readonly ListView _list=new(){SelectionMode=ListViewSelectionMode.None,Padding=new(0),BorderThickness=new(0),IsTabStop=true};
    private readonly Grid _header;
    private readonly ReportTableViewport _viewport;
    private static readonly (string Label,int Width)[] ClosureColumns=[("JOB",145),("BUYER",145),("PRUEBA",225),("USD",95),("SERVICE",245)];
    private (string Label,int Width)[] _columns=ClosureColumns;
    private bool _grouped=true;
    public event Action<ReportClosure>? OpenRequested;
    public ReportClosureGrid()
    {
        _header=Header();AutomationProperties.SetName(_list,"Bandeja de cierres");
        _list.ItemsPanel=(ItemsPanelTemplate)XamlReader.Load("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsStackPanel ItemsUpdatingScrollMode='KeepScrollOffset'/></ItemsPanelTemplate>");
        _list.ItemTemplate=(DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid/></DataTemplate>");
        var style=new Style(typeof(ListViewItem)){BasedOn=(Style)Application.Current.Resources["LimsDataRowStyle"]};style.Setters.Add(new Setter(ListViewItem.PaddingProperty,new Thickness(0)));style.Setters.Add(new Setter(ListViewItem.MinHeightProperty,32d));style.Setters.Add(new Setter(ListViewItem.MarginProperty,new Thickness(0)));_list.ItemContainerStyle=style;
        void Populate(ListViewBase sender,ContainerContentChangingEventArgs args)
        {
            if(args.InRecycleQueue){_realized.Remove(args.ItemContainer);return;}
            if(args.Phase==0&&args.ItemContainer.ContentTemplateRoot is not Grid){args.RegisterUpdateCallback(1,Populate);return;}
            if(args.Item is not ClosureGridRow row||args.ItemContainer.ContentTemplateRoot is not Grid grid)return;
            _realized[args.ItemContainer]=grid;grid.Tag=null;grid.Children.Clear();grid.ColumnDefinitions.Clear();grid.RowDefinitions.Clear();grid.Width=ContentWidth;grid.HorizontalAlignment=HorizontalAlignment.Left;
            if(row.Closure is not {} closure)
            {
                grid.Height=62;var layout=PlanningUi.Rows(new GridLength(32),new GridLength(30));layout.RowSpacing=0;
                var band=new Grid{ColumnSpacing=10,Padding=new(11,0,11,0)};foreach(var width in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star)})band.ColumnDefinitions.Add(new(){Width=width});
                var date=PlanningUi.Text(row.Date,12);date.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;date.Foreground=PlanningSheetGrid.Color("#54D9A7");date.VerticalAlignment=VerticalAlignment.Center;band.Children.Add(date);
                var clock=PlanningUi.Text(row.Time,11);clock.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;clock.Foreground=PlanningSheetGrid.Color("#71DBB0");
                var pill=new Border{Child=clock,CornerRadius=new(10),Padding=new(8,2,8,2),Background=PlanningSheetGrid.Color("#0C2019"),BorderBrush=PlanningSheetGrid.Color("#28503F"),BorderThickness=new(1),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(pill,1);band.Children.Add(pill);
                var count=PlanningUi.Text(row.Count,11,true);count.HorizontalAlignment=HorizontalAlignment.Right;count.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(count,2);band.Children.Add(count);
                PlanningUi.Place(layout,new Border{Child=band,Background=PlanningSheetGrid.Color("#123126"),BorderBrush=PlanningSheetGrid.Color("#244236"),BorderThickness=new(0,0,0,1)},0);PlanningUi.Place(layout,Header(),1);grid.Children.Add(layout);args.ItemContainer.IsTabStop=false;AutomationProperties.SetName(grid,row.Label);
            }
            else
            {
                grid.Height=30;args.ItemContainer.IsTabStop=true;AutomationProperties.SetName(grid,closure.Job);
                var values=Values(closure);var rowIndex=_data.FindIndex(c=>ReferenceEquals(c,closure));grid.Tag=rowIndex;
                for(var i=0;i<_columns.Length;i++)
                {grid.ColumnDefinitions.Add(new(){Width=i==_columns.Length-1?new(1,GridUnitType.Star):new(_columns[i].Width)});var text=PlanningUi.Text(values[i],12);text.Margin=new(11,0,8,0);text.VerticalAlignment=VerticalAlignment.Center;text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;if(i==0)text.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;ToolTipService.SetToolTip(text,values[i]);var cell=new Border{Child=text,Tag=new SheetCell(rowIndex,i)};cell.PointerPressed+=CellPressed;Grid.SetColumn(cell,i);grid.Children.Add(cell);AutomationProperties.SetName(text,$"Fila {rowIndex+1}, {_columns[i].Label}: {values[i]}");}
                PaintRow(grid,rowIndex);
                ToolTipService.SetToolTip(grid,$"{closure.LastDigitizedAt.ToOffset(TimeSpan.FromHours(-6)):dd/MM/yyyy HH:mm} · {closure.StateText}");
            }
            args.Handled=true;
        }
        _list.ContainerContentChanging+=Populate;
        _viewport=new(_header,_list);Children.Add(_viewport);Background=PlanningSheetGrid.Color("#0C131A");BorderBrush=PlanningSheetGrid.Color("#26343F");BorderThickness=new(1);CornerRadius=new(4);
        ConfigureSelection();
        SizeChanged+=(_,_)=>{_header.Width=ContentWidth;for(var i=0;i<_list.Items.Count;i++)if(_list.ContainerFromIndex(i) is ListViewItem{ContentTemplateRoot:Grid grid})grid.Width=ContentWidth;};
    }
    private double ContentWidth=>Math.Max(_columns.Sum(c=>c.Width),ActualWidth-2);
    private Grid Header()
    {
        var header=new Grid{Background=PlanningSheetGrid.Color("#151E27"),Height=30};
        for(var i=0;i<_columns.Length;i++){int col=i;var column=_columns[i];header.ColumnDefinitions.Add(new(){Width=i==_columns.Length-1?new(1,GridUnitType.Star):new(column.Width)});var text=PlanningUi.Text(column.Label,11,true);text.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;text.Margin=new(12,0,8,0);text.VerticalAlignment=VerticalAlignment.Center;var cell=new Border{Child=text,Background=PlanningSheetGrid.Color("#151E27"),BorderBrush=PlanningUi.Brush("LimsBorderBrush"),BorderThickness=new(0,0,0,1)};cell.Tapped+=(_,e)=>{SelectColumn(col);e.Handled=true;};ToolTipService.SetToolTip(cell,"Seleccionar columna de esta página");Grid.SetColumn(cell,i);header.Children.Add(cell);}return header;
    }
    public void SetItems(IEnumerable<ReportClosure> items,bool grouped)
    {
        StopDragging();ClearSelection();_realized.Clear();var rows=new List<ClosureGridRow>();_data.Clear();_grouped=grouped;_columns=grouped?ClosureColumns:[..ClosureColumns,("FECHA / HORA",155)];_header.Visibility=grouped?Visibility.Collapsed:Visibility.Visible;
        _viewport.ShowHeader(!grouped);
        var fresh=Header();_header.Width=ContentWidth;_header.Children.Clear();_header.ColumnDefinitions.Clear();foreach(var column in fresh.ColumnDefinitions)_header.ColumnDefinitions.Add(new(){Width=column.Width});foreach(var child in fresh.Children.ToArray()){fresh.Children.Remove(child);_header.Children.Add(child);}
        if(!grouped){_data.AddRange(items);_list.ItemsSource=_data.Select(c=>new ClosureGridRow(c)).ToArray();return;}
        foreach(var group in items.GroupBy(c=>c.LastDigitizedAt.ToOffset(TimeSpan.FromHours(-6)).ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture)).OrderByDescending(g=>g.Key,StringComparer.Ordinal))
        {
            if(grouped){var count=group.Count();var total=group.Max(c=>c.GroupCount);var date=group.First().LastDigitizedAt.ToOffset(TimeSpan.FromHours(-6)).ToString("dd/MM/yyyy",CultureInfo.InvariantCulture);var label=$"{(total>count?$"{count:N0} de {total:N0}":count.ToString("N0",CultureInfo.CurrentCulture))} {(total==1||total==0&&count==1?"cierre":"cierres")}";rows.Add(new(null,$"{date} · {group.Key[11..]} · {label}",date,group.Key[11..],label));}
            foreach(var row in group.OrderBy(c=>c.Job,StringComparer.Ordinal)){_data.Add(row);rows.Add(new(row));}
        }
        _list.ItemsSource=rows;
    }
}
