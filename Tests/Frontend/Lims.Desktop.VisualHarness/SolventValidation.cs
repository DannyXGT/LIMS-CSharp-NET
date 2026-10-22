using System.Text.Json;
using Lims.Desktop.Views;
using Lims.Desktop.ViewModels;
using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Lims.Contracts.ReferencePreparations;

namespace Lims.Desktop.VisualHarness;

public partial class App
{
    private CommercialPreparationDetailDialog? _solventReloadedDialog;
    private async Task HandleSolventCommandAsync(string action, JsonElement data)
    {
        if (!PersistentPreparationValidation.Enabled) throw new InvalidOperationException("This validation requires the persistent isolated database.");
        var kind = data.TryGetProperty("kind", out var k) ? k.GetString()! : "Stock";
        if (action == "solventCloseDetail")
        {
            _stockPage!.CommercialDetail?.Hide(); _intermediatePage!.CommercialDetail?.Hide(); _solventReloadedDialog?.Hide(); _solventReloadedDialog = null;
            await Task.Delay(350); return;
        }
        if (action is "solventReload" or "solventSaved")
        {
            var expected = data.TryGetProperty("expected", out var expectedValue) ? expectedValue.GetInt32() : 1;
            for (var i = 0; i < 100 && _commercialApi!.Roles.Count(r => r.Kind == kind) < expected; i++) await Task.Delay(50);
            var role = _commercialApi!.Roles.Last(r => r.Kind == kind);
            var reloaded = (await PersistentPreparationValidation.GetAsync(kind, role.Material.Id, default)).Value!;
            File.WriteAllText(Path.Combine(_directory, data.GetProperty("name").GetString()! + ".json"), JsonSerializer.Serialize(new { persistentIsolatedDatabase = true, reloaded }, ReportOptions));
            if (action == "solventReload")
            {
                _stockPage!.CommercialDetail?.Hide(); _intermediatePage!.CommercialDetail?.Hide(); await Task.Delay(400);
                _solventReloadedDialog = new CommercialPreparationDetailDialog(reloaded) { XamlRoot = _shell!.XamlRoot, RequestedTheme = ElementTheme.Dark };
                _ = _solventReloadedDialog.ShowAsync();
            }
            return;
        }
        var root = kind == "Intermedia" ? (FrameworkElement)_intermediatePage!.CommercialWorkspace! : kind == "StockPrepared" ? _stockPage!.Editor! : (CommercialPreparationEditor)_stockPage!.Editor!.FindName("CommercialEntry");
        var editor = (SolventCompositionEditor)root.FindName("CompositionEditor");
        var draft = kind == "Intermedia" ? _intermediatePage!.CommercialWorkspace!.ViewModel.Composition : kind == "StockPrepared" ? _stockPage!.ViewModel.Composition : ((CommercialPreparationEditor)root).Composition;
        switch (action)
        {
            case "solventReady":
                for (var i = 0; i < 100 && !draft.IsReady; i++) await Task.Delay(50);
                if (!draft.IsReady) throw new InvalidOperationException(draft.CatalogMessage);
                break;
            case "solventSet":
                foreach (var row in draft.Rows.ToArray()) draft.Remove(row);
                foreach (var row in data.GetProperty("rows").EnumerateArray())
                {
                    draft.Add();
                    var grid = (Grid)editor.RowsPanel.Children[^1];
                    ((LimsSearchSelect)grid.Children[0]).SelectedItem = draft.Catalog.Single(s => s.Abbreviation == row.GetProperty("abbreviation").GetString());
                    ((TextBox)grid.Children[1]).Text = row.GetProperty("percent").GetString()!;
                }
                await Task.Delay(100); root.UpdateLayout();
                var scroll = (ScrollViewer)(kind == "Intermedia" ? root.FindName("CaptureScroll") : kind == "StockPrepared" ? root.FindName("StepScroll") : root.FindName("FieldsScroll"));
                var targetY = editor.TransformToVisual((UIElement)scroll.Content).TransformPoint(new Windows.Foundation.Point()).Y;
                scroll.ChangeView(null, targetY, null, true); await Task.Delay(200);
                break;
            case "solventPopup":
                if (draft.Rows.Count == 0) draft.Add();
                editor.StartBringIntoView(); await Task.Delay(250);
                var select = (LimsSearchSelect)((Grid)editor.RowsPanel.Children[0]).Children[0];
                select.StartBringIntoView(); await Task.Delay(200); select.OpenPopup();
                break;
            case "solventReport":
                var catalog = (await PersistentPreparationValidation.CatalogAsync(null, default)).Value!;
                var popupOptions = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot)
                    .SelectMany(p => Descendants(p.Child).OfType<ListView>()).SelectMany(l => l.Items.OfType<SolventOption>()).ToArray();
                File.WriteAllText(Path.Combine(_directory, data.GetProperty("name").GetString()! + ".json"), JsonSerializer.Serialize(new {
                    persistentIsolatedDatabase = true, kind, catalog, uiCatalog = draft.Catalog.ToArray(), popupOptions,
                    byName = (await PersistentPreparationValidation.CatalogAsync("Tetrahidrofurano", default)).Value,
                    byAbbreviation = (await PersistentPreparationValidation.CatalogAsync("THF", default)).Value,
                    draft.IsReady, draft.Error, draft.IsValid, draft.Inputs, draft.Summary,
                    saveEnabled = ((Button)(kind == "Intermedia" ? root.FindName("SaveButton") : _stockPage!.Editor!.FindName("SaveButton"))).IsEnabled,
                    nominalPanelRemoved = root.FindName("NominalPanel") is null && root.FindName("ConcentrationBasisSelect") is null,
                    responsibleIsReadOnly = kind == "StockPrepared" || root.FindName("ResponsibleText") is TextBlock,
                    size = root.XamlRoot.Size
                }, ReportOptions));
                break;
        }
    }
}
