using System.Text.Json;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.ViewModels;
using Lims.Desktop.Views;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lims.Desktop.VisualHarness;

public partial class App
{
    private async Task HandleActionsCommandAsync(string action, JsonElement data)
    {
        if (action == "actionsCompatibility") { _commercialApi!.SupportsSolventComposition = data.GetProperty("supported").GetBoolean(); return; }
        var kind = data.TryGetProperty("kind", out var k) ? k.GetString()! : "Stock";
        FrameworkElement host = kind switch
        {
            "Intermedia" => _intermediatePage!.CommercialWorkspace!,
            "IntermediaPrepared" => _intermediatePage!.Editor!,
            "Curva" or "AQS" => GroupPage(data).Editor!,
            _ => _stockPage!.Editor!
        };
        var commercial = kind == "Stock" ? (CommercialPreparationEditor)host.FindName("CommercialEntry") : null;
        var form = (FrameworkElement?)commercial ?? host;
        var root = (FrameworkElement?)host.FindName("EditorRoot") ?? host;
        var composition = kind == "Intermedia" ? _intermediatePage!.CommercialWorkspace!.ViewModel.Composition :
            kind == "Stock" ? commercial!.Composition : kind == "StockPrepared" ? _stockPage!.ViewModel.Composition : null;
        var editor = form.FindName("CompositionEditor") as SolventCompositionEditor;
        Control FindControl(string name) => form.FindName(name) as Control ?? host.FindName(name) as Control ??
            Descendants(host).OfType<Control>().First(c => AutomationProperties.GetName(c) == name || c is Button b && b.Content as string == name);
        switch (action)
        {
            case "actionsCatalog":
                composition!.SetCatalog(JsonSerializer.Deserialize<SolventOption[]>(data.GetProperty("catalog").GetRawText())!);
                break;
            case "actionsComposition":
                foreach (var row in composition!.Rows.ToArray()) composition.Remove(row);
                foreach (var item in data.GetProperty("rows").EnumerateArray())
                {
                    var row = composition.Add();
                    row.SelectedSolvent = composition.Catalog.Single(s => s.Id == item.GetProperty("id").GetInt32());
                    row.Proportion = item.GetProperty("percent").GetString()!;
                }
                break;
            case "actionsFocus": FindControl(data.GetProperty("field").GetString()!).Focus(FocusState.Keyboard); break;
            case "actionsSection":
                var section = (FrameworkElement)form.FindName(data.GetProperty("field").GetString()!);
                var scroll = (ScrollViewer)form.FindName("FieldsScroll");
                form.UpdateLayout();
                scroll.ChangeView(null, section.TransformToVisual((UIElement)scroll.Content).TransformPoint(new()).Y, null, true);
                break;
            case "actionsReport":
                host.UpdateLayout();
                object Bounds(FrameworkElement element)
                {
                    var p = element.TransformToVisual(null).TransformPoint(new());
                    return new { x = p.X, y = p.Y, width = element.ActualWidth, height = element.ActualHeight };
                }
                var save = (Button)host.FindName("SaveButton");
                var cancel = (Button)host.FindName("CancelButton");
                File.WriteAllText(Path.Combine(_directory, data.GetProperty("name").GetString()! + ".json"), JsonSerializer.Serialize(new
                {
                    fixtureOnly = true, databaseModified = false, kind, host.XamlRoot.Size, host.XamlRoot.RasterizationScale,
                    root = Bounds(root), save = Bounds(save), cancel = Bounds(cancel), saveEnabled = save.IsEnabled,
                    status = (host.FindName("FooterStatus") as TextBlock)?.Text ?? (host.FindName("SuccessText") as TextBlock)?.Text,
                    inputs = Descendants(host).OfType<TextBox>().Select(t => new { t.Name, t.Text }).ToArray(),
                    selections = Descendants(host).OfType<LimsSelect>().Select(t => new { t.Name, selected = t.SelectedItem?.ToString() }).ToArray(),
                    composition = composition?.Inputs,
                    compositionError = composition?.Error,
                    visibleCompositionError = editor is null ? null : Descendants(editor).OfType<TextBlock>().Where(t => t.Visibility == Visibility.Visible).Select(t => t.Text).ToArray(),
                    concentration = (form.FindName("ResultConcentrations") as TextBlock)?.Text,
                    volume = (form.FindName("BalanceText") as TextBlock)?.Text,
                    available = (form.FindName("BalanceBar") as ProgressBar)?.Value,
                    buttons = Descendants(host).OfType<Button>().Where(b => b.Visibility == Visibility.Visible && b.ActualWidth > 0 && (LimsAction.GetIcon(b) != LimsIconKind.None || b is LimsPopupField)).Select(b => new
                    {
                        b.Name, label = AutomationProperties.GetName(b), content = b.Content as string,
                        icon = LimsAction.GetIcon(b).ToString(), b.IsEnabled, focus = b.FocusState.ToString(),
                        bounds = Bounds(b), tooltip = ToolTipService.GetToolTip(b)?.ToString(),
                        state = VisualTreeHelper.GetChildrenCount(b) == 0 ? null : VisualStateManager.GetVisualStateGroups((FrameworkElement)VisualTreeHelper.GetChild(b, 0)).Select(g => g.CurrentState?.Name).ToArray(),
                        renderedIcons = Descendants(b).OfType<LimsIcon>().Select(i => i.Icon.ToString()).ToArray()
                    }).ToArray()
                }, ReportOptions));
                break;
        }
        await Task.Delay(100);
    }
}
