using System.Text.Json;
using Lims.Desktop.Views;
using Lims.Desktop.ViewModels;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace Lims.Desktop.VisualHarness;

public partial class App
{
    private static readonly string[] MixFieldNames = new[] { "ProductNameBox", "MethodSelect", "BrandBox", "CatalogBox", "LotBox", "VolumeBox", "BottleCountBox", "VolumeUnitSelect", "ReceivedPicker", "ExpirationPicker", "LocationSelect", "StorageBox", "SaveButton", "CancelButton", "AddRowButton", "PasteButton", "ComponentSearchBox" };
    private async Task HandleMixCommandAsync(string action, JsonElement data)
    {
        if (action == "mixOpen")
        {
            ((NavigationView)_shell!.FindName("MainNavigation")).SelectedItem = _shell.FindName("PreparationsItem");
            await ((PreparationsPage)((ContentControl)_shell.FindName("PreparationsHost")).Content).ShowIntermediateAsync();
            await Task.Delay(200);
            await _intermediatePage!.OpenCommercialWorkspaceAsync(); return;
        }
        if (action == "mixListReport")
        {
            File.WriteAllText(Path.Combine(_directory, data.GetProperty("name").GetString()! + ".json"), JsonSerializer.Serialize(new {
                fixtureOnly = true, _intermediatePage!.IsCommercialWorkspaceOpen, _intermediatePage.ViewModel.Page,
                selected = _intermediatePage.ViewModel.SelectedPreparation?.Id,
                search = ((TextBox)_intermediatePage.FindName("SearchBox")).Text, _commercialApi!.Roles,
                headerVisible = ((NavigationView)_shell!.FindName("MainNavigation")).AlwaysShowHeader
            }, ReportOptions)); return;
        }
        var workspace = _intermediatePage!.CommercialWorkspace ?? throw new InvalidOperationException("Commercial page is not open.");
        var vm = workspace.ViewModel;
        T Field<T>(string name) where T : class => (T)workspace.FindName(name);
        switch (action)
        {
            case "mixFill":
                await Task.Delay(500);
                workspace.UpdateLayout();
                Field<TextBox>("ProductNameBox").Text = "Mix · PRUEBA VISUAL";
                Field<TextBox>("BrandBox").Text = "Fabricante de prueba";
                Field<TextBox>("CatalogBox").Text = "CAT-VISUAL";
                Field<TextBox>("LotBox").Text = "LOTE-VISUAL";
                Field<TextBox>("VolumeBox").Text = "10";
                Field<TextBox>("BottleCountBox").Text = "1";
                Field<TextBox>("StorageBox").Text = "Ambiente";
                Field<LimsSearchSelect>("MethodSelect").SelectedIndex = 0;
                Field<LimsSelect>("LocationSelect").SelectedIndex = 0;
                vm.Composition.Rows[0].SelectedSolvent = vm.Composition.Catalog[0]; vm.Composition.Rows[0].Proportion = "100";
                Field<LimsDatePicker>("ExpirationPicker").Date = vm.ReceivedDate!.Value.AddYears(1);
                break;
            case "mixPaste":
                var original = Clipboard.GetContent();
                // Snapshot every retrievable format and restore it after the real clipboard paste.
                var previous = new DataPackage();
                foreach (var format in original.AvailableFormats)
                    previous.SetData(format, await original.GetDataAsync(format));
                try
                {
                    var clipboard = new DataPackage(); clipboard.SetText(data.GetProperty("text").GetString()!); Clipboard.SetContent(clipboard);
                    await workspace.PasteClipboardAsync();
                }
                finally { Clipboard.SetContent(previous); }
                break;
            case "mixSearch": Field<TextBox>("ComponentSearchBox").Text = data.GetProperty("text").GetString()!; break;
            case "mixSection":
                var inventory = data.GetProperty("section").GetString() == "inventory";
                Field<Expander>("ProductExpander").IsExpanded = data.GetProperty("section").GetString() == "product";
                Field<Expander>("InventoryExpander").IsExpanded = inventory;
                await Task.Delay(250);
                workspace.UpdateLayout();
                Field<ScrollViewer>("CaptureScroll").ChangeView(null, inventory ? 9999 : 0, null, true);
                break;
            case "mixScroll": Field<ScrollViewer>(data.TryGetProperty("field", out var field) ? field.GetString()! : "ComponentsScroll").ChangeView(null, data.GetProperty("offset").GetDouble(), null, true); break;
            case "mixField":
                var control = Field<Control>(data.GetProperty("field").GetString()!);
                control.StartBringIntoView(); control.Focus(FocusState.Keyboard); break;
            case "mixPopup": Field<LimsPopupField>(data.GetProperty("field").GetString()!).OpenPopup(); break;
            case "mixFocus":
                var rowIndex = data.GetProperty("row").GetInt32();
                var grid = Descendants(workspace).OfType<Grid>().Single(g => ReferenceEquals(g.Tag, vm.Rows[rowIndex]));
                var focus = (Control)grid.FindName(data.GetProperty("field").GetString()!); focus.StartBringIntoView(); focus.Focus(FocusState.Keyboard); break;
            case "mixReport":
                var root = workspace.XamlRoot;
                var viewport = Field<ScrollViewer>("ComponentsScroll");
                var capture = Field<ScrollViewer>("CaptureScroll");
                var rows = Descendants(workspace).OfType<Grid>().Where(g => g.Tag is CommercialComponentRow).ToArray();
                var focused = FocusManager.GetFocusedElement(root) as FrameworkElement;
                File.WriteAllText(Path.Combine(_directory, data.GetProperty("name").GetString()! + ".json"), JsonSerializer.Serialize(new {
                    fixtureOnly = true, root.Size, root.RasterizationScale, windowSize = new { _window!.AppWindow.Size.Width, _window.AppWindow.Size.Height },
                    vm.ValidCount, totalRows = vm.Rows.Count, vm.CanSave, vm.InitialVolume, vm.AvailableVolume, vm.AvailabilityPercent,
                    vm.ValidationStatus, vm.ImportMessage, vm.IsReady, vm.Name, vm.Method, vm.Location, vm.VolumeUnit, vm.ExpirationDate, vm.Storage,
                    footer = MixBounds(Field<FrameworkElement>("ActionBar")), summary = MixBounds(Field<FrameworkElement>("SummaryPanel")),
                    header = MixBounds(Field<FrameworkElement>("TableHeader")), viewport = MixBounds(viewport),
                    viewport.VerticalOffset, viewport.ScrollableHeight, captureOffset = capture.VerticalOffset,
                    rows = rows.Select(g => new { ((CommercialComponentRow)g.Tag).Number, ((CommercialComponentRow)g.Tag).Name, visible = MixIntersects(g, viewport) && MixIntersects(g, capture), bounds = MixBounds(g),
                        fields = g.Children.OfType<Control>().Select(c => new { c.Name, label = AutomationProperties.GetName(c), c.IsTabStop, c.IsEnabled, bounds = MixBounds(c) }) }),
                    focused = focused is null ? null : new { focused.Name, label = AutomationProperties.GetName(focused), bounds = MixBounds(focused), intersectsCapture = MixIntersects(focused, capture) },
                    fieldNames = MixFieldNames.Select(name => new { name, label = AutomationProperties.GetName(Field<FrameworkElement>(name)) }),
                    request = vm.CanSave ? vm.ReadRequest() : null
                }, ReportOptions)); break;
        }
    }
    private static IEnumerable<FrameworkElement> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element) yield return element;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static object MixBounds(FrameworkElement element)
    {
        var p = element.TransformToVisual(null).TransformPoint(new Point());
        return new { x = p.X, y = p.Y, width = element.ActualWidth, height = element.ActualHeight };
    }
    private static bool MixIntersects(FrameworkElement element, FrameworkElement viewport)
    {
        var p = element.TransformToVisual(viewport).TransformPoint(new Point());
        return element.Visibility == Visibility.Visible && p.Y >= -0.5 && p.Y + element.ActualHeight <= viewport.ActualHeight + 0.5;
    }
}
