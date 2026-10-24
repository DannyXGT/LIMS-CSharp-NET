using System.Numerics;
using System.Text.Json;
using Lims.Desktop.Presentation;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Lims.Desktop.VisualHarness;

public partial class App
{
    private async Task ValidateMotionRegressionAsync()
    {
        // Exercise the same reset used when Windows animations are disabled,
        // without changing the user's Windows accessibility settings.
        var fresh = new Border();
        PermissionsMotion.Reset(fresh);
        PermissionsMotion.Reset(fresh);
        var visual = ElementCompositionPreview.GetElementVisual(fresh);
        var status = visual.Properties.TryGetVector3("Translation", out var translation);
        if (status != Microsoft.UI.Composition.CompositionGetValueStatus.Succeeded ||
            translation != Vector3.Zero || visual.Opacity != 1)
            throw new InvalidOperationException("First-use reset did not restore the visual.");

        Motion.Enter(fresh, y: 7);
        PermissionsMotion.Reset(fresh);
        if (visual.Opacity != 1)
            throw new InvalidOperationException("Reset did not cancel the opacity animation.");

        // Exit must also work on an element that never received an entrance animation.
        await Motion.ExitAsync(new Border());
        File.WriteAllText(Path.Combine(_directory, "motion-regression.json"), JsonSerializer.Serialize(new
        {
            fixtureOnly = true,
            firstUseWithoutAnimations = "passed",
            repeatedReset = "passed",
            resetAfterEntrance = "passed",
            exitWithoutEntrance = "passed"
        }, ReportOptions));
    }
}
