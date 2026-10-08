using Lims.Contracts.ReferencePreparations;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Lims.Desktop.Views;

public sealed partial class GraduatedCylinder : UserControl
{
    private decimal _previous = -1;
    private Storyboard? _animation;
    public GraduatedCylinder() => InitializeComponent();
    public void SetVolumes(decimal takenMl, decimal finalMl, bool animate = true)
    {
        var percent = IntermediateCalculator.OccupiedPercent(takenMl, finalMl);
        var scale = (double)Math.Clamp(percent / 100m, 0, 1);
        var error = finalMl > 0 && takenMl > finalMl;
        Liquid.Background = (Brush)Application.Current.Resources[error ? "LimsDangerBrush" : "LimsAccentBrush"];
        Tick100.Text = StockPresentation.Quantity(finalMl, "mL");
        Tick75.Text = StockPresentation.Quantity(finalMl * .75m, "mL");
        Tick50.Text = StockPresentation.Quantity(finalMl * .5m, "mL");
        Tick25.Text = StockPresentation.Quantity(finalMl * .25m, "mL");
        PercentText.Text = error ? $"{StockPresentation.Number(percent, "%")} % · Exceso" : $"{StockPresentation.Number(percent, "%")} %";
        AutomationProperties.SetName(this, $"{StockPresentation.Quantity(takenMl, "mL")} añadidos; aforar hasta {StockPresentation.Quantity(finalMl, "mL")}; {StockPresentation.Quantity(finalMl - takenMl, "mL")} restantes; {PercentText.Text}");
        if (_previous == percent) return;
        var current = LiquidScale.ScaleY;
        _animation?.Stop();
        LiquidScale.ScaleY = current;
        if (animate && Motion.Enabled && _previous >= 0)
        {
            var animation = new DoubleAnimation { From = current, To = scale, Duration = new Duration(TimeSpan.FromMilliseconds(240)), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(animation, LiquidScale); Storyboard.SetTargetProperty(animation, "ScaleY");
            _animation = new Storyboard(); _animation.Children.Add(animation); _animation.Begin();
        }
        else LiquidScale.ScaleY = scale;
        _previous = percent;
    }
}
