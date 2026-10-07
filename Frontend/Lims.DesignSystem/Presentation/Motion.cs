using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace Lims.DesignSystem.Presentation;

/// <summary>Short, replaceable compositor animations. Never changes layout or input state.</summary>
public static class Motion
{
    private static readonly UISettings Settings = new();
    private static readonly ConditionalWeakTable<FrameworkElement, object> Hooked = new();
    public static bool Enabled => Settings.AnimationsEnabled;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnEnabledChanged));
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static readonly DependencyProperty PressedScaleProperty = DependencyProperty.RegisterAttached(
        "PressedScale", typeof(float), typeof(Motion), new PropertyMetadata(0.98f));
    public static float GetPressedScale(DependencyObject element) => (float)element.GetValue(PressedScaleProperty);
    public static void SetPressedScale(DependencyObject element, float value) => element.SetValue(PressedScaleProperty, value);

    private static void OnEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement element || !(bool)args.NewValue) return;
        if (Hooked.TryGetValue(element, out _)) return;
        Hooked.Add(element, new object());
        if (element is Button)
        {
            // Button consumes pointer events internally; handledEventsToo is required.
            element.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => Press(element, Enabled ? GetPressedScale(element) : InteractionMotion.PressedScale(false))), true);
            element.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => Press(element, 1)), true);
            element.PointerExited += (_, _) => Press(element, 1);
            element.PointerCanceled += (_, _) => Press(element, 1);
            element.PointerCaptureLost += (_, _) => Press(element, 1);
            element.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, e) => { if (e.Key is Windows.System.VirtualKey.Space or Windows.System.VirtualKey.Enter) Press(element, Enabled ? GetPressedScale(element) : InteractionMotion.PressedScale(false)); }), true);
            element.AddHandler(UIElement.KeyUpEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, _) => Press(element, 1)), true);
            element.LostFocus += (_, _) => Press(element, 1);
        }
    }

    public static void ConfigureVisibility(FrameworkElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var group = visual.Compositor.CreateAnimationGroup();
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.Target = "Opacity";
        fade.InsertKeyFrame(1, 0);
        fade.Duration = TimeSpan.FromMilliseconds(Enabled ? 90 : 60);
        group.Add(fade);
        if (Enabled)
        {
            var translation = visual.Compositor.CreateVector3KeyFrameAnimation();
            translation.Target = "Translation";
            translation.InsertKeyFrame(1, new Vector3(0, -3, 0));
            translation.Duration = TimeSpan.FromMilliseconds(90);
            group.Add(translation);
        }
        ElementCompositionPreview.SetImplicitHideAnimation(element, group);
    }

    public static void Enter(FrameworkElement element, float x = 0, float y = 0, int milliseconds = 140, float scale = 1)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Translation");
        visual.StopAnimation("Scale");
        visual.Opacity = 1;
        visual.Scale = Vector3.One;
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        Scalar(visual, "Opacity", 0, 1, Enabled ? milliseconds : 60);
        if (!Enabled) return;
        Vector(visual, "Translation", new Vector3(x, y, 0), Vector3.Zero, milliseconds);
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        if (scale != 1) Vector(visual, "Scale", new Vector3(scale, scale, 1), Vector3.One, milliseconds);
    }

    public static Task ExitAsync(FrameworkElement element, int milliseconds = 90, float scale = 0.99f)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Translation");
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        if (!Enabled) { visual.StopAnimation("Scale"); visual.Scale = Vector3.One; }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var batch = visual.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) => { completion.TrySetResult(); batch.Dispose(); };
        Scalar(visual, "Opacity", null, 0, Enabled ? milliseconds : 60);
        if (Enabled) Vector(visual, "Scale", null, new Vector3(scale, scale, 1), milliseconds);
        batch.End();
        return completion.Task;
    }

    public static void Press(FrameworkElement element, float scale)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        if (!Enabled) { visual.StopAnimation("Scale"); visual.Scale = Vector3.One; return; }
        Vector(visual, "Scale", null, new Vector3(scale, scale, 1), InteractionMotion.PressDuration(scale < 1));
    }

    private static void Scalar(Visual visual, string property, float? from, float to, int duration)
    {
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        if (from.HasValue) animation.InsertKeyFrame(0, from.Value);
        animation.InsertKeyFrame(1, to, visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0), new Vector2(0.2f, 1)));
        animation.Duration = TimeSpan.FromMilliseconds(duration);
        visual.StartAnimation(property, animation);
    }

    private static void Vector(Visual visual, string property, Vector3? from, Vector3 to, int duration)
    {
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        if (from.HasValue) animation.InsertKeyFrame(0, from.Value);
        animation.InsertKeyFrame(1, to, visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0), new Vector2(0.2f, 1)));
        animation.Duration = TimeSpan.FromMilliseconds(duration);
        visual.StartAnimation(property, animation);
    }
}
