using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Planning;

internal static class ReportScannerDialog
{
    internal static ContentDialog Create(XamlRoot root,string title,string message,bool warning)
    {
        var dialog=new ContentDialog
        {
            XamlRoot=root,Style=(Style)Application.Current.Resources["LimsEditorDialogStyle"],
            Title=Heading(title,warning),Content=Body(message),CloseButtonText="Aceptar",
            CloseButtonStyle=(Style)Application.Current.Resources["LimsPrimaryButtonStyle"],
            DefaultButton=ContentDialogButton.Close,CornerRadius=new(12)
        };
        dialog.Resources["ContentDialogMaxWidth"]=560d;
        AutomationProperties.SetName(dialog,"Mensaje del scanner");
        return dialog;
    }
    internal static UIElement Heading(string title,bool warning)
    {
        var icon=new Border
        {
            Width=44,Height=44,CornerRadius=new(12),Background=PlanningUi.Brush("LimsSurfaceSecondaryBrush"),
            Child=new LimsIcon{Icon=warning?LimsIconKind.Alerts:LimsIconKind.Error,Size=23,Foreground=PlanningUi.Brush("LimsWarningBrush")}
        };
        var caption=PlanningUi.Text(title.Replace("LIMS · ","",StringComparison.Ordinal),20);
        caption.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;caption.VerticalAlignment=VerticalAlignment.Center;
        var heading=PlanningUi.Split(caption,icon);heading.ColumnDefinitions[0].Width=new(1,GridUnitType.Star);heading.ColumnSpacing=20;
        return heading;
    }
    internal static FrameworkElement Body(string message)
    {
        var label=PlanningUi.Text("REPORT LIST · LECTOR DE CÓDIGOS",11,true);label.CharacterSpacing=70;
        var detail=PlanningUi.Text(message,14);detail.LineHeight=22;
        var content=PlanningUi.Stack(label,detail);content.Spacing=14;content.Margin=new(0,0,0,8);content.MaxWidth=464;
        AutomationProperties.SetName(content,"Mensaje del scanner");
        return content;
    }
}
