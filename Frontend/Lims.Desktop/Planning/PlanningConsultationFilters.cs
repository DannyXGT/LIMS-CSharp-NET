using System.Globalization;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Planning;

public sealed partial class PlanningJobsView
{
    private readonly Dictionary<string,string> _columnFilters=new(StringComparer.Ordinal);
    private readonly ComboBox _scope=new(){Header="Alcance de datos",ItemsSource=new[]{"Período operativo (20 días)","Histórico completo"},SelectedIndex=0,Width=200};
    private readonly ComboBox _holdFilter=new(){Header="On Hold",ItemsSource=new[]{"Todos","Solo On Hold","Excluir On Hold"},SelectedIndex=0,Width=200};
    private readonly ComboBox _tagFilter=new(){Header="Etiquetas",ItemsSource=new[]{"Todos","Solo JOBs etiquetados","Sin etiqueta"},SelectedIndex=0,Width=200};
    private readonly TextBlock _coverage=PlanningUi.Text("Comprobando cobertura de datos…",12,true),_columnSummary=PlanningUi.Text("Sin filtros de cabecera.",12,true);
    private void AddOperationalFilters(StackPanel criteria)
    {
        AutomationProperties.SetName(_scope,"Alcance de datos");AutomationProperties.SetName(_holdFilter,"Filtrar On Hold");AutomationProperties.SetName(_tagFilter,"Filtrar etiquetas");
        foreach(var combo in new[]{_scope,_holdFilter,_tagFilter}){combo.Style=(Style)Application.Current.Resources["LimsCompactComboBoxStyle"];combo.SelectionChanged+=(_,_)=>UpdateFilters();}
        _coverage.MaxWidth=_columnSummary.MaxWidth=412;
        criteria.Children.Insert(0,PlanningUi.Row(_scope,_holdFilter));criteria.Children.Insert(1,_tagFilter);
        criteria.Children.Add(_columnSummary);criteria.Children.Add(new Expander{Header="Cobertura de los datos",Content=_coverage});
    }
    private static StackPanel DateFilter(TextBox field,string label)
    {
        field.Header=null;field.Width=double.NaN;field.MinWidth=0;
        var input=new Grid{Width=200,ColumnSpacing=4};input.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});input.ColumnDefinitions.Add(new(){Width=GridLength.Auto});input.Children.Add(field);
        var calendar=new CalendarView{SelectionMode=CalendarViewSelectionMode.Single,MinWidth=280,MaxWidth=320};
        var button=FooterButton("Calendario · "+label,new SymbolIcon(Symbol.Calendar),()=>Task.CompletedTask);button.Height=36;
        var flyout=new Flyout{Content=calendar};button.Flyout=flyout;
        calendar.SelectedDatesChanged+=(_,args)=>{if(args.AddedDates.Count==0)return;field.Text=args.AddedDates[0].ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);flyout.Hide();};
        flyout.Opened+=(_,_)=>{if(DateTimeOffset.TryParseExact(field.Text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day))calendar.SetDisplayDate(day);};
        Grid.SetColumn(button,1);input.Children.Add(button);var panel=PlanningUi.Stack(PlanningUi.Text(label,13),input);panel.Spacing=5;return panel;
    }
    private void ShowColumnFilter(string key,FrameworkElement anchor)
    {
        var column=PlanningSheetModel.AvailableColumns.Single(c=>c.Key==key);_columnFilters.TryGetValue(key,out var current);
        var flyout=new Flyout();FrameworkElement input;Func<string?> read;
        if(key is "InDate" or "DueDate" or "TestedOn")
        {
            var date=new CalendarDatePicker{PlaceholderText="Seleccione fecha exacta",Width=280};AutomationProperties.SetName(date,"Fecha para filtrar "+column.Title);
            if(DateTimeOffset.TryParseExact(current,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var parsed))date.Date=parsed;
            input=date;read=()=>date.Date?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        }
        else if(key is "OnHold" or "NoProgram" or "DigitizationState")
        {
            var values=key=="DigitizationState"?new[]{"","Digitado","Sin información","Información incompleta"}:new[]{"","true","false"};
            var names=key=="DigitizationState"?new[]{"Todos","Digitada (hora y fecha)","Sin información","Información incompleta"}:key=="OnHold"?new[]{"Todos","On Hold","Activo"}:new[]{"Todos","NO PROGRAMAR","Sin NO PROGRAMAR"};
            var combo=new ComboBox{ItemsSource=names,SelectedIndex=Math.Max(0,Array.IndexOf(values,current??"")),Width=280};AutomationProperties.SetName(combo,"Valor para filtrar "+column.Title);input=combo;read=()=>values[combo.SelectedIndex];
        }
        else
        {
            var text=PlanningUi.Field("Valor para filtrar "+column.Title,current,max:2000);text.Header=null;text.PlaceholderText=key=="Usd"?"USD exacto, con punto decimal":"Contiene…";text.Width=280;AutomationProperties.SetName(text,"Valor para filtrar "+column.Title);input=text;read=()=>text.Text;
        }
        async Task Apply(bool clear)
        {
            var value=clear?null:read();if(string.IsNullOrWhiteSpace(value))_columnFilters.Remove(key);else _columnFilters[key]=value.Trim();
            flyout.Hide();_page=1;UpdateFilters();await Load();
        }
        flyout.Content=PlanningUi.Stack(PlanningUi.Text("Filtrar · "+column.Title,17),PlanningUi.Text("Se aplica a todos los registros del alcance seleccionado.",12,true),input,
            PlanningUi.Row(ConsultationVisuals.Action("Aplicar columna",LimsIconKind.ApplyFilters,()=>Apply(false),true),ConsultationVisuals.Action("Limpiar columna",LimsIconKind.Restore,()=>Apply(true))));
        flyout.ShowAt(anchor);
    }
}
