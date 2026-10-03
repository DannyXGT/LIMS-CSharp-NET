using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.DesignSystem.Controls;

public sealed partial class StatusBadge : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(StatusBadge), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(string), typeof(StatusBadge), new PropertyMetadata("Neutral", OnToneChanged));

    public StatusBadge()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateTone();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
    public string Tone
    {
        get => (string)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    private static void OnToneChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((StatusBadge)sender).UpdateTone();

    private void UpdateTone() => VisualStateManager.GoToState(this,
        Tone is "Success" or "Warning" or "Danger" or "Information" ? Tone : "Neutral", false);
}
