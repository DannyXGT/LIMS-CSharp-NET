using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Lims.Desktop.Planning;

// Presentation shared only by Consulta and its dossier. All states come from the LIMS styles.
internal static class ConsultationVisuals
{
    public static void FitWindow(Window window,int width,int height)
    {
        var area=DisplayArea.GetFromWindowId(window.AppWindow.Id,DisplayAreaFallback.Primary).WorkArea;
        window.AppWindow.Resize(new SizeInt32(Math.Min(width,area.Width-40),Math.Min(height,area.Height-40)));
    }
    public static SolidColorBrush Navy => PlanningSheetGrid.Color("#0D1924");
    public static SolidColorBrush Edge => PlanningSheetGrid.Color("#2A465B");
    public static LinearGradientBrush Panel(bool hero=false)
    {
        var brush=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(1,1)};
        brush.GradientStops.Add(new(){Color=PlanningSheetGrid.Color(hero?"#162D41":"#152735").Color,Offset=0});
        brush.GradientStops.Add(new(){Color=PlanningSheetGrid.Color("#0D1923").Color,Offset=1});
        return brush;
    }
    public static Border Surface(UIElement content,Thickness? padding=null)=>new(){Child=content,Background=Panel(),BorderBrush=Edge,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=padding??new Thickness(16)};
    public static Button Action(string text, LimsIconKind icon, Func<Task> action, bool primary = false, bool quiet = false)
    {
        var button = PlanningUi.Button(text, action, primary);
        button.CornerRadius=new CornerRadius(8);
        button.Background=primary?Primary():Panel();
        button.BorderBrush=PlanningSheetGrid.Color(primary?"#2196FF":"#2D4A60");
        if (quiet)
        {
            button.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            button.BorderThickness = new Thickness(0);
        }
        button.FontSize = 13;
        button.MinHeight = 36;
        button.Padding = new Thickness(12, 7, 12, 7);
        button.FocusVisualPrimaryBrush=PlanningSheetGrid.Color("#69C6FF");
        button.FocusVisualSecondaryBrush=Navy;
        button.Content = PlanningUi.Row(new LimsIcon { Icon = icon, Size = 16, VerticalAlignment = VerticalAlignment.Center }, PlanningUi.Text(text, 13));
        // Inherit foreground so primary, hover and disabled states apply to both icon and caption.
        foreach (var child in ((StackPanel)button.Content).Children)
            if (child is Control control) control.ClearValue(Control.ForegroundProperty);
            else if (child is TextBlock caption) caption.ClearValue(TextBlock.ForegroundProperty);
        ((StackPanel)button.Content).Spacing = 7;
        AutomationProperties.SetName(button, text);
        ToolTipService.SetToolTip(button, text);
        return button;
    }
    public static void Symbol(Button button,Microsoft.UI.Xaml.Controls.Symbol symbol)
    {
        var row=(StackPanel)button.Content;row.Children.RemoveAt(0);
        row.Children.Insert(0,new FontIcon{FontFamily=new FontFamily("Segoe Fluent Icons"),Glyph=char.ConvertFromUtf32((int)symbol),FontSize=16,VerticalAlignment=VerticalAlignment.Center});
    }

    private static LinearGradientBrush Primary()
    {
        var brush=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(0,1)};
        brush.GradientStops.Add(new(){Color=PlanningSheetGrid.Color("#168BFF").Color,Offset=0});
        brush.GradientStops.Add(new(){Color=PlanningSheetGrid.Color("#0872DF").Color,Offset=1});return brush;
    }

    public static StackPanel Metric(TextBlock value, string label, string tone = "LimsTextPrimaryBrush")
    {
        value.FontSize = 20;
        value.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        value.Foreground = PlanningUi.Brush(tone);
        var metric = PlanningUi.Stack(value, PlanningUi.Text(label, 12, true));
        metric.Spacing = 1;
        metric.MinWidth = 85;
        return metric;
    }

    public static Grid Split(UIElement left, UIElement right)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.Children.Add(left);
        Grid.SetColumn((FrameworkElement)right, 1);
        grid.Children.Add(right);
        return grid;
    }

    public static Border Section(string title, UIElement content)
    {
        var label = PlanningUi.Text(title, 13, true);
        label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        return Surface(PlanningUi.Stack(label, content));
    }
}
