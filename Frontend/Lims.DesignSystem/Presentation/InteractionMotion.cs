namespace Lims.DesignSystem.Presentation;

/// <summary>Shared timing and reduced-motion policy, independent of the system settings provider.</summary>
public static class InteractionMotion
{
    public static float PressedScale(bool animationsEnabled) => animationsEnabled ? 0.965f : 1;
    public static int PressDuration(bool pressed) => pressed ? 70 : 110;
    public static int PopupDuration(bool opening, bool animationsEnabled) => animationsEnabled ? (opening ? 170 : 105) : 60;
}
