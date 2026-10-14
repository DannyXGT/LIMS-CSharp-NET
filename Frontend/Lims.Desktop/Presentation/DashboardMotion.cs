using System.Numerics;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
namespace Lims.Desktop.Presentation;
internal static class DashboardMotion
{
    public static void Enter(FrameworkElement element, int delay = 0)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        visual.StopAnimation("Opacity"); visual.StopAnimation("Translation");
        visual.Opacity = 1; visual.Properties.InsertVector3("Translation", Vector3.Zero);
        if (!Motion.Enabled) return;
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0); fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(180); fade.DelayTime = TimeSpan.FromMilliseconds(delay);
        fade.DelayBehavior = Microsoft.UI.Composition.AnimationDelayBehavior.SetInitialValueBeforeDelay;
        var slide = visual.Compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0, new Vector3(0, 6, 0)); slide.InsertKeyFrame(1, Vector3.Zero);
        slide.Duration = fade.Duration; slide.DelayTime = fade.DelayTime; slide.DelayBehavior = fade.DelayBehavior;
        visual.StartAnimation("Opacity", fade); visual.StartAnimation("Translation", slide);
    }
    public static void Hover(FrameworkElement element, bool over)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        if (!Motion.Enabled) { visual.StopAnimation("Scale"); visual.Scale = Vector3.One; return; }
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(1, new Vector3(over ? 1.008f : 1, over ? 1.008f : 1, 1));
        animation.Duration = TimeSpan.FromMilliseconds(140); visual.StartAnimation("Scale", animation);
    }
}
