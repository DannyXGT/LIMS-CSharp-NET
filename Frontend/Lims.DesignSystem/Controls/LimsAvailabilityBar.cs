using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.DesignSystem.Controls;

public sealed class LimsAvailabilityBar : ProgressBar
{
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(string), typeof(LimsAvailabilityBar), new PropertyMetadata("Information", OnToneChanged));
    public string Tone { get => (string)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }

    public LimsAvailabilityBar() => UpdateStyle();
    private static void OnToneChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((LimsAvailabilityBar)sender).UpdateStyle();
    private void UpdateStyle() => Style = (Style)Application.Current.Resources[Tone switch
    {
        "Warning" => "LimsAvailabilityWarningStyle",
        "Danger" => "LimsAvailabilityDangerStyle",
        "Neutral" => "LimsAvailabilityUnknownStyle",
        _ => "LimsAvailabilityBarStyle"
    }];
}
