using System.Globalization;
using System.Net;
using System.Text;
using Lims.Contracts.Planning;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Lims.Desktop.Planning;

internal sealed partial class ReportClosureGrid
{
    private readonly List<ReportClosure> _data=[];
    private readonly Dictionary<SelectorItem,Grid> _realized=[];
    private readonly DispatcherTimer _dragScroll=new(){Interval=TimeSpan.FromMilliseconds(60)};
    private SheetCell _anchor,_active;
    private bool _dragging,_shiftPressed;
    private Point _pointer;
    public bool HasSelection {get;private set;}
    public event Action<string>? SelectionFeedback;
    private (int Top,int Left,int Bottom,int Right) SelectionRange=>(Math.Min(_anchor.Row,_active.Row),Math.Min(_anchor.Column,_active.Column),Math.Max(_anchor.Row,_active.Row),Math.Max(_anchor.Column,_active.Column));
    private static bool Down(VirtualKey key)=>(InputKeyboardSource.GetKeyStateForCurrentThread(key)&CoreVirtualKeyStates.Down)!=0;
    private bool Shift()=>_shiftPressed||new[]{VirtualKey.Shift,VirtualKey.LeftShift,VirtualKey.RightShift}.Any(Down);
    private void ConfigureSelection()
    {
        _list.AddHandler(PreviewKeyDownEvent,new KeyEventHandler(SelectionKey),true);
        _list.AddHandler(KeyUpEvent,new KeyEventHandler((_,e)=>{if(e.Key is VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift)_shiftPressed=false;}),true);
        _list.LostFocus+=(_,_)=>
        {
            var focus=XamlRoot is null?null:FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            while(focus is not null&&focus!=_list)focus=VisualTreeHelper.GetParent(focus);
            if(focus is null)_shiftPressed=false;
        };
        AddHandler(PointerMovedEvent,new PointerEventHandler((_,e)=>{if(!_dragging)return;_pointer=e.GetCurrentPoint(_list).Position;ExtendDrag();e.Handled=true;}),true);
        AddHandler(PointerReleasedEvent,new PointerEventHandler((_,e)=>{StopDragging();ReleasePointerCapture(e.Pointer);}),true);
        PointerCaptureLost+=(_,e)=>{if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)StopDragging();};Unloaded+=(_,_)=>{StopDragging();_shiftPressed=false;};
        _dragScroll.Tick+=(_,_)=>
        {
            if(!_dragging||_viewport.Scroll is not {} scroll)return;
            double dy=_pointer.Y<24?-24:_pointer.Y>_list.ActualHeight-24?24:0;
            double dx=_pointer.X<24?-24:_pointer.X>_list.ActualWidth-24?24:0;
            if(dx!=0||dy!=0){scroll.ChangeView(Math.Clamp(scroll.HorizontalOffset+dx,0,scroll.ScrollableWidth),Math.Clamp(scroll.VerticalOffset+dy,0,scroll.ScrollableHeight),null,true);ExtendDrag();}
        };
        _list.AddHandler(DoubleTappedEvent,new DoubleTappedEventHandler((_,e)=>
        {
            for(var source=e.OriginalSource as DependencyObject;source is not null&&source!=_list;source=VisualTreeHelper.GetParent(source))
                if(source is Border{Tag:SheetCell}){OpenSelection();e.Handled=true;break;}
        }),true);
        var menu=new MenuFlyout();
        void Item(string text,Action action){var item=new MenuFlyoutItem{Text=text};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        Item("Copiar selección",()=>CopySelection());Item("Copiar con encabezados",()=>CopySelection(true));
        Item("Seleccionar todo (página)",SelectAll);Item("Abrir detalle del JOB",OpenSelection);
        menu.Opening+=(_,_)=>{foreach(var item in menu.Items.OfType<MenuFlyoutItem>())item.IsEnabled=HasSelection||item.Text=="Seleccionar todo (página)"&&_data.Count>0;};
        _list.ContextFlyout=menu;
    }
    private string[] Values(ReportClosure row)
    {
        string[] values=[row.Job,row.Buyer??"Sin información",row.LastTest,row.Usd?.ToString("C2",CultureInfo.GetCultureInfo("en-US"))??"Sin información",row.Service??"Sin información"];
        return _grouped?values:[..values,row.LastDigitizedAt.ToOffset(TimeSpan.FromHours(-6)).ToString("dd/MM/yyyy HH:mm",CultureInfo.InvariantCulture)];
    }
    public void Select(int row,int column,bool extend=false)
    {
        if(_data.Count==0)return;
        _active=new(Math.Clamp(row,0,_data.Count-1),Math.Clamp(column,0,_columns.Length-1));
        if(!extend||!HasSelection)_anchor=_active;
        HasSelection=true;PaintSelection();var r=SelectionRange;
        SelectionFeedback?.Invoke($"{r.Bottom-r.Top+1} filas × {r.Right-r.Left+1} columnas · Ctrl+C para copiar");
    }
    public void ClearSelection(){HasSelection=false;PaintSelection();SelectionFeedback?.Invoke("Arrastre para seleccionar · Ctrl+C para copiar · Doble clic para abrir detalle");}
    private void SelectAll(){if(_data.Count==0)return;Select(0,0);Select(_data.Count-1,_columns.Length-1,true);_list.Focus(FocusState.Programmatic);}
    private void SelectColumn(int column){if(_data.Count==0)return;Select(0,column);Select(_data.Count-1,column,true);_list.Focus(FocusState.Programmatic);}
    public void OpenSelection(){if(HasSelection&&_active.Row<_data.Count)OpenRequested?.Invoke(_data[_active.Row]);}
    private void CellPressed(object sender,PointerRoutedEventArgs args)
    {
        if(sender is not Border{Tag:SheetCell cell})return;
        var point=args.GetCurrentPoint(_list);
        if(point.Properties.IsRightButtonPressed){if(!Contains(cell.Row,cell.Column))Select(cell.Row,cell.Column);_list.Focus(FocusState.Programmatic);return;}
        if(!point.Properties.IsLeftButtonPressed)return;
        Select(cell.Row,cell.Column,Shift());_list.Focus(FocusState.Programmatic);_pointer=point.Position;
        _dragging=true;CapturePointer(args.Pointer);_dragScroll.Start();args.Handled=true;
    }
    private void StopDragging(){var captured=_dragging;_dragging=false;_dragScroll.Stop();if(captured)ReleasePointerCaptures();}
    private void ExtendDrag()
    {
        var nearest=_realized.Values.Where(g=>g.Tag is int i&&i>=0&&i<_data.Count)
            .Select(g=>(Grid:g,Y:g.TransformToVisual(_list).TransformPoint(new()).Y))
            .OrderBy(g=>_pointer.Y<g.Y?g.Y-_pointer.Y:_pointer.Y>g.Y+g.Grid.ActualHeight?_pointer.Y-g.Y-g.Grid.ActualHeight:0).FirstOrDefault();
        if(nearest.Grid?.Tag is not int row)return;
        var x=_pointer.X+(_viewport.Scroll?.HorizontalOffset??0);int column=0;
        while(column<_columns.Length-1&&x>=_columns[column].Width){x-=_columns[column].Width;column++;}
        Select(row,column,true);
    }
    private bool Contains(int row,int column){var r=SelectionRange;return HasSelection&&row>=r.Top&&row<=r.Bottom&&column>=r.Left&&column<=r.Right;}
    private void PaintSelection(){foreach(var grid in _realized.Values)if(grid.Tag is int row&&row>=0)PaintRow(grid,row);}
    private void PaintRow(Grid grid,int row)
    {
        for(int col=0;col<grid.Children.Count;col++)
        {
            if(grid.Children[col] is not Border cell)continue;
            var active=HasSelection&&_active==new SheetCell(row,col);var selected=Contains(row,col);
            cell.Background=PlanningSheetGrid.Color(selected?"#164B68":"#0C131A");
            cell.BorderBrush=PlanningSheetGrid.Color(active?"#65CCFF":selected?"#2D7394":"#202A33");
            cell.BorderThickness=active?new Thickness(2):new Thickness(0,0,1,1);
        }
    }
    private void SelectionKey(object sender,KeyRoutedEventArgs args)
    {
        if(args.OriginalSource is TextBox)return;
        if(args.Key is VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift){_shiftPressed=true;return;}
        var ctrl=Down(VirtualKey.Control);
        if(ctrl&&args.Key==VirtualKey.C){CopySelection();args.Handled=true;return;}
        if(ctrl&&args.Key==VirtualKey.A){SelectAll();args.Handled=true;return;}
        if(args.Key==VirtualKey.Escape){ClearSelection();args.Handled=true;return;}
        if(args.Key==VirtualKey.Enter){OpenSelection();args.Handled=true;return;}
        if(args.Key is VirtualKey.F2 or VirtualKey.Delete or VirtualKey.Back||ctrl&&args.Key is VirtualKey.V or VirtualKey.X){args.Handled=true;return;}
        int dr=args.Key==VirtualKey.Down?1:args.Key==VirtualKey.Up?-1:0,dc=args.Key==VirtualKey.Right?1:args.Key==VirtualKey.Left?-1:0;
        if(dr==0&&dc==0&&args.Key is not (VirtualKey.Home or VirtualKey.End))return;
        int row=HasSelection?_active.Row:0,col=HasSelection?_active.Column:0;
        if(args.Key==VirtualKey.Home){col=0;if(ctrl)row=0;}
        else if(args.Key==VirtualKey.End){col=_columns.Length-1;if(ctrl)row=_data.Count-1;}
        else if(HasSelection){row+=dr;col+=dc;}
        Select(row,col,Shift());RevealSelection();args.Handled=true;
    }
    private void RevealSelection()
    {
        if(!HasSelection)return;
        var item=_list.Items.OfType<ClosureGridRow>().FirstOrDefault(r=>ReferenceEquals(r.Closure,_data[_active.Row]));
        if(item is not null)_list.ScrollIntoView(item);
        double left=_columns.Take(_active.Column).Sum(c=>c.Width),right=_active.Column==_columns.Length-1?ContentWidth:left+_columns[_active.Column].Width;
        _viewport.Reveal(left,right);
    }
    private static string QuoteCell(string value)=>value.IndexOfAny(['\t','\r','\n','"'])<0?value:"\""+value.Replace("\"","\"\"",StringComparison.Ordinal)+"\"";
    public string SelectedText(bool headers=false)
    {
        if(!HasSelection)return "";var r=SelectionRange;var lines=new List<string>();
        if(headers)lines.Add(string.Join('\t',_columns.Skip(r.Left).Take(r.Right-r.Left+1).Select(c=>QuoteCell(c.Label))));
        for(int row=r.Top;row<=r.Bottom;row++)lines.Add(string.Join('\t',Values(_data[row]).Skip(r.Left).Take(r.Right-r.Left+1).Select(QuoteCell)));
        return string.Join("\r\n",lines);
    }
    public string SelectedHtml(bool headers=false)
    {
        if(!HasSelection)return "";
        var range=SelectionRange;
        var html=new StringBuilder("<table border=\"1\" cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse;font-family:Segoe UI,Arial,sans-serif;font-size:11pt;\">");
        void Row(IEnumerable<string> values,bool heading=false)
        {
            html.Append("<tr>");var tag=heading?"th":"td";
            foreach(var value in values)
            {
                html.Append('<').Append(tag);
                if(heading)html.Append(" scope=\"col\"");
                html.Append(" style=\"border:1px solid #808080;padding:6px 10px;text-align:left;vertical-align:top;white-space:pre-wrap;\">");
                html.Append(WebUtility.HtmlEncode(value.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n')).Replace("\n","<br />",StringComparison.Ordinal));
                html.Append("</").Append(tag).Append('>');
            }
            html.Append("</tr>");
        }
        if(headers)Row(_columns.Skip(range.Left).Take(range.Right-range.Left+1).Select(c=>c.Label),true);
        for(int row=range.Top;row<=range.Bottom;row++)Row(Values(_data[row]).Skip(range.Left).Take(range.Right-range.Left+1));
        return html.Append("</table>").ToString();
    }
    public bool CopySelection(bool headers=false)
    {
        if(!HasSelection)return false;
        try{var content=new DataPackage{RequestedOperation=DataPackageOperation.Copy};content.SetText(SelectedText(headers));content.SetHtmlFormat(HtmlFormatHelper.CreateHtmlFormat(SelectedHtml(headers)));Clipboard.SetContent(content);Clipboard.Flush();SelectionFeedback?.Invoke("Selección copiada · Tabla para Teams y Excel");return true;}
        catch(System.Runtime.InteropServices.COMException){SelectionFeedback?.Invoke("El portapapeles está ocupado. Vuelva a copiar la selección.");return false;}
    }
}
