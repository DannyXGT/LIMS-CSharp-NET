using Lims.Contracts.Planning;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Lims.Desktop.Planning;

/// <summary>Native virtualized rows with rectangular cell selection and a frozen JOB column.</summary>
public sealed class PlanningSheetGrid : Grid
{
    public PlanningSheetModel Model{get;}=new();
    private readonly ListView _rows=new(){SelectionMode=ListViewSelectionMode.None,IsItemClickEnabled=false,Padding=new Thickness(0),BorderThickness=new Thickness(0),IsTabStop=true};
    private readonly StackPanel _header=new(){Orientation=Orientation.Horizontal};
    private readonly ScrollViewer _horizontal;
    private ScrollViewer? _vertical;
    private readonly Dictionary<ListViewItem,Grid> _realized=[];
    private readonly DispatcherTimer _autoScroll=new(){Interval=TimeSpan.FromMilliseconds(60)};
    private bool _dragging;private Point _pointer;
    public event Action? SelectionChanged;
    public event Func<Task>? CopyRequested;
    public event Func<Task>? PasteRequested;
    public event Func<Task>? OpenRequested;
    public event Func<int,int,Task>? EditRequested;
    public event Func<string,Task>? SortRequested;
    public PlanningSheetGrid()
    {
        var table=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star));table.RowSpacing=0;
        _header.Height=37;_header.Background=Color("#1A3044");PlanningUi.Place(table,_header,0);PlanningUi.Place(table,_rows,1);
        _horizontal=new ScrollViewer{Content=table,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalContentAlignment=VerticalAlignment.Stretch};Children.Add(_horizontal);
        _rows.ItemsSource=Model.Rows;
        var rowStyle=new Style(typeof(ListViewItem)){BasedOn=(Style)Application.Current.Resources["LimsDataRowStyle"]};
        rowStyle.Setters.Add(new Setter(ListViewItem.MinHeightProperty,32d));rowStyle.Setters.Add(new Setter(ListViewItem.PaddingProperty,new Thickness(0)));rowStyle.Setters.Add(new Setter(ListViewItem.MarginProperty,new Thickness(0)));rowStyle.Setters.Add(new Setter(ListViewItem.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));
        rowStyle.Setters.Add(new Setter(ListViewItem.UseSystemFocusVisualsProperty,false));_rows.UseSystemFocusVisuals=false;
        _rows.ItemContainerStyle=rowStyle;
        AutomationProperties.SetName(_rows,"Tabla de Consulta de Jobs");
        _rows.ContainerContentChanging+=ContainerChanging;
        _rows.Loaded+=(_,_)=>{_vertical=Descendant<ScrollViewer>(_rows);Paint();};
        _horizontal.ViewChanged+=(_,_)=>PinJob();
        AddHandler(PointerMovedEvent,new PointerEventHandler(Moved),true);
        AddHandler(PointerReleasedEvent,new PointerEventHandler((_,e)=>{_dragging=false;_autoScroll.Stop();ReleasePointerCapture(e.Pointer);}),true);
        PointerCaptureLost+=(_,e)=>{if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed){_dragging=false;_autoScroll.Stop();}};
        _autoScroll.Tick+=(_,_)=>AutoScroll();
        _rows.AddHandler(KeyDownEvent,new KeyEventHandler(Key),true);
        _rows.AddHandler(DoubleTappedEvent,new DoubleTappedEventHandler(async(_,e)=>
        {
            if(Model.Rows.Count==0)return;
            if(Model.Active.Column==0){if(OpenRequested is{} open)await open();}
            else if(Model.Columns[Model.Active.Column].Editable&&EditRequested is{} edit)await edit(Model.Active.Row,Model.Active.Column);
            e.Handled=true;
        }),true);
        var menu=new MenuFlyout();
        void Item(string title,Func<Task> action){var item=new MenuFlyoutItem{Text=title};item.Click+=async(_,_)=>await action();menu.Items.Add(item);}
        Item("Copiar selección",async()=>{if(CopyRequested is {} action)await action();});
        Item("Pegar en celdas editables",async()=>{if(PasteRequested is {} action)await action();});
        Item("Estado del JOB",async()=>{if(OpenRequested is {} action)await action();});
        Item("Seleccionar columna cargada",()=>{Model.SelectColumn(Model.Active.Column);Changed();return Task.CompletedTask;});
        _rows.ContextFlyout=menu;
        RebuildColumns();
        Unloaded+=(_,_)=>{_autoScroll.Stop();_dragging=false;};
    }
    internal static SolidColorBrush Color(string value)
    {
        var n=Convert.ToUInt32(value.TrimStart('#'),16);return new(Windows.UI.Color.FromArgb(255,(byte)(n>>16),(byte)(n>>8),(byte)n));
    }
    private static T? Descendant<T>(DependencyObject root)where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T found)return found;if(Descendant<T>(child)is{} nested)return nested;}return null;}
    public void RebuildColumns()
    {
        _realized.Clear();_header.Children.Clear();
        for(int i=0;i<Model.Columns.Count;i++)
        {
            int col=i;var column=Model.Columns[i];
            var button=PlanningUi.Button(column.Title+(column.Editable?" ✎":" ⌄"),async()=>{if(SortRequested is{} sort)await sort(column.Key);});
            button.Width=column.Width;button.Height=37;button.MinHeight=0;button.FontSize=11;button.Padding=new Thickness(10,0,4,0);button.CornerRadius=new CornerRadius(0);button.BorderThickness=new Thickness(0,0,1,1);button.Background=Color(i==0?"#18384E":"#1A3044");button.HorizontalContentAlignment=HorizontalAlignment.Left;
            var label=PlanningUi.Text(column.Title+(column.Editable?" ✎":" ⌄"),11,true);label.Width=column.Width-20;label.TextWrapping=TextWrapping.NoWrap;button.Content=label;
            var flyout=new MenuFlyout();var select=new MenuFlyoutItem{Text="Seleccionar columna cargada"};select.Click+=(_,_)=>{Model.SelectColumn(col);Changed();};flyout.Items.Add(select);button.ContextFlyout=flyout;_header.Children.Add(button);
        }
        _rows.ItemTemplate=(DataTemplate)XamlReader.Load($"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Height='32' Width='{Model.Columns.Sum(c=>c.Width)}'/></DataTemplate>");
        Model.Select(Model.Active.Row,Math.Min(Model.Active.Column,Model.Columns.Count-1));
        _rows.UpdateLayout();Paint();
    }
    private void ContainerChanging(ListViewBase sender,ContainerContentChangingEventArgs args)
    {
        if(args.ItemContainer is not ListViewItem item)return;
        if(args.InRecycleQueue){_realized.Remove(item);return;}
        if(args.Phase==0){args.RegisterUpdateCallback(1,ContainerChanging);return;}
        if(item.ContentTemplateRoot is not Grid grid)return;
        int row=args.ItemIndex;grid.Tag=row;
        grid.ColumnDefinitions.Clear();grid.Children.Clear();
        for(int col=0;col<Model.Columns.Count;col++)
        {
            grid.ColumnDefinitions.Add(new(){Width=new(Model.Columns[col].Width)});
            var text=PlanningUi.Text(Model.Value(row,col),12);text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;text.VerticalAlignment=VerticalAlignment.Center;
            var cell=new Border{Child=text,Tag=new SheetCell(row,col),Padding=new Thickness(10,0,6,0),BorderThickness=new Thickness(0,0,1,1),BorderBrush=Color("#253646")};
            Grid.SetColumn(cell,col);
            cell.PointerPressed+=Pressed;
            // Text automation names identify actual cell coordinates for assistive technology and runtime acceptance.
            AutomationProperties.SetName(text,$"F{row+1} C{col+1} · {Model.Columns[col].Title} · {text.Text}");
            ToolTipService.SetToolTip(cell,text.Text);grid.Children.Add(cell);
        }
        _realized[item]=grid;PaintGrid(grid,row);PinJob();args.Handled=true;
    }
    private void Pressed(object sender,PointerRoutedEventArgs args)
    {
        var point=args.GetCurrentPoint(_rows);var cell=(SheetCell)((Border)sender).Tag;
        if(point.Properties.IsRightButtonPressed){if(!Model.Contains(cell.Row,cell.Column)){Model.Select(cell.Row,cell.Column);Changed();}return;}
        if(!point.Properties.IsLeftButtonPressed)return;
        Model.Select(cell.Row,cell.Column,Shift());Changed();_rows.Focus(FocusState.Programmatic);_pointer=new(args.GetCurrentPoint(_horizontal).Position.X,point.Position.Y);
        _dragging=true;CapturePointer(args.Pointer);_autoScroll.Start();args.Handled=true;
    }
    private void Moved(object sender,PointerRoutedEventArgs args)
    {if(!_dragging)return;_pointer=new(args.GetCurrentPoint(_horizontal).Position.X,args.GetCurrentPoint(_rows).Position.Y);ExtendPointer();args.Handled=true;}
    private void ExtendPointer()
    {
        int row=(int)Math.Floor((Math.Clamp(_pointer.Y,0,Math.Max(0,_rows.ActualHeight-1))+(_vertical?.VerticalOffset??0))/32);
        double x=_pointer.X+_horizontal.HorizontalOffset;int col=0;
        if(_pointer.X>=Model.Columns[0].Width){while(col<Model.Columns.Count-1&&x>=Model.Columns[col].Width){x-=Model.Columns[col].Width;col++;}}
        Model.Select(row,col,true);Changed();
    }
    private void AutoScroll()
    {
        if(!_dragging||Model.Rows.Count==0)return;
        if(_vertical is {} v){double delta=_pointer.Y<24?-32:_pointer.Y>_rows.ActualHeight-24?32:0;if(delta!=0)v.ChangeView(null,Math.Clamp(v.VerticalOffset+delta,0,v.ScrollableHeight),null,true);}
        double dx=_pointer.X<24?-35:_pointer.X>_horizontal.ActualWidth-24?35:0;
        if(dx!=0)_horizontal.ChangeView(Math.Clamp(_horizontal.HorizontalOffset+dx,0,_horizontal.ScrollableWidth),null,null,true);
        ExtendPointer();
    }
    private static bool Shift()=>(InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)&CoreVirtualKeyStates.Down)!=0;
    private async void Key(object sender,KeyRoutedEventArgs args)
    {
        if(args.OriginalSource is TextBox)return;
        bool ctrl=(InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)&CoreVirtualKeyStates.Down)!=0;
        if(ctrl&&args.Key==VirtualKey.C){args.Handled=true;if(CopyRequested is{} copy)await copy();return;}
        if(ctrl&&args.Key==VirtualKey.V){args.Handled=true;if(PasteRequested is{} paste)await paste();return;}
        if(ctrl&&args.Key==VirtualKey.Space){Model.SelectColumn(Model.Active.Column);Changed();args.Handled=true;return;}
        if(args.Key==VirtualKey.Enter){args.Handled=true;if(OpenRequested is{} open)await open();return;}
        if(args.Key==VirtualKey.F2){args.Handled=true;if(EditRequested is{} edit)await edit(Model.Active.Row,Model.Active.Column);return;}
        int dr=args.Key==VirtualKey.Down?1:args.Key==VirtualKey.Up?-1:0,dc=args.Key==VirtualKey.Right?1:args.Key==VirtualKey.Left?-1:0;
        if(dr!=0||dc!=0){Model.Select(Model.Active.Row+dr,Model.Active.Column+dc,Shift());Changed();EnsureVisible();args.Handled=true;}
    }
    public void FocusTable()=>_rows.Focus(FocusState.Programmatic);
    public void Select(int row,int column){Model.Select(row,column);Changed();EnsureVisible();}
    public void EnsureVisible()
    {
        if(Model.Rows.Count==0)return;
        _rows.ScrollIntoView(Model.Rows[Model.Active.Row],ScrollIntoViewAlignment.Default);
        if(Model.Active.Column>0)
        {
            double left=Model.Columns.Take(Model.Active.Column).Sum(c=>c.Width),right=left+Model.Columns[Model.Active.Column].Width;
            var offset=_horizontal.HorizontalOffset;if(left<offset+Model.Columns[0].Width)offset=left-Model.Columns[0].Width;else if(right>offset+_horizontal.ViewportWidth)offset=right-_horizontal.ViewportWidth;
            _horizontal.ChangeView(Math.Max(0,offset),null,null,true);
        }
    }
    private void Changed(){Paint();SelectionChanged?.Invoke();}
    public void Paint(){foreach(var grid in _realized.Values)if(grid.Tag is int row&&row<Model.Rows.Count)PaintGrid(grid,row);}
    private void PaintGrid(Grid grid,int row)
    {
        for(int col=0;col<grid.Children.Count;col++)
        {
            var cell=(Border)grid.Children[col];var text=(TextBlock)cell.Child;bool active=Model.Active==new SheetCell(row,col),selected=Model.Contains(row,col);
            cell.Background=Color(active?"#205F89":selected?"#1C4D70":col==0?(row%2==0?"#102332":"#142737"):(row%2==0?"#101D29":"#13212E"));
            cell.BorderBrush=Color(active?"#70C6FF":selected?"#2B688F":"#253646");cell.BorderThickness=active?new Thickness(2):new Thickness(0,0,1,1);
            text.Foreground=Color(col==0?"#A5D9FF":Model.Columns[col].Editable?"#E6D3AC":Model.Columns[col].Key=="DigitizationState"?(Model.Rows[row].DigitizedAt is not null?"#71E4B2":Model.Rows[row].DigitizationState=="Pendiente"?"#FFD58C":"#AEC1D0"):"#E8F3FB");
        }
    }
    private void PinJob()
    {
        if(_header.Children.FirstOrDefault()is FrameworkElement head){head.RenderTransform=new TranslateTransform{X=_horizontal.HorizontalOffset};Canvas.SetZIndex(head,10);}
        foreach(var grid in _realized.Values)if(grid.Children.FirstOrDefault()is FrameworkElement cell){cell.RenderTransform=new TranslateTransform{X=_horizontal.HorizontalOffset};Canvas.SetZIndex(cell,10);}
    }
}
