using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Lims.Desktop.Views;

public sealed partial class CommercialBottleVisual : UserControl
{
    private double _previous = -1;
    private Storyboard? _animation;
    public CommercialBottleVisual() { InitializeComponent(); Unloaded += (_, _) => _animation?.Stop(); }
    public void SetLevel(double value)
    {
        var level = Math.Clamp(value, 0, 1);
        if (Math.Abs(level - _previous) < .0001) return;
        var current = BottleLevel.ScaleY; _animation?.Stop();
        if (_previous >= 0 && Motion.Enabled)
        {
            var change = new DoubleAnimation { From = current, To = level, Duration = new Duration(TimeSpan.FromMilliseconds(160)) };
            Storyboard.SetTarget(change, BottleLevel); Storyboard.SetTargetProperty(change, "ScaleY");
            _animation = new Storyboard(); _animation.Children.Add(change); _animation.Begin();
        }
        else BottleLevel.ScaleY = level;
        _previous = level;
    }
}
