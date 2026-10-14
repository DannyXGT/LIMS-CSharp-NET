using System.Numerics;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Lims.Desktop.Presentation;

internal static class PermissionsMotion
{
    public static void Enter(FrameworkElement element, bool slide = false)
    {
        if (Motion.Enabled) Motion.Enter(element, y: slide ? 7 : 0, milliseconds: 180);
        else
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.StopAnimation("Opacity"); visual.StopAnimation("Translation");
            visual.Opacity = 1; visual.Properties.InsertVector3("Translation", Vector3.Zero);
        }
    }

    public static void Opacity(FrameworkElement element, float target, int milliseconds)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        if (!Motion.Enabled) { visual.StopAnimation("Opacity"); visual.Opacity = target; return; }
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1, target, visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0), new Vector2(0.2f, 1)));
        animation.Duration = TimeSpan.FromMilliseconds(milliseconds);
        visual.StartAnimation("Opacity", animation);
    }
}
