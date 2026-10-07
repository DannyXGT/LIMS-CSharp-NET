using Lims.Contracts.ReferenceMaterials;
using Lims.DesignSystem.Controls;
using Lims.DesignSystem.Presentation;
using Lims.Desktop.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Lims.Desktop.VisualHarness;

internal static class PopupInteractionChecks
{
    public static async Task<Dictionary<string, bool>> RunAsync(ReferenceMaterialEditorDialog editor, XamlRoot root)
    {
        var results = new Dictionary<string, bool>();
        var method = (LimsSearchSelect)editor.FindName("MethodBox");
        var unit = (LimsSelect)editor.FindName("UnitCombo");
        method.SelectedValue = 12;
        results["selectionById"] = method.SelectedItem is ReferenceMethodOption { Id: 12 };
        method.OpenPopup();
        await Task.Delay(200);
        var search = FocusManager.GetFocusedElement(root) as TextBox;
        results["searchFocus"] = search is not null;
        if (search is not null)
        {
            search.Text = "23";
            await Task.Delay(100);
            var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Single(p => Find<LimsPopupSurface>(p.Child) is not null);
            var list = Find<ListView>(popup.Child)!;
            results["localFilter"] = list.Items.Count == 1 && list.Items[0] is ReferenceMethodOption { Id: 23 };
            results["filterPreservesSelection"] = method.SelectedValue is 12;
            search.Text = "no-such-fixture";
            await Task.Delay(100);
            results["emptyFilter"] = list.Items.Count == 0;
        }
        for (var index = 0; index < 12; index++) { method.CloseImmediately(); method.OpenPopup(); }
        await Task.Delay(200);
        results["reopenTwelveTimes"] = method.IsPopupOpen;
        method.ClosePopup(true);
        method.OpenPopup();
        await Task.Delay(200);
        results["openingReplacesClosing"] = method.IsPopupOpen;
        unit.OpenPopup();
        await Task.Delay(200);
        results["oneActivePopup"] = unit.IsPopupOpen && !method.IsPopupOpen;
        ((TextBox)editor.FindName("NameBox")).Focus(FocusState.Keyboard);
        await Task.Delay(250);
        results["focusOutsideCloses"] = !unit.IsPopupOpen;
        method.OpenPopup();
        await Task.Delay(200);
        LimsPopupField.CloseForRoot(root);
        results["ownerCloseRemovesPopup"] = !method.IsPopupOpen &&
            !VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(p => Find<LimsPopupSurface>(p.Child) is not null);
        results["reducedMotionPolicy"] = InteractionMotion.PressedScale(false) == 1 && InteractionMotion.PopupDuration(true, false) == 60;
        results["systemAnimationsEnabled"] = Motion.Enabled;
        return results;
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T result) return result;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Find<T>(VisualTreeHelper.GetChild(root, i));
            if (child is not null) return child;
        }
        return null;
    }
}
