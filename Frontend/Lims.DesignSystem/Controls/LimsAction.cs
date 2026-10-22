using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lims.DesignSystem.Controls;

/// <summary>Semantic artwork for shared action styles; Button.Content remains its accessible label.</summary>
public static class LimsAction
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(LimsIconKind), typeof(LimsAction), new PropertyMetadata(LimsIconKind.None, OnPresentationChanged));
    public static LimsIconKind GetIcon(DependencyObject target) => (LimsIconKind)target.GetValue(IconProperty);
    public static void SetIcon(DependencyObject target, LimsIconKind value) => target.SetValue(IconProperty, value);

    public static readonly DependencyProperty LabelVisibilityProperty = DependencyProperty.RegisterAttached(
        "LabelVisibility", typeof(Visibility), typeof(LimsAction), new PropertyMetadata(Visibility.Visible, OnPresentationChanged));
    public static Visibility GetLabelVisibility(DependencyObject target) => (Visibility)target.GetValue(LabelVisibilityProperty);
    public static void SetLabelVisibility(DependencyObject target, Visibility value) => target.SetValue(LabelVisibilityProperty, value);

    private static void OnPresentationChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not Button button) return;
        button.Loaded -= OnLoaded;
        button.Loaded += OnLoaded;
        Apply(button);
    }
    private static void OnLoaded(object sender, RoutedEventArgs args) => Apply((Button)sender);
    private static void Apply(Button button)
    {
        void Visit(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is LimsIcon { Name: "ActionIcon" } icon) icon.Icon = GetIcon(button);
                else if (child is ContentPresenter { Name: "ActionLabel" } label) label.Visibility = GetLabelVisibility(button);
                else Visit(child);
            }
        }
        Visit(button);
    }
}
