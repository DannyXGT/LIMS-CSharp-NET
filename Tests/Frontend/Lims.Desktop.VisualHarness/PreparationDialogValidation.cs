using System.Text.Json;
using Lims.Desktop.Views;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.VisualHarness;

public partial class App
{
    private void HandleFormCommand(string action, JsonElement data)
    {
        var dialog = _stockPage!.Editor!;
        var commercial = (CommercialPreparationEditor)dialog.FindName("CommercialEntry");
        var isCommercial = commercial.Visibility == Visibility.Visible;
        object Bounds(FrameworkElement element)
        {
            var p = element.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point());
            return new { x = p.X, y = p.Y, width = element.ActualWidth, height = element.ActualHeight, visible = element.Visibility == Visibility.Visible };
        }
        FrameworkElement Field(string name) => (FrameworkElement)(isCommercial ? commercial.FindName(name) : dialog.FindName(name));
        switch (action)
        {
            case "formScroll":
                ((ScrollViewer)(isCommercial ? commercial.FindName("FieldsScroll") : dialog.FindName("StepScroll"))).ChangeView(null, data.GetProperty("offset").GetDouble(), null, true);
                break;
            case "formField":
                var field = Field(data.GetProperty("field").GetString()!);
                field.StartBringIntoView(); ((Control)field).Focus(FocusState.Keyboard);
                break;
            case "formPopup": ((LimsPopupField)Field(data.GetProperty("field").GetString()!)).OpenPopup(); break;
            case "formReport":
                var scroll = (ScrollViewer)(isCommercial ? commercial.FindName("FieldsScroll") : dialog.FindName("StepScroll"));
                File.WriteAllText(Path.Combine(_directory, data.GetProperty("name").GetString()! + ".json"), JsonSerializer.Serialize(new {
                    fixtureOnly = true, isCommercial, dialog.XamlRoot.Size, dialog.XamlRoot.RasterizationScale,
                    footer = Bounds((FrameworkElement)dialog.FindName("ActionBar")),
                    result = Bounds((FrameworkElement)(isCommercial ? commercial.FindName("ResultSurface") : dialog.FindName("ResultPanel"))),
                    viewport = Bounds(scroll), scroll.VerticalOffset, scroll.ScrollableHeight,
                    saveEnabled = ((Button)dialog.FindName("SaveButton")).IsEnabled,
                    _stockPage.ViewModel.CanSave, _stockPage.ViewModel.Compound, _stockPage.ViewModel.Calculation,
                    _stockPage.ViewModel.Message, commercialError = ((TextBlock)commercial.FindName("ErrorText")).Text,
                    weight = ((TextBlock)dialog.FindName("CalculatedWeightText")).Text,
                    balance = ((TextBlock)commercial.FindName("BalanceText")).Text,
                    concentration = ((TextBlock)commercial.FindName("ResultConcentrations")).Text
                }, ReportOptions));
                break;
        }
    }
}
