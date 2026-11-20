using Lims.Contracts.Planning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Lims.Desktop.Planning;

/// <summary>Report List table in the existing WinUI design system. Realized rows only create controls.</summary>
internal sealed class ReportListGrid : Grid
{
    private sealed record Column(string Key,string Label,double Width,bool Filter=false);
    private readonly List<Column> _columns=[new("Block","Bloque",85,true),new("Job","JOB",185,true),new("Test","Prueba",220,true),
        new("Laboratory","Laboratorio",110,true),new("Buyer","Cliente",180,true),new("Keep","Mantener",85),
        new("VerifiedBy","Verificado por",160,true),new("Date","Fecha",100,true),new("Time","Hora",85),new("Checked","CHK",65)];
    private readonly ListView _rows=new(){SelectionMode=ListViewSelectionMode.Extended,HorizontalAlignment=HorizontalAlignment.Left,Padding=new(0),BorderThickness=new(0),IsMultiSelectCheckBoxEnabled=true};
    private readonly StackPanel _header=new(){Orientation=Orientation.Horizontal,Height=38};
    private readonly Dictionary<ListViewItem,Grid> _realized=[];
    private readonly Dictionary<string,Button> _heads=[];
    private bool _busy;
    public Func<ReportListItem,bool>? CanCheck {get;set;}
    public Func<bool>? CanKeep {get;set;}
    public event Func<ReportListItem,Task>? CheckRequested;
    public event Func<ReportListItem,Task>? KeepRequested;
    public event Func<ReportListItem,Task>? OpenRequested;
    public event Func<string,Task>? SortRequested;
    public event Action<string,FrameworkElement>? FilterRequested;
    public event Action? SelectionChanged;
    public IReadOnlyList<ReportListItem> Selected=>_rows.SelectedItems.OfType<ReportListItem>().ToArray();
    public ReportListItem? FirstSelected=>_rows.SelectedItems.OfType<ReportListItem>().FirstOrDefault();
    public ReportListGrid()
    {
        var table=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star));table.RowSpacing=0;
        PlanningUi.Place(table,_header,0);PlanningUi.Place(table,_rows,1);
        Children.Add(new ScrollViewer{Content=table,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalContentAlignment=VerticalAlignment.Stretch});
        _header.Background=PlanningUi.Brush("LimsSurfaceSecondaryBrush");
        _rows.ItemsPanel=(ItemsPanelTemplate)XamlReader.Load("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsStackPanel ItemsUpdatingScrollMode='KeepScrollOffset'/></ItemsPanelTemplate>");
        var style=new Style(typeof(ListViewItem)){BasedOn=(Style)Application.Current.Resources["LimsDataRowStyle"]};
        style.Setters.Add(new Setter(ListViewItem.MinHeightProperty,34d));style.Setters.Add(new Setter(ListViewItem.PaddingProperty,new Thickness(0)));
        style.Setters.Add(new Setter(ListViewItem.MarginProperty,new Thickness(0)));style.Setters.Add(new Setter(ListViewItem.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));
        _rows.ItemContainerStyle=style;
        AutomationProperties.SetName(_rows,"Ejecuciones de Report List");
        _rows.ContainerContentChanging+=ContainerChanging;
        _rows.SelectionChanged+=(_,_)=>SelectionChanged?.Invoke();
        _rows.AddHandler(KeyDownEvent,new KeyEventHandler(async(_,e)=>
        {
            if(e.OriginalSource is CheckBox)return;
            if(e.Key==VirtualKey.Enter&&FirstSelected is {} item){e.Handled=true;if(OpenRequested is {} open)await open(item);}
        }),false);
        var menu=new MenuFlyout();var dossier=new MenuFlyoutItem{Text="Expediente e historial"};
        dossier.Click+=async(_,_)=>{if(FirstSelected is {} item&&OpenRequested is {} open)await open(item);};menu.Items.Add(dossier);_rows.ContextFlyout=menu;
        BuildHeader();
    }
    public void SetSource(ReportListRows? rows){_realized.Clear();_rows.ItemsSource=rows;}
    public void SelectAll()=>_rows.SelectAll();
    public void ClearSelection()=>_rows.SelectedItems.Clear();
    public void SetBusy(bool value){_busy=value;foreach(var grid in _realized.Values)if(grid.Tag is ReportListItem item)Populate(grid,item);}
    public void SetSort(string key,bool descending)
    {foreach(var c in _columns)_heads[c.Key].Content=c.Label+(c.Key==key?(descending?" ↓":" ↑"):"");}
    public void SetFilters(IEnumerable<string> keys)
    {var active=keys.ToHashSet();foreach(var (key,button) in _heads)button.Foreground=PlanningUi.Brush(active.Contains(key)?"LimsAccentBrush":"LimsTextPrimaryBrush");}
    private void BuildHeader()
    {
        _header.Children.Clear();_heads.Clear();
        foreach(var c in _columns)
        {
            var head=new Grid{Width=c.Width,BorderBrush=PlanningUi.Brush("LimsBorderBrush"),BorderThickness=new(0,0,1,1)};
            head.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});head.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var sort=PlanningUi.Button(c.Label,async()=>{if(SortRequested is {} action)await action(c.Key);});
            sort.FontSize=12;sort.MinWidth=0;sort.MinHeight=38;sort.Padding=new(8,0,0,0);sort.BorderThickness=new(0);sort.CornerRadius=new(0);
            sort.Background=PlanningUi.Brush("LimsSurfaceSecondaryBrush");sort.HorizontalContentAlignment=HorizontalAlignment.Left;
            AutomationProperties.SetName(sort,"Ordenar Report List por "+c.Label);head.Children.Add(sort);_heads[c.Key]=sort;
            if(c.Filter)
            {
                var filter=new Button{Content=new FontIcon{Glyph="\uE71C",FontSize=11},Width=24,MinWidth=24,MinHeight=38,Padding=new(3),Style=(Style)Application.Current.Resources["LimsIconButtonStyle"]};
                AutomationProperties.SetName(filter,"Filtrar Report List por "+c.Label);filter.Click+=(_,_)=>FilterRequested?.Invoke(c.Key,filter);Grid.SetColumn(filter,1);head.Children.Add(filter);
            }
            var grip=new Border{Width=5,HorizontalAlignment=HorizontalAlignment.Right,Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent),ManipulationMode=ManipulationModes.TranslateX};
            AutomationProperties.SetName(grip,"Redimensionar Report List "+c.Label);Grid.SetColumnSpan(grip,2);head.Children.Add(grip);
            grip.ManipulationDelta+=(_,e)=>
            {
                var i=_columns.FindIndex(x=>x.Key==c.Key);var width=Math.Clamp(_columns[i].Width+e.Delta.Translation.X,65,600);_columns[i]=_columns[i] with{Width=width};head.Width=width;ResizeRows();e.Handled=true;
            };
            _header.Children.Add(head);
        }
        _rows.Width=_columns.Sum(c=>c.Width);
        _rows.ItemTemplate=(DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Height='34' HorizontalAlignment='Left'/></DataTemplate>");
    }
    private void ResizeRows()
    {
        _rows.Width=_columns.Sum(c=>c.Width);
        foreach(var grid in _realized.Values){grid.Width=_columns.Sum(c=>c.Width);for(int i=0;i<_columns.Count;i++)grid.ColumnDefinitions[i].Width=new(_columns[i].Width);}
    }
    private void ContainerChanging(ListViewBase sender,ContainerContentChangingEventArgs args)
    {
        if(args.ItemContainer is not ListViewItem container)return;
        if(args.InRecycleQueue){_realized.Remove(container);return;}
        if(args.Item is not ReportListItem item||container.ContentTemplateRoot is not Grid grid)return;
        _realized[container]=grid;Populate(grid,item);
    }
    private void Populate(Grid grid,ReportListItem item)
    {
        grid.Tag=item;grid.Width=_columns.Sum(c=>c.Width);grid.ColumnDefinitions.Clear();grid.Children.Clear();
        for(int i=0;i<_columns.Count;i++)
        {
            var c=_columns[i];grid.ColumnDefinitions.Add(new(){Width=new(c.Width)});FrameworkElement content;
            if(c.Key is "Checked" or "Keep")
            {
                var check=new CheckBox{IsChecked=c.Key=="Checked"?item.Execution.Complete:item.KeepInPlanning,MinWidth=0,MinHeight=0,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
                AutomationProperties.SetName(check,(c.Key=="Checked"?"CHK ":"Mantener ")+item.Execution.Job+" · "+item.Execution.PlanningTest+" · "+item.Execution.ExecutionId);
                check.IsEnabled=!_busy&&(c.Key=="Checked"?(CanCheck?.Invoke(item)??false):(CanKeep?.Invoke()??false));
                ToolTipService.SetToolTip(check,c.Key=="Checked"?"Modo: "+item.CheckMode+" · "+item.Execution.OriginalTest:"Conservar en Planificación después del marcado");
                check.Click+=async(_,_)=>
                {
                    // Persisted state is displayed only after the API accepts the versioned change.
                    check.IsChecked=c.Key=="Checked"?item.Execution.Complete:item.KeepInPlanning;
                    if(c.Key=="Checked"&&CheckRequested is {} action)await action(item);
                    if(c.Key=="Keep"&&KeepRequested is {} keep)await keep(item);
                };
                content=check;
            }
            else
            {
                var value=c.Key switch{"Block"=>item.BlockLabel,"Job"=>item.Execution.Job,"Test"=>item.Execution.PlanningTest,"Laboratory"=>item.Execution.Laboratory,"Buyer"=>item.Execution.Buyer,"VerifiedBy"=>item.Execution.DigitizedBy,"Date"=>item.CheckedDateText,"Time"=>item.CheckedTimeText,_=>""};
                var text=PlanningUi.Text(value??"",12);text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;text.VerticalAlignment=VerticalAlignment.Center;text.Margin=new(8,0,8,0);
                if(c.Key=="Test")text.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
                ToolTipService.SetToolTip(text,c.Key=="Test"?$"{item.Execution.OriginalTest}\nEjecución: {item.Execution.ExecutionId}\nModo: {item.CheckMode}":value);
                content=text;
            }
            var cell=new Border{Child=content,BorderBrush=PlanningUi.Brush("LimsBorderBrush"),BorderThickness=new(0,0,1,1)};Grid.SetColumn(cell,i);grid.Children.Add(cell);
        }
    }
}
