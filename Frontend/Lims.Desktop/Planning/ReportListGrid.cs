using Lims.Contracts.Planning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Core;
using Column = Lims.Desktop.Planning.ReportListTableModel.Column;
using Lims.DesignSystem.Controls;
using System.Runtime.CompilerServices;

namespace Lims.Desktop.Planning;

/// <summary>Report List table in the existing WinUI design system. Realized rows only create controls.</summary>
internal sealed class ReportListGrid : Grid
{
    private readonly ReportListTableModel _model=new();
    private List<Column> _columns=>_model.Columns;
    private string? _preferences;
    private string _sort="",_preferenceError="";
    private bool _descending,_dragging;
    private HashSet<string> _filtered=[];
    public event Action<string>? PreferenceError;
    private readonly ListView _rows=new(){SelectionMode=ListViewSelectionMode.None,Padding=new(0),BorderThickness=new(0),IsMultiSelectCheckBoxEnabled=false};
    private readonly HashSet<Guid> _marked=[];
    private CheckBox? _visibleCheck;
    private ReportListItem? _contextItem;
    private int? _checkAnchor;
    private readonly ReportTableViewport _viewport;
    private readonly StackPanel _header=new(){Orientation=Orientation.Horizontal,Height=40};
    private readonly Dictionary<ListViewItem,Grid> _realized=[];
    private readonly Dictionary<string,Button> _heads=[];
    private readonly Brush _completed=new SolidColorBrush(Windows.UI.Color.FromArgb(38,34,197,94));
    private readonly Brush _transparent=new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    private sealed class RowState {public int LayoutVersion=-1;public int Index;}
    private readonly ConditionalWeakTable<Grid,RowState> _rowStates=new();
    private int _layoutVersion;
    private bool _visibleCheckPending;
    private bool _busy;
    private double _fitWidth;
    public Func<bool>? CanKeep {get;set;}
    public Func<bool>? CanMark {get;set;}
    public Func<bool>? CanCorrect {get;set;}
    public Func<bool>? IsAdministrator {get;set;}
    public event Func<ReportListItem,Task>? CheckRequested;
    public event Func<ReportListItem,Task>? KeepRequested;
    public event Func<ReportListItem,Task>? CorrectionRequested;
    public event Func<ReportListItem,Task>? OpenRequested;
    public event Func<string,Task>? SortRequested;
    public event Action<string,FrameworkElement>? FilterRequested;
    public event Action? SelectionChanged;
    public IReadOnlyList<ReportListItem> Selected=>_model.Rows.Where(IsMarked).ToArray();
    private bool IsMarked(ReportListItem item)=>item.Execution.ExecutionId is {} id&&_marked.Contains(id);
    public ReportListItem? FirstSelected=>Selected is {Count:>0} selected?selected[0]:null;
    public int SelectedOutsideView=>Selected.Count(item=>!VisibleItems().Contains(item));
    public ReportListGrid()
    {
        _viewport=new(_header,_rows);Children.Add(_viewport);
        _viewport.Scrolled+=QueueVisibleCheck;
        _header.Background=PlanningUi.Brush("LimsSurfaceSecondaryBrush");
        _rows.ItemsPanel=(ItemsPanelTemplate)XamlReader.Load("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsStackPanel ItemsUpdatingScrollMode='KeepScrollOffset'/></ItemsPanelTemplate>");
        var style=new Style(typeof(ListViewItem)){BasedOn=(Style)Application.Current.Resources["LimsDataRowStyle"]};
        style.Setters.Add(new Setter(ListViewItem.MinHeightProperty,32d));style.Setters.Add(new Setter(ListViewItem.PaddingProperty,new Thickness(0)));
        style.Setters.Add(new Setter(ListViewItem.MarginProperty,new Thickness(0)));style.Setters.Add(new Setter(ListViewItem.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(ListViewItem.UseSystemFocusVisualsProperty,false));
        _rows.UseSystemFocusVisuals=false;
        _rows.ItemContainerStyle=style;
        AutomationProperties.SetName(_rows,"Ejecuciones de Report List");
        _rows.ContainerContentChanging+=ContainerChanging;
        _rows.PreviewKeyDown+=OnTableKeyDown;
        _rows.Loaded+=(_,_)=>QueueVisibleCheck();
        _rows.SizeChanged+=(_,_)=>{FitColumns();QueueVisibleCheck();};
        AddHandler(PointerReleasedEvent,new PointerEventHandler((_,_)=>_dragging=false),true);
        Unloaded+=(_,_)=>_dragging=false;
        var menu=new MenuFlyout();var dossier=new MenuFlyoutItem{Text="Expediente e historial"};
        dossier.Click+=async(_,_)=>{if(_contextItem is {} item&&OpenRequested is {} open)await open(item);};menu.Items.Add(dossier);
        var correction=new MenuFlyoutItem{Text="Corregir digitación"};correction.Click+=async(_,_)=>{if(_contextItem is {} item&&CorrectionRequested is {} action)await action(item);};menu.Items.Add(correction);
        menu.Opening+=(_,_)=>{dossier.IsEnabled=_contextItem is not null;correction.Visibility=_contextItem?.Execution.Complete==true&&(CanKeep?.Invoke()??false)?Visibility.Visible:Visibility.Collapsed;correction.IsEnabled=!_busy;};_rows.ContextFlyout=menu;
        BuildHeader();
    }
    public void SetSource(ReportListRows? rows){_dragging=false;_marked.Clear();_contextItem=null;_checkAnchor=null;_model.ClearCells();_model.Rows=rows is null?Array.Empty<ReportListItem>():rows;_realized.Clear();_rows.ItemsSource=rows;_viewport.Scroll?.ChangeView(0,0,null,true);RefreshSelection();}
    private void FitColumns()
    {
        var width=Math.Floor(_rows.ActualWidth)-16;
        if(width<700||Math.Abs(width-_fitWidth)<2)return;
        _fitWidth=width;_model.Fit(width);BuildHeader();RefreshRealized();
    }
    public void RefreshSaved(){_model.ClearCells();foreach(var grid in _realized.Values)if(grid.Tag is ReportListItem item){var index=_model.Rows.IndexOfItem(item);if(index>=0)_rowStates.GetValue(grid,static _=>new()).Index=index;}RefreshRealized();QueueVisibleCheck();}
    public void SetUser(int? id)
    {
        var path=id is >0?System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Lims","ReportList","columns-"+id+".json"):null;
        if(_preferences==path)return;_preferences=path;
        try{_model.LoadLayout(path is not null&&System.IO.File.Exists(path)?System.IO.File.ReadAllText(path):null);}
        catch(Exception ex)when(ex is System.IO.IOException or UnauthorizedAccessException){_model.Restore();PreferenceError?.Invoke("No se pudieron leer las preferencias de columnas.");}
        BuildHeader();RefreshRealized();_fitWidth=0;FitColumns();
    }
    private void SavePreferences()
    {
        if(_preferences is null)return;
        try{System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_preferences)!);System.IO.File.WriteAllText(_preferences+".tmp",_model.SaveLayout());System.IO.File.Move(_preferences+".tmp",_preferences,true);_preferenceError="";}
        catch(Exception ex)when(ex is System.IO.IOException or UnauthorizedAccessException){if(_preferenceError.Length==0)PreferenceError?.Invoke(_preferenceError="No se pudieron guardar las preferencias de columnas.");}
    }
    public void ClearSelection(){_marked.Clear();_model.ClearCells();RefreshSelection();SelectionChanged?.Invoke();}
    public void SetBusy(bool value){_busy=value;foreach(var grid in _realized.Values)if(grid.Tag is ReportListItem item)Populate(grid,item);}
    private void RefreshRealized(){foreach(var grid in _realized.Values)if(grid.Tag is ReportListItem item)Populate(grid,item);}
    private void RefreshSelection()
    {
        foreach(var grid in _realized.Values)foreach(var cell in grid.Children.OfType<Border>())if(cell.Child is CheckBox {Tag:"Selection"} check&&grid.Tag is ReportListItem item){check.IsChecked=CheckValue(item);AutomationProperties.SetItemStatus(check,ReportListTableModel.CheckDescription(item,IsMarked(item)));}
        UpdateVisibleCheck();PaintCells();
    }
    private bool? CheckValue(ReportListItem item)=>item.Execution.Complete||IsMarked(item)?true:item.Execution.DigitizedAt is not null||item.Execution.TestedOn is not null?null:false;
    private HashSet<ReportListItem> VisibleItems()=>_realized.Values.Where(grid=>{var p=grid.TransformToVisual(_rows).TransformPoint(new Windows.Foundation.Point(0,0));return grid.ActualHeight>0&&p.Y>=0&&p.Y+grid.ActualHeight<=(_viewport.Scroll?.ViewportHeight??_rows.ActualHeight)+1;}).Select(grid=>grid.Tag).OfType<ReportListItem>().ToHashSet();
    private void QueueVisibleCheck()
    {
        if(_visibleCheck is null||_visibleCheckPending)return;_visibleCheckPending=true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,()=>{_visibleCheckPending=false;UpdateVisibleCheck();});
    }
    private void UpdateVisibleCheck()
    {
        if(_visibleCheck is null)return;var visible=VisibleItems().Where(ReportListTableModel.CanMark).ToArray();var count=visible.Count(IsMarked);
        _visibleCheck.IsChecked=count==0?false:count==visible.Length?true:null;_visibleCheck.IsEnabled=!_busy&&(CanMark?.Invoke()??false)&&visible.Length>0;
    }
    private void ToggleMark(ReportListItem item,bool extend)
    {
        if(_busy||!(CanMark?.Invoke()??false)||!ReportListTableModel.CanMark(item))return;var index=_model.Rows.IndexOfItem(item);var add=!IsMarked(item);
        var visible=VisibleItems();var items=extend&&_checkAnchor is {} anchor?_model.Rows.Skip(Math.Min(anchor,index)).Take(Math.Abs(anchor-index)+1).Where(visible.Contains):new[]{item};
        foreach(var row in items.Where(ReportListTableModel.CanMark)){var id=row.Execution.ExecutionId!.Value;if(add)_marked.Add(id);else _marked.Remove(id);}
        _checkAnchor=index;_model.ClearCells();RefreshSelection();SelectionChanged?.Invoke();
    }
    public MenuFlyout ColumnsMenu()
    {
        var menu=new MenuFlyout();var columns=new MenuFlyoutSubItem{Text="Columnas"};menu.Items.Add(columns);foreach(var column in ReportListTableModel.Available)
        {
            var toggle=new ToggleMenuFlyoutItem{Text=column.Label,IsEnabled=!ReportListTableModel.Required.Contains(column.Key)};
            toggle.Click+=(_,_)=>{if(toggle.IsChecked)_columns.Add(column);else _columns.RemoveAll(c=>c.Key==column.Key);LayoutChanged();};columns.Items.Add(toggle);
        }
        menu.Opening+=(_,_)=>{foreach(var toggle in columns.Items.OfType<ToggleMenuFlyoutItem>())toggle.IsChecked=_columns.Any(c=>c.Label==toggle.Text);};
        columns.Items.Add(new MenuFlyoutSeparator());var restore=new MenuFlyoutItem{Text="Restablecer columnas"};restore.Click+=(_,_)=>{_model.Restore();LayoutChanged();};columns.Items.Add(restore);
        return menu;
    }
    private void LayoutChanged(){_model.ClearCells();_fitWidth=0;FitColumns();BuildHeader();RefreshRealized();SavePreferences();}
    public void SetSort(string key,bool descending)
    {_sort=key;_descending=descending;foreach(var c in _columns)if(_heads.TryGetValue(c.Key,out var head)){var caption=PlanningUi.Text(c.Label+(c.Key==key?(descending?" ↓":" ↑"):""),11);caption.TextWrapping=c.Key=="Keep"?TextWrapping.Wrap:TextWrapping.NoWrap;caption.TextTrimming=TextTrimming.CharacterEllipsis;caption.MaxLines=c.Key=="Keep"?2:1;caption.VerticalAlignment=VerticalAlignment.Center;head.Content=caption;}}
    public void SetFilters(IEnumerable<string> keys)
    {_filtered=keys.Select(key=>key switch{"TestedOn"=>"Date","DigitizedAt"=>"Time","DigitizationState"=>"Checked",_=>key}).ToHashSet();foreach(var (key,button) in _heads){button.Foreground=PlanningUi.Brush(_filtered.Contains(key)?"LimsAccentBrush":"LimsTextPrimaryBrush");if(button.Content is TextBlock text)text.Foreground=button.Foreground;}}
    private void BuildHeader()
    {
        _layoutVersion++;
        _header.Children.Clear();_heads.Clear();_visibleCheck=null;
        foreach(var c in _columns)
        {
            var head=new Grid{Width=c.Width,BorderBrush=PlanningUi.Brush("LimsBorderBrush"),BorderThickness=new(0,0,1,1)};
            if(c.Key=="Check")
            {
                var label=PlanningUi.Text("CHK",11);label.HorizontalAlignment=HorizontalAlignment.Center;label.VerticalAlignment=VerticalAlignment.Center;head.Children.Add(label);_header.Children.Add(head);continue;
            }
            if(c.Key=="Selection")
            {
                _visibleCheck=new CheckBox{Content="Sel.",IsThreeState=true,MinWidth=0,FontSize=12,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
                AutomationProperties.SetName(_visibleCheck,"Marcar pruebas pendientes visibles");ToolTipService.SetToolTip(_visibleCheck,"Marcar o desmarcar solo las pruebas pendientes visibles");
                _visibleCheck.Click+=(_,_)=>{var visible=VisibleItems().Where(ReportListTableModel.CanMark).ToArray();var add=visible.Any(item=>!IsMarked(item));foreach(var item in visible){if(add)_marked.Add(item.Execution.ExecutionId!.Value);else _marked.Remove(item.Execution.ExecutionId!.Value);}RefreshSelection();SelectionChanged?.Invoke();};head.Children.Add(_visibleCheck);_header.Children.Add(head);continue;
            }
            head.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});head.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var sort=PlanningUi.Button(c.Label,async()=>{if(c.Key!="Selection"&&SortRequested is {} action)await action(c.Key);});
            sort.FontSize=11;sort.MinWidth=0;sort.MinHeight=38;sort.Padding=new(6,0,2,0);sort.BorderThickness=new(0);sort.CornerRadius=new(0);
            sort.Background=PlanningUi.Brush("LimsSurfaceSecondaryBrush");sort.HorizontalContentAlignment=HorizontalAlignment.Left;
            AutomationProperties.SetName(sort,"Ordenar Report List por "+c.Label);head.Children.Add(sort);_heads[c.Key]=sort;
            var orderMenu=new MenuFlyout();
            foreach(var (label,delta) in new[]{("Mover columna a la izquierda",-1),("Mover columna a la derecha",1)})
            {
                var index=_columns.FindIndex(x=>x.Key==c.Key);var target=index+delta;
                var move=new MenuFlyoutItem{Text=label,IsEnabled=target>=0&&target<_columns.Count};
                move.Click+=(_,_)=>{var current=_columns.FindIndex(x=>x.Key==c.Key);var column=_columns[current];_columns.RemoveAt(current);_columns.Insert(target,column);LayoutChanged();};orderMenu.Items.Add(move);
            }
            var widthItem=new MenuFlyoutItem{Text="Cambiar ancho de columna"};
            widthItem.Click+=async(_,_)=>
            {
                var index=_columns.FindIndex(x=>x.Key==c.Key);var input=PlanningUi.Field("Ancho (px)",_columns[index].Width.ToString("0",System.Globalization.CultureInfo.InvariantCulture),3);
                AutomationProperties.SetName(input,"Ancho de columna en píxeles");var error=PlanningUi.Text("",12,true);
                var dialog=new ContentDialog{XamlRoot=XamlRoot,Style=(Style)Application.Current.Resources["LimsEditorDialogStyle"],Title=c.Label,Content=PlanningUi.Stack(input,error),PrimaryButtonText="Aplicar ancho",CloseButtonText="Cancelar"};
                dialog.PrimaryButtonClick+=(_,e)=>{if(!int.TryParse(input.Text,out var width)||width<65||width>700){error.Text="El ancho debe estar entre 65 y 700 px.";e.Cancel=true;}};
                if(await dialog.ShowAsync()==ContentDialogResult.Primary){_columns[index]=_columns[index] with{Width=int.Parse(input.Text,System.Globalization.CultureInfo.InvariantCulture)};LayoutChanged();}
            };
            orderMenu.Items.Add(widthItem);
            sort.ContextFlyout=orderMenu;ToolTipService.SetToolTip(sort,"Ordenar · clic derecho para mover la columna");
            if(c.Key is not ("Selection" or "Keep"))
            {
                var filter=new Button{Content=new FontIcon{Glyph="\uE71C",FontSize=10},Width=20,MinWidth=20,MinHeight=38,Padding=new(2),Style=(Style)Application.Current.Resources["LimsIconButtonStyle"]};
                AutomationProperties.SetName(filter,"Filtrar Report List por "+c.Label);filter.Click+=(_,_)=>FilterRequested?.Invoke(c.Key,filter);Grid.SetColumn(filter,1);head.Children.Add(filter);
            }
            var grip=new Microsoft.UI.Xaml.Controls.Primitives.Thumb{Width=7,MinWidth=0,HorizontalAlignment=HorizontalAlignment.Right,IsTabStop=false,
                Template=(ControlTemplate)XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Border Background='Transparent'/></ControlTemplate>")};
            AutomationProperties.SetName(grip,"Redimensionar Report List "+c.Label);Grid.SetColumnSpan(grip,2);head.Children.Add(grip);
            grip.DragDelta+=(_,e)=>
            {
                var i=_columns.FindIndex(x=>x.Key==c.Key);var width=Math.Clamp(_columns[i].Width+e.HorizontalChange,65,700);_columns[i]=_columns[i] with{Width=width};head.Width=width;ResizeRows();
            };
            grip.DragCompleted+=(_,_)=>{RefreshRealized();SavePreferences();};
            _header.Children.Add(head);
        }
        _header.Width=_columns.Sum(c=>c.Width);
        _rows.ItemTemplate=(DataTemplate)XamlReader.Load($"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Height='32' Width='{_header.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)}' HorizontalAlignment='Left'/></DataTemplate>");
        SetSort(_sort,_descending);SetFilters(_filtered);
    }
    private void ResizeRows()
    {
        _header.Width=_columns.Sum(c=>c.Width);
        foreach(var grid in _realized.Values){grid.Width=_columns.Sum(c=>c.Width);for(int i=0;i<_columns.Count;i++)grid.ColumnDefinitions[i].Width=new(_columns[i].Width);}
    }
    private void ContainerChanging(ListViewBase sender,ContainerContentChangingEventArgs args)
    {
        if(args.ItemContainer is not ListViewItem container)return;
        if(args.InRecycleQueue){_realized.Remove(container);QueueVisibleCheck();return;}
        if(args.Phase==0&&container.ContentTemplateRoot is not Grid){args.RegisterUpdateCallback(1,ContainerChanging);return;}
        if(args.Item is not ReportListItem item||container.ContentTemplateRoot is not Grid grid)return;
        if(args.ItemIndex<0||args.ItemIndex>=_model.Rows.Count||!ReferenceEquals(_model.Rows[args.ItemIndex],item))return;
        _rowStates.GetValue(grid,static _=>new()).Index=args.ItemIndex;
        _realized[container]=grid;Populate(grid,item);QueueVisibleCheck();args.Handled=true;
    }
    private void Populate(Grid grid,ReportListItem item)
    {
        grid.Tag=item;
        if(_rowStates.GetValue(grid,static _=>new()).LayoutVersion!=_layoutVersion)BuildRow(grid);
        var row=RowIndex(grid);
        for(int i=0;i<_columns.Count;i++)
        {
            var c=_columns[i];var content=((Border)grid.Children[i]).Child;
            if(content is CheckBox check)
            {
                check.IsChecked=c.Key=="Check"?item.Execution.Complete:c.Key=="Selection"?CheckValue(item):item.KeepInPlanning;
                check.IsEnabled=!_busy&&(c.Key=="Check"?ReportListTableModel.CanCheck(item,CanMark?.Invoke()??false,CanCorrect?.Invoke()??false,IsAdministrator?.Invoke()??false):c.Key=="Selection"?(CanMark?.Invoke()??false)&&ReportListTableModel.CanMark(item):(CanKeep?.Invoke()??false));
                check.Content=c.Key=="Selection"&&item.Execution.Complete?new LimsIcon{Icon=LimsIconKind.Lock,Size=11}:null;
                AutomationProperties.SetName(check,(c.Key=="Check"?"CHK ":c.Key=="Selection"?"Seleccionar ":"Mantener en Muestreo ")+item.Execution.Job+" · "+item.Execution.PlanningTest+" · "+item.Execution.ExecutionId);
                AutomationProperties.SetItemStatus(check,ReportListTableModel.CheckDescription(item,IsMarked(item)));
                ToolTipService.SetToolTip(check,c.Key=="Check"?item.CheckMode!="manual"&&!(IsAdministrator?.Invoke()??false)?"Esta prueba se completa con el código AR del reporte.":item.Execution.Complete?"Digitación guardada. Desmarcar registra una corrección e invalida la revisión del horario.":"Guarda esta prueba con la fecha y hora fijadas.":c.Key=="Selection"?ReportListTableModel.CheckDescription(item,IsMarked(item)):"Conservar en Muestreo después de digitar");
            }
            else if(content is TextBlock text)
            {
                var value=ReportListTableModel.Value(item,c.Key);text.Text=value;
                text.Foreground=PlanningUi.Brush(item.Execution.Complete&&c.Key is "Time" or "Date" or "VerifiedBy" or "Checked"?"LimsAccentBrush":"LimsTextPrimaryBrush");
                ToolTipService.SetToolTip(text,c.Key=="Test"?$"Prueba: {value}\nNombre técnico: {item.Execution.OriginalTest}\nEjecución: {item.Execution.ExecutionId}\nModo: {item.CheckMode}":value);
                AutomationProperties.SetName(text,$"F{row+1} C{i+1} · {c.Label} · {value}");
            }
        }
        PaintGrid(grid);
    }
    private void BuildRow(Grid grid)
    {
        grid.Width=_columns.Sum(c=>c.Width);grid.ColumnDefinitions.Clear();grid.Children.Clear();
        _rowStates.GetValue(grid,static _=>new()).LayoutVersion=_layoutVersion;
        for(int i=0;i<_columns.Count;i++)
        {
            var c=_columns[i];grid.ColumnDefinitions.Add(new(){Width=new(c.Width)});FrameworkElement content;
            if(c.Key is "Selection" or "Keep" or "Check")
            {
                var check=new CheckBox{Tag=c.Key,IsThreeState=c.Key=="Selection",MinWidth=0,MinHeight=0,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
                check.RightTapped+=(_,_)=>_contextItem=grid.Tag as ReportListItem;
                check.Click+=async(_,_)=>
                {
                    if(grid.Tag is not ReportListItem item)return;
                    if(c.Key=="Check"){check.IsChecked=item.Execution.Complete;if(CheckRequested is{} action)await action(item);return;}
                    if(c.Key=="Selection"){ToggleMark(item,Shift());return;}
                    check.IsChecked=item.KeepInPlanning;
                    if(c.Key=="Keep"&&KeepRequested is {} keep)await keep(item);
                };
                content=check;
            }
            else
            {
                var text=PlanningUi.Text("",11);text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;text.VerticalAlignment=VerticalAlignment.Center;text.Margin=new(7,0,7,0);
                if(c.Key=="Test")text.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
                content=text;
            }
            var cell=new Border{Child=content,Tag=i,BorderBrush=PlanningUi.Brush("LimsBorderBrush"),BorderThickness=new(0,0,1,1),Background=_transparent};
            cell.RightTapped+=(_,_)=>_contextItem=grid.Tag as ReportListItem;
            cell.DoubleTapped+=async(_,e)=>{if(content is CheckBox||grid.Tag is not ReportListItem item)return;e.Handled=true;if(CheckRequested is{} action)await action(item);};
            var columnIndex=i;
            if(content is not CheckBox)
            {
                cell.PointerPressed+=(_,e)=>{_contextItem=grid.Tag as ReportListItem;if(e.GetCurrentPoint(cell).Properties.IsRightButtonPressed){_model.Select(RowIndex(grid),columnIndex);PaintCells();return;}if(!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed)return;_model.Select(RowIndex(grid),columnIndex,Shift());_dragging=true;_rows.Focus(FocusState.Programmatic);PaintCells();e.Handled=true;};
                cell.PointerEntered+=(_,e)=>{if(_dragging&&e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed){_model.Select(RowIndex(grid),columnIndex,true);PaintCells();}};
            }
            Grid.SetColumn(cell,i);grid.Children.Add(cell);
        }
    }
    private int RowIndex(Grid grid)=>_rowStates.TryGetValue(grid,out var state)?state.Index:0;
    private static bool Shift()=>(Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)&CoreVirtualKeyStates.Down)!=0;
    private static bool Control()=>(Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)&CoreVirtualKeyStates.Down)!=0;
    private void PaintCells(){foreach(var grid in _realized.Values)PaintGrid(grid);}
    private void PaintGrid(Grid grid)
    {
        if(grid.Tag is not ReportListItem item)return;var row=RowIndex(grid);
        foreach(var cell in grid.Children.OfType<Border>())
        {
            var col=(int)cell.Tag;var selected=_model.Contains(row,col);var active=selected&&_model.Active==new SheetCell(row,col);
            cell.Background=selected||IsMarked(item)?PlanningUi.Brush("LimsSelectionBrush"):item.Execution.Complete?_completed:_transparent;
            var clock=item.Execution.DigitizedAt?.ToUnixTimeSeconds()/60;
            var previous=row>0?_model.Rows[row-1].Execution.DigitizedAt?.ToUnixTimeSeconds()/60:null;
            var starts=item.Execution.Complete&&clock!=previous;
            cell.BorderBrush=PlanningUi.Brush(active||starts?"LimsAccentBrush":"LimsBorderBrush");cell.BorderThickness=active?new(1):new(0,starts?1:0,0,0.5);
        }
    }
    private async void OnTableKeyDown(object sender,KeyRoutedEventArgs e)
    {
        if(e.OriginalSource is CheckBox)return;
        if(Control()&&e.Key==VirtualKey.C)
        {
            var text=_model.HasCells?_model.Copy():string.Join("\r\n",Selected.Select(r=>string.Join('\t',_columns.Select(c=>ReportListTableModel.Quote(ReportListTableModel.Value(r,c.Key))))));
            if(text.Length>0){var data=new DataPackage();data.SetText(text);Clipboard.SetContent(data);}e.Handled=true;return;
        }
        if(Control()&&e.Key==VirtualKey.A){_model.Select(0,0);_model.Select(_model.Rows.Count-1,_columns.Count-1,true);PaintCells();e.Handled=true;return;}
        if(e.Key==VirtualKey.Escape){ClearSelection();e.Handled=true;return;}
        if(_model.Rows.Count==0)return;
        var a=_model.Active;var row=a.Row;var col=a.Column;
        switch(e.Key)
        {
            case VirtualKey.Left:col--;break;case VirtualKey.Right:col++;break;
            case VirtualKey.Up:row--;break;case VirtualKey.Down:row++;break;
            case VirtualKey.Enter:e.Handled=true;if(CheckRequested is{} action)await action(_model.Rows[row]);return;
            case VirtualKey.Tab:col+=Shift()?-1:1;if(col<0){col=_columns.Count-1;row--;}else if(col>=_columns.Count){col=0;row++;}break;
            case VirtualKey.Home:col=0;if(Control())row=0;break;
            case VirtualKey.End:col=_columns.Count-1;if(Control())row=_model.Rows.Count-1;break;
            case VirtualKey.Space:ToggleMark(_model.Rows[row],Shift());e.Handled=true;return;
            default:return;
        }
        _model.Select(row,col,Shift()&&e.Key is not (VirtualKey.Tab or VirtualKey.Enter));PaintCells();
        _rows.ScrollIntoView(_model.Rows[_model.Active.Row]);var left=_columns.Take(_model.Active.Column).Sum(c=>c.Width);var right=left+_columns[_model.Active.Column].Width;
        _viewport.Reveal(left,right);
        e.Handled=true;
    }
}

internal static class ReportListRowIndex
{
    public static int IndexOfItem(this IReadOnlyList<ReportListItem> rows,ReportListItem item)=>rows is IList<ReportListItem> list?list.IndexOf(item):Enumerable.Range(0,rows.Count).FirstOrDefault(i=>ReferenceEquals(rows[i],item),-1);
}
