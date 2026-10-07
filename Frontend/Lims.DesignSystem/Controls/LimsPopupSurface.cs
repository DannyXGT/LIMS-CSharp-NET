using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.DesignSystem.Controls;

/// <summary>Shared popup chrome, elevation, padding and scrolling for all LIMS pickers.</summary>
public sealed class LimsPopupSurface : ContentControl
{
    public LimsPopupSurface()
    {
        Style = (Style)Application.Current.Resources["LimsPopupSurfaceStyle"];
        IsTabStop = false;
    }
}
