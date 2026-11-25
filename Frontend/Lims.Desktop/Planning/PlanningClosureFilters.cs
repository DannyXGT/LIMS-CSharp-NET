using System.Globalization;
using Lims.Contracts.Planning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Lims.Desktop.Planning;

internal sealed partial class PlanningClosuresView
{
    private IReadOnlyList<ReportClosureFacet> _facets=[];
    private HashSet<string>? _selectedDates,_selectedTimes;
    private Button? _dateButton,_timeButton;
    private static ControlTemplate? _filterCheckTemplate;
    private static string? Selection(HashSet<string>? values)=>values is null?null:values.Count==0?"None":string.Join(',',values.Order(StringComparer.Ordinal));
    private void UpdateCalendarLabels()
    {
        if(_dateButton is {} date)date.Content=_selectedDates is null?"Fecha · Todas":$"Fecha · {_selectedDates.Count:N0} sel.";
        if(_timeButton is {} time)time.Content=_selectedTimes is null?"Hora · Todas":$"Hora · {_selectedTimes.Count:N0} sel.";
    }
    private static CheckBox FilterCheck(string label,bool? value)
    {
        _filterCheckTemplate??=(ControlTemplate)XamlReader.Load("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="CheckBox">
              <Grid x:Name="Root" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="Transparent" Height="26">
                <VisualStateManager.VisualStateGroups>
                  <VisualStateGroup x:Name="CommonStates">
                    <VisualState x:Name="Normal"/>
                    <VisualState x:Name="PointerOver"><VisualState.Setters><Setter Target="Root.Background" Value="#192833"/></VisualState.Setters></VisualState>
                    <VisualState x:Name="Pressed"><VisualState.Setters><Setter Target="Root.Background" Value="#20382D"/></VisualState.Setters></VisualState>
                    <VisualState x:Name="Disabled"><VisualState.Setters><Setter Target="Root.Opacity" Value="0.45"/></VisualState.Setters></VisualState>
                  </VisualStateGroup>
                  <VisualStateGroup x:Name="CheckStates">
                    <VisualState x:Name="Unchecked"/>
                    <VisualState x:Name="Checked"><VisualState.Setters><Setter Target="Box.Background" Value="#38D89B"/><Setter Target="Box.BorderBrush" Value="#38D89B"/><Setter Target="Mark.Opacity" Value="1"/></VisualState.Setters></VisualState>
                    <VisualState x:Name="Indeterminate"><VisualState.Setters><Setter Target="Box.Background" Value="#38D89B"/><Setter Target="Mark.Text" Value="−"/><Setter Target="Mark.Opacity" Value="1"/></VisualState.Setters></VisualState>
                  </VisualStateGroup>
                </VisualStateManager.VisualStateGroups>
                <Border x:Name="Box" Width="14" Height="14" HorizontalAlignment="Left" VerticalAlignment="Center" CornerRadius="3" BorderThickness="1" BorderBrush="#52626E" Background="#111A22"/>
                <TextBlock x:Name="Mark" Text="✓" Width="14" FontSize="12" FontWeight="SemiBold" Foreground="#092318" Opacity="0" TextAlignment="Center" HorizontalAlignment="Left" VerticalAlignment="Center"/>
                <ContentPresenter Margin="22,0,0,0" VerticalAlignment="Center" Content="{TemplateBinding Content}" Foreground="{TemplateBinding Foreground}" FontSize="12"/>
              </Grid>
            </ControlTemplate>
            """);
        var check=new CheckBox{Content=label,IsChecked=value,Template=_filterCheckTemplate,Height=26,MinHeight=26,MinWidth=0,HorizontalAlignment=HorizontalAlignment.Stretch,Padding=new(0),Foreground=PlanningSheetGrid.Color("#D7E2EB"),UseSystemFocusVisuals=true};
        AutomationProperties.SetName(check,label);return check;
    }
    private static Style ClosureFlyoutStyle()
    {
        var style=new Style(typeof(FlyoutPresenter));style.Setters.Add(new Setter(Control.BackgroundProperty,PlanningSheetGrid.Color("#111A22")));style.Setters.Add(new Setter(Control.BorderBrushProperty,PlanningSheetGrid.Color("#344451")));style.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(1)));style.Setters.Add(new Setter(Control.CornerRadiusProperty,new CornerRadius(6)));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10)));style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty,0d));return style;
    }
    private Button CalendarFilter(bool dates)
    {
        var button=Button(dates?"Fecha · Todas":"Hora · Todas",()=>Task.CompletedTask);
        if(dates)_dateButton=button;else _timeButton=button;AutomationProperties.SetName(button,dates?"Fechas de cierre":"Horas de cierre");
        var body=PlanningUi.Stack();body.Spacing=6;body.Width=dates?226:164;
        var popup=new Flyout{Content=body,FlyoutPresenterStyle=ClosureFlyoutStyle()};button.Flyout=popup;
        popup.Opening+=(_,_)=>{
            body.Children.Clear();var checks=new List<(string Value,CheckBox Check)>();var parents=new List<(CheckBox Header,IReadOnlyList<CheckBox> Leaves)>();var selected=dates?_selectedDates:_selectedTimes;
            var updating=false;
            void RefreshParents(){if(updating)return;updating=true;try{foreach(var (header,leaves) in parents)header.IsChecked=leaves.All(c=>c.IsChecked==true)?true:leaves.Any(c=>c.IsChecked==true)?null:false;}finally{updating=false;}}
            void SelectBranch(CheckBox header,IReadOnlyList<CheckBox> leaves){if(updating)return;updating=true;try{foreach(var leaf in leaves)leaf.IsChecked=header.IsChecked==true;}finally{updating=false;}RefreshParents();}
            Button Link(string text,bool mark)
            {
                var link=Button(text,()=>{foreach(var (_,check) in checks)check.IsChecked=mark;RefreshParents();return Task.CompletedTask;});link.Height=22;link.MinHeight=22;link.Padding=new(3,0,3,0);link.BorderThickness=new(0);link.Background=PlanningSheetGrid.Color("#111A22");link.Foreground=PlanningSheetGrid.Color("#7DCCAC");return link;
            }
            var heading=PlanningUi.Text(dates?"FECHA DE CIERRE":"HORA DE CIERRE",10,true);heading.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
            body.Children.Add(heading);var links=PlanningUi.Row(Link("Todas",true),Link("Ninguna",false));links.Spacing=10;body.Children.Add(links);
            body.Children.Add(new Border{Height=1,Background=PlanningSheetGrid.Color("#2A3945")});
            var choices=PlanningUi.Stack();choices.Spacing=0;
            FrameworkElement Branch(string label,StackPanel children,IReadOnlyList<CheckBox> leaves)
            {
                var header=FilterCheck(label,leaves.All(c=>c.IsChecked==true));parents.Add((header,leaves));header.Checked+=(_,_)=>SelectBranch(header,leaves);header.Unchecked+=(_,_)=>SelectBranch(header,leaves);
                var arrow=Button("⌄",()=>{children.Visibility=children.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;return Task.CompletedTask;});arrow.Width=18;arrow.Height=26;arrow.MinHeight=26;arrow.Padding=new(0);arrow.BorderThickness=new(0);arrow.Background=PlanningSheetGrid.Color("#111A22");AutomationProperties.SetName(arrow,"Expandir o contraer "+label);
                var row=new Grid{ColumnSpacing=2};row.ColumnDefinitions.Add(new(){Width=new(18)});row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});row.Children.Add(arrow);Grid.SetColumn(header,1);row.Children.Add(header);
                var branch=PlanningUi.Stack(row,children);branch.Spacing=0;children.Margin=new(18,0,0,0);return branch;
            }
            if(dates)
            {
                foreach(var year in _facets.Select(f=>f.Date).Distinct().OrderDescending().GroupBy(d=>d.Year))
                {
                    var months=PlanningUi.Stack();months.Spacing=0;var yearLeaves=new List<CheckBox>();
                    foreach(var month in year.GroupBy(d=>d.Month))
                    {
                        var days=PlanningUi.Stack();days.Spacing=0;var monthLeaves=new List<CheckBox>();
                        foreach(var day in month)
                        {
                            var value=day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);var check=FilterCheck(day.Day.ToString("00",CultureInfo.InvariantCulture),selected is null||selected.Contains(value));check.Margin=new(18,0,0,0);
                            check.Checked+=(_,_)=>RefreshParents();check.Unchecked+=(_,_)=>RefreshParents();checks.Add((value,check));monthLeaves.Add(check);yearLeaves.Add(check);days.Children.Add(check);
                        }
                        months.Children.Add(Branch(CultureInfo.GetCultureInfo("es-GT").DateTimeFormat.GetMonthName(month.Key),days,monthLeaves));
                    }
                    choices.Children.Add(Branch(year.Key.ToString(CultureInfo.InvariantCulture),months,yearLeaves));
                }
                RefreshParents();
            }
            else
            {
                foreach(var hour in _facets.Where(f=>_selectedDates is null||_selectedDates.Contains(f.Date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture))).Select(f=>f.Time).Distinct().OrderDescending(StringComparer.Ordinal))
                {var check=FilterCheck(hour,selected is null||selected.Contains(hour));checks.Add((hour,check));choices.Children.Add(check);}
            }
            if(checks.Count==0)choices.Children.Add(PlanningUi.Text("Sin horarios disponibles.",12,true));
            body.Children.Add(new ScrollViewer{Content=choices,MaxHeight=286,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
            body.Children.Add(new Border{Height=1,Background=PlanningSheetGrid.Color("#2A3945")});
            var apply=Button("Aplicar",async()=>{var selection=checks.Where(c=>c.Check.IsChecked==true).Select(c=>c.Value).ToHashSet(StringComparer.Ordinal);if(dates)_selectedDates=selection.Count==checks.Count?null:selection;else _selectedTimes=selection.Count==checks.Count?null:selection;UpdateCalendarLabels();popup.Hide();await Load();},true);apply.HorizontalAlignment=HorizontalAlignment.Right;apply.MinWidth=70;body.Children.Add(apply);
        };
        return button;
    }
}
