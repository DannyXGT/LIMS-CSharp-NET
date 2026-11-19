using Lims.Contracts.Planning;
using Lims.DesignSystem.Controls;
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
    public bool AllowColumnLayout{get;set;}
    public void SetInteractionEnabled(bool enabled){_rows.IsEnabled=enabled;_header.IsHitTestVisible=enabled;}
    private readonly ListView _rows=new(){SelectionMode=ListViewSelectionMode.None,IsItemClickEnabled=false,Padding=new Thickness(0),BorderThickness=new Thickness(0),IsTabStop=true,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly StackPanel _header=new(){Orientation=Orientation.Horizontal};
    private readonly ScrollViewer _horizontal;
    private ScrollViewer? _vertical;
    private readonly Dictionary<ListViewItem,Grid> _realized=[];
    private readonly DispatcherTimer _autoScroll=new(){Interval=TimeSpan.FromMilliseconds(60)};
    private bool _dragging,_shiftPressed;private Point _pointer;
    public event Action? SelectionChanged;
    public event Func<Task>? CopyRequested;
    public event Func<Task>? PasteRequested;
    public event Func<Task>? OpenRequested;
    public event Func<int,int,Task>? EditRequested;
    public event Func<string,Task>? SortRequested;
    public event Action<string,FrameworkElement>? FilterRequested;
    private int FrozenColumns=>Model.Columns.FindIndex(c=>c.Key=="Job")+1;
    private double FrozenWidth=>Model.Columns.Take(FrozenColumns).Sum(c=>c.Width);
    private string _sortKey="Job";
    private bool _descending;
    private HashSet<string> _filtered=[];
    private readonly Dictionary<string,FontIcon> _sortIcons=[];
    private readonly Dictionary<string,LimsIcon> _filterIcons=[];
    private readonly SolidColorBrush _selectionBackground=SelectionBackground();
    public void SetSort(string key,bool descending){_sortKey=key;_descending=descending;UpdateHeader();}
    public void SetFilters(IEnumerable<string> keys){_filtered=keys.ToHashSet();UpdateHeader();}
    private void UpdateHeader()
    {
        foreach(var (key,icon) in _sortIcons){icon.Glyph=_descending?"\uE70D":"\uE70E";icon.Visibility=key==_sortKey?Visibility.Visible:Visibility.Collapsed;}
        foreach(var (key,icon) in _filterIcons)icon.Foreground=PlanningUi.Brush(_filtered.Contains(key)?"LimsAccentBrush":"LimsTextSecondaryBrush");
    }
    public PlanningSheetGrid(bool readOnly=false,string tableName="Tabla de Consulta de Jobs")
    {
        var table=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star));table.RowSpacing=0;
        _header.Height=40;_header.Background=PlanningUi.Brush("LimsSurfaceSecondaryBrush");PlanningUi.Place(table,_header,0);PlanningUi.Place(table,_rows,1);
        _horizontal=new ScrollViewer{Content=table,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalContentAlignment=VerticalAlignment.Stretch};Children.Add(_horizontal);
        _rows.ItemsSource=Model.Rows;
        _rows.ItemsPanel=(ItemsPanelTemplate)XamlReader.Load("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsStackPanel ItemsUpdatingScrollMode='KeepScrollOffset'/></ItemsPanelTemplate>");
        var rowStyle=new Style(typeof(ListViewItem)){BasedOn=(Style)Application.Current.Resources["LimsDataRowStyle"]};
        rowStyle.Setters.Add(new Setter(ListViewItem.MinHeightProperty,32d));rowStyle.Setters.Add(new Setter(ListViewItem.PaddingProperty,new Thickness(0)));rowStyle.Setters.Add(new Setter(ListViewItem.MarginProperty,new Thickness(0)));rowStyle.Setters.Add(new Setter(ListViewItem.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));
        rowStyle.Setters.Add(new Setter(ListViewItem.UseSystemFocusVisualsProperty,false));_rows.UseSystemFocusVisuals=false;
        _rows.ItemContainerStyle=rowStyle;
        AutomationProperties.SetName(_rows,tableName);
        _rows.ContainerContentChanging+=ContainerChanging;
        _rows.Loaded+=(_,_)=>{_vertical=Descendant<ScrollViewer>(_rows);Paint();};
        _horizontal.ViewChanged+=(_,_)=>PinJob();
        AddHandler(PointerMovedEvent,new PointerEventHandler(Moved),true);
        AddHandler(PointerReleasedEvent,new PointerEventHandler((_,e)=>{_dragging=false;_autoScroll.Stop();ReleasePointerCapture(e.Pointer);}),true);
        PointerCaptureLost+=(_,e)=>{if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed){_dragging=false;_autoScroll.Stop();}};
        _autoScroll.Tick+=(_,_)=>AutoScroll();
        _rows.AddHandler(KeyDownEvent,new KeyEventHandler(Key),true);
        _rows.AddHandler(KeyUpEvent,new KeyEventHandler((_,e)=>{if(e.Key is VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift)_shiftPressed=false;}),true);
        _rows.LostFocus+=(_,_)=>_shiftPressed=false;
        _rows.AddHandler(DoubleTappedEvent,new DoubleTappedEventHandler(async(_,e)=>
        {
            if(Model.Rows.Count==0)return;
            if(Model.Columns[Model.Active.Column].Key=="Job"){if(OpenRequested is{} open)await open();}
            else if(Model.Columns[Model.Active.Column].Editable&&EditRequested is{} edit)await edit(Model.Active.Row,Model.Active.Column);
            e.Handled=true;
        }),true);
        var menu=new MenuFlyout();
        void Item(string title,Func<Task> action){var item=new MenuFlyoutItem{Text=title};item.Click+=async(_,_)=>await action();menu.Items.Add(item);}
        Item("Copiar selección",async()=>{if(CopyRequested is {} action)await action();});
        if(!readOnly)Item("Pegar en celdas editables",async()=>{if(PasteRequested is {} action)await action();});
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
        _rows.Width=Model.Columns.Sum(c=>c.Width);
        _realized.Clear();_header.Children.Clear();_sortIcons.Clear();_filterIcons.Clear();
        for(int i=0;i<Model.Columns.Count;i++)
        {
            int col=i;var column=Model.Columns[i];
            var head=new Grid{Width=column.Width,Height=40,Background=PlanningUi.Brush("LimsSurfaceSecondaryBrush"),BorderBrush=PlanningUi.Brush("LimsBorderBrush"),BorderThickness=new Thickness(0,0,1,1)};
            head.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});head.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var button=PlanningUi.Button(column.Title,async()=>{if(SortRequested is{} sort)await sort(column.Key);});
            button.Height=40;button.MinHeight=0;button.Padding=new Thickness(10,0,4,0);button.CornerRadius=new CornerRadius(0);button.BorderThickness=new Thickness(0);button.Background=PlanningUi.Brush("LimsSurfaceSecondaryBrush");button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
            var label=PlanningUi.Text(column.Title,12,true);label.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;label.TextWrapping=TextWrapping.Wrap;label.TextTrimming=TextTrimming.CharacterEllipsis;label.MaxLines=2;label.VerticalAlignment=VerticalAlignment.Center;
            var sortIcon=new FontIcon{FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=10,Foreground=PlanningUi.Brush("LimsAccentBrush"),VerticalAlignment=VerticalAlignment.Center};_sortIcons[column.Key]=sortIcon;
            var caption=ConsultationVisuals.Split(label,sortIcon);caption.ColumnSpacing=4;button.Content=caption;AutomationProperties.SetName(button,"Ordenar por "+column.Title);ToolTipService.SetToolTip(button,"Ordenar por "+column.Title+(column.Editable?" (columna editable)":""));head.Children.Add(button);
            {
                var icon=new LimsIcon{Icon=LimsIconKind.Filter,Size=12};_filterIcons[column.Key]=icon;
                var filter=new Button{Content=icon,Width=28,MinWidth=28,MinHeight=40,Padding=new Thickness(4),Style=(Style)Application.Current.Resources["LimsIconButtonStyle"]};
                AutomationProperties.SetName(filter,"Filtrar "+column.Title);ToolTipService.SetToolTip(filter,"Filtrar "+column.Title);filter.Click+=(_,_)=>FilterRequested?.Invoke(column.Key,filter);Grid.SetColumn(filter,1);head.Children.Add(filter);
            }
            var flyout=new MenuFlyout();var select=new MenuFlyoutItem{Text="Seleccionar columna cargada"};select.Click+=(_,_)=>{Model.SelectColumn(col);Changed();};flyout.Items.Add(select);button.ContextFlyout=flyout;_header.Children.Add(head);
            if(AllowColumnLayout)
            {
                foreach(var (title,delta) in new[]{("Mover columna a la izquierda",-1),("Mover columna a la derecha",1)})
                {
                    var target=col+delta;var move=new MenuFlyoutItem{Text=title,IsEnabled=column.Key!="Job"&&target>0&&target<Model.Columns.Count};
                    move.Click+=(_,_)=>{var activeKey=Model.Columns[Model.Active.Column].Key;var row=Model.Active.Row;Model.Columns.RemoveAt(col);Model.Columns.Insert(target,column);RebuildColumns();Model.Select(row,Model.Columns.FindIndex(c=>c.Key==activeKey));Changed();};flyout.Items.Add(move);
                }
                var grip=new Border{Width=6,HorizontalAlignment=HorizontalAlignment.Right,Background=new SolidColorBrush(Colors.Transparent),ManipulationMode=ManipulationModes.TranslateX};Grid.SetColumnSpan(grip,2);head.Children.Add(grip);
                AutomationProperties.SetName(grip,"Redimensionar "+column.Title);ToolTipService.SetToolTip(grip,"Arrastra para cambiar el ancho de "+column.Title);
                grip.ManipulationDelta+=(_,e)=>
                {
                    var width=Math.Clamp(Model.Columns[col].Width+(int)Math.Round(e.Delta.Translation.X),70,700);Model.Columns[col]=Model.Columns[col] with{Width=width};head.Width=width;
                    _rows.Width=Model.Columns.Sum(c=>c.Width);
                    foreach(var grid in _realized.Values){grid.Width=_rows.Width;grid.ColumnDefinitions[col].Width=new(width);}PinJob();e.Handled=true;
                };
                grip.ManipulationCompleted+=(_,_)=>RebuildColumns();
            }
        }
        UpdateHeader();
        _rows.ItemTemplate=(DataTemplate)XamlReader.Load($"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Height='32' Width='{Model.Columns.Sum(c=>c.Width)}' HorizontalAlignment='Left'/></DataTemplate>");
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
            UIElement content=text;
            if(Model.Columns[col].Key=="Job"&&(Model.Rows[row].OnHold||!string.IsNullOrWhiteSpace(Model.Rows[row].JobLabel)))
            {
                var detail=PlanningUi.Text(string.Join(" · ",new[]{Model.Rows[row].OnHold?"ON HOLD":null,Model.Rows[row].JobLabel}.Where(s=>!string.IsNullOrWhiteSpace(s))),10,true);detail.TextWrapping=TextWrapping.NoWrap;detail.TextTrimming=TextTrimming.CharacterEllipsis;
                content=new StackPanel{Spacing=0,VerticalAlignment=VerticalAlignment.Center,Children={text,detail}};
            }
            if(Model.Columns[col].Key=="DigitizationState")
            {
                text.FontSize=11;text.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
                content=new Border{Child=text,CornerRadius=new CornerRadius(4),Padding=new Thickness(6,2,6,2),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,MaxWidth=Model.Columns[col].Width-16};
            }
            var cell=new Border{Child=content,Tag=new SheetCell(row,col),Padding=new Thickness(10,0,6,0),BorderThickness=new Thickness(0,0,1,1),BorderBrush=PlanningUi.Brush("LimsBorderBrush")};
            Grid.SetColumn(cell,col);
            cell.PointerPressed+=Pressed;
            // Text automation names identify actual cell coordinates for assistive technology and runtime acceptance.
            AutomationProperties.SetName(text,$"F{row+1} C{col+1} · {Model.Columns[col].Title} · {text.Text}");
            ToolTipService.SetToolTip(cell,Model.Columns[col].Key=="Job"?$"{text.Text}{(Model.Rows[row].OnHold?"\nON HOLD · "+Model.Rows[row].HoldReason:"")}{(Model.Rows[row].JobLabel is{} label?"\nEtiqueta: "+label:"")}":text.Text);grid.Children.Add(cell);
        }
        _realized[item]=grid;PaintGrid(grid,row);PinJob();args.Handled=true;
    }
    private void Pressed(object sender,PointerRoutedEventArgs args)
    {
        var point=args.GetCurrentPoint(_rows);var cell=(SheetCell)((Border)sender).Tag;
        if(point.Properties.IsRightButtonPressed){if(!Model.Contains(cell.Row,cell.Column)){Model.Select(cell.Row,cell.Column);Changed();}return;}
        if(!point.Properties.IsLeftButtonPressed)return;
        Model.Select(cell.Row,cell.Column,Shift());Changed();FocusTable();_pointer=new(args.GetCurrentPoint(_horizontal).Position.X,point.Position.Y);
        _dragging=true;CapturePointer(args.Pointer);_autoScroll.Start();args.Handled=true;
    }
    private void Moved(object sender,PointerRoutedEventArgs args)
    {if(!_dragging)return;_pointer=new(args.GetCurrentPoint(_horizontal).Position.X,args.GetCurrentPoint(_rows).Position.Y);ExtendPointer();args.Handled=true;}
    private void ExtendPointer()
    {
        int row=(int)Math.Floor((Math.Clamp(_pointer.Y,0,Math.Max(0,_rows.ActualHeight-1))+(_vertical?.VerticalOffset??0))/32);
        double x=_pointer.X+(_pointer.X<FrozenWidth?0:_horizontal.HorizontalOffset);int col=0;
        while(col<Model.Columns.Count-1&&x>=Model.Columns[col].Width){x-=Model.Columns[col].Width;col++;}
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
    private bool Shift()=>_shiftPressed||new[]{VirtualKey.Shift,VirtualKey.LeftShift,VirtualKey.RightShift}.Any(key=>(InputKeyboardSource.GetKeyStateForCurrentThread(key)&CoreVirtualKeyStates.Down)!=0);
    private async void Key(object sender,KeyRoutedEventArgs args)
    {
        if(args.OriginalSource is TextBox)return;
        if(args.Key is VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift){_shiftPressed=true;return;}
        bool ctrl=(InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)&CoreVirtualKeyStates.Down)!=0;
        if(ctrl&&args.Key==VirtualKey.C){args.Handled=true;if(CopyRequested is{} copy)await copy();return;}
        if(ctrl&&args.Key==VirtualKey.V){args.Handled=true;if(PasteRequested is{} paste)await paste();return;}
        if(ctrl&&args.Key==VirtualKey.A){Model.SelectAll();Changed();args.Handled=true;return;}
        if(args.Key==VirtualKey.Escape){Model.Select(Model.Active.Row,Model.Active.Column);Changed();args.Handled=true;return;}
        if(ctrl&&args.Key==VirtualKey.Space){Model.SelectColumn(Model.Active.Column);Changed();args.Handled=true;return;}
        if(args.Key==VirtualKey.Enter){args.Handled=true;if(OpenRequested is{} open)await open();return;}
        if(args.Key==VirtualKey.F2){args.Handled=true;if(EditRequested is{} edit)await edit(Model.Active.Row,Model.Active.Column);return;}
        int dr=args.Key==VirtualKey.Down?1:args.Key==VirtualKey.Up?-1:0,dc=args.Key==VirtualKey.Right?1:args.Key==VirtualKey.Left?-1:0;
        if(dr!=0||dc!=0){Model.Select(Model.Active.Row+dr,Model.Active.Column+dc,Shift());Changed();EnsureVisible();args.Handled=true;}
    }
    public void FocusTable()
    {
        if(_rows.ContainerFromIndex(Model.Active.Row) is ListViewItem row)row.Focus(FocusState.Programmatic);
        else _rows.Focus(FocusState.Programmatic);
        EnsureVisible();
        var active=Model.Active;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,()=>
        {
            if(!_dragging&&Model.Active==active&&Model.Rows.Count>active.Row)EnsureVisible();
        });
    }
    public void Select(int row,int column){Model.Select(row,column);Changed();EnsureVisible();}
    public (double Horizontal,double Vertical) ScrollPosition=>(_horizontal.HorizontalOffset,_vertical?.VerticalOffset??0);
    public void RestoreScroll((double Horizontal,double Vertical) position){_rows.UpdateLayout();_horizontal.ChangeView(position.Horizontal,null,null,true);_vertical?.ChangeView(null,position.Vertical,null,true);}
    public void EnsureVisible()
    {
        if(Model.Rows.Count==0)return;
        _rows.UpdateLayout();
        _rows.ScrollIntoView(Model.Rows[Model.Active.Row],ScrollIntoViewAlignment.Default);
        if(Model.Active.Row==0)
        {
            _vertical?.ChangeView(null,0,null,true);
            var first=Model.Rows[0];
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,()=>
            {
                if(!_dragging&&Model.Rows.Count>0&&Model.Active.Row==0&&ReferenceEquals(first,Model.Rows[0]))_vertical?.ChangeView(null,0,null,true);
            });
        }
        if(Model.Active.Column>=FrozenColumns)
        {
            double left=Model.Columns.Take(Model.Active.Column).Sum(c=>c.Width),right=left+Model.Columns[Model.Active.Column].Width;
            var offset=_horizontal.HorizontalOffset;if(left<offset+FrozenWidth)offset=left-FrozenWidth;else if(right>offset+_horizontal.ViewportWidth)offset=right-_horizontal.ViewportWidth;
            _horizontal.ChangeView(Math.Max(0,offset),null,null,true);
        }
    }
    private void Changed(){Paint();SelectionChanged?.Invoke();}
    public void Paint(){foreach(var grid in _realized.Values)if(grid.Tag is int row&&row<Model.Rows.Count)PaintGrid(grid,row);}
    private void PaintGrid(Grid grid,int row)
    {
        for(int col=0;col<grid.Children.Count;col++)
        {
            var cell=(Border)grid.Children[col];var text=cell.Child is TextBlock value?value:cell.Child is StackPanel job?(TextBlock)job.Children[0]:(TextBlock)((Border)cell.Child).Child;bool active=Model.Active==new SheetCell(row,col),selected=Model.Contains(row,col);
            cell.Background=PlanningUi.Brush(active?"LimsSurfaceElevatedBrush":selected?"LimsSurfaceSecondaryBrush":row%2==0?"LimsSurfaceBrush":"LimsInputBackgroundBrush");
            if(selected)cell.Background=_selectionBackground;
            cell.BorderBrush=PlanningUi.Brush(active?"LimsFocusBrush":selected?"LimsAccentBrush":"LimsBorderBrush");cell.BorderThickness=active?new Thickness(2):new Thickness(0,0,1,1);
            text.FontWeight=Model.Columns[col].Key is "Job" or "DigitizationState"?Microsoft.UI.Text.FontWeights.SemiBold:Microsoft.UI.Text.FontWeights.Normal;
            text.Foreground=PlanningUi.Brush(Model.Columns[col].Key=="Job"?(Model.Rows[row].OnHold?"LimsWarningBrush":"LimsInformationBrush"):Model.Columns[col].Key=="DigitizationState"?(Model.Rows[row].Complete?"LimsSuccessBrush":Model.Rows[row].DigitizationState=="Información incompleta"?"LimsWarningBrush":"LimsTextSecondaryBrush"):"LimsTextPrimaryBrush");
            if(Model.Columns[col].Key=="State")text.Foreground=PlanningUi.Brush(Model.Value(row,col) switch{"Retenido"=>"LimsWarningBrush","Resuelto"=>"LimsSuccessBrush",_=>"LimsTextSecondaryBrush"});
            if(Model.Columns[col].Key=="JobState")text.Foreground=PlanningUi.Brush(Model.Rows[row].JobState=="No informado"?"LimsTextSecondaryBrush":"LimsInformationBrush");
            if(cell.Child is Border badge)badge.Background=Model.Columns[col].Key=="JobState"?UnknownBadge:Model.Rows[row].Complete?DigitizedBadge:Model.Rows[row].DigitizationState=="Información incompleta"?PendingBadge:UnknownBadge;

        }
    }
    private static readonly SolidColorBrush DigitizedBadge=Color("#16392E"),PendingBadge=Color("#44331B"),UnknownBadge=Color("#193345");
    private static SolidColorBrush SelectionBackground()
    {
        var background=((SolidColorBrush)PlanningUi.Brush("LimsSurfaceBrush")).Color;
        var selection=((SolidColorBrush)PlanningUi.Brush("LimsSelectionBrush")).Color;
        byte Blend(byte value,byte basis)=>(byte)((value*selection.A+basis*(255-selection.A))/255);
        return new(Windows.UI.Color.FromArgb(255,Blend(selection.R,background.R),Blend(selection.G,background.G),Blend(selection.B,background.B)));
    }
    private void PinJob()
    {
        for(int i=0;i<_header.Children.Count;i++)if(_header.Children[i]is FrameworkElement head){head.RenderTransform=new TranslateTransform{X=i<FrozenColumns?_horizontal.HorizontalOffset:0};Canvas.SetZIndex(head,i<FrozenColumns?10:0);}
        foreach(var grid in _realized.Values)for(int i=0;i<grid.Children.Count;i++)if(grid.Children[i]is FrameworkElement cell){cell.RenderTransform=new TranslateTransform{X=i<FrozenColumns?_horizontal.HorizontalOffset:0};Canvas.SetZIndex(cell,i<FrozenColumns?10:0);}
    }
}
