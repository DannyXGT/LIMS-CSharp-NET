namespace Lims.DesignSystem.Presentation;

/// <summary>Placement in public XamlRoot coordinates, also used by the visual validation bank.</summary>
public readonly record struct PopupPlacement(double X, double Y, double Width, double Height, bool Above);

public static class PopupLayout
{
    public static PopupPlacement Place(double x, double y, double fieldWidth, double fieldHeight,
        double desiredWidth, double desiredHeight, double rootWidth, double top, double bottom)
    {
        const double gap = 3;
        var width = Math.Min(desiredWidth, Math.Max(0, rootWidth - 16));
        var below = Math.Max(0, bottom - y - fieldHeight - gap);
        var above = Math.Max(0, y - top - gap);
        var openAbove = desiredHeight > below && above > below;
        var height = Math.Min(desiredHeight, openAbove ? above : below);
        return new PopupPlacement(Math.Clamp(x, 8, Math.Max(8, rootWidth - width - 8)),
            openAbove ? y - height - gap : y + fieldHeight + gap, width, height, openAbove);
    }
}
