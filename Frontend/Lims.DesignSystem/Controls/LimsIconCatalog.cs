using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Lims.DesignSystem.Controls;

/// <summary>One vector per meaning. Filled outlines use a 24-unit artboard and a 1.5-unit pen.</summary>
public static class LimsIconCatalog
{
    public const string FontFamilyName = "Segoe Fluent Icons";

    // Even/odd contours form outlines; these contain no color and no state-specific artwork.
    private const string Vial = "F0 M7,2 L17,2 17,6 16,6 16,8 19,11 19,20 Q19,22 17,22 L7,22 Q5,22 5,20 L5,11 8,8 8,6 7,6 Z M8.5,3.5 L8.5,4.5 15.5,4.5 15.5,3.5 Z M9.5,6 L9.5,8.6 6.5,11.6 6.5,20 Q6.5,20.5 7,20.5 L17,20.5 Q17.5,20.5 17.5,20 L17.5,11.6 14.5,8.6 14.5,6 Z M7,14 L17,14 17,15.5 7,15.5 Z";
    private const string NewVial = "F0 M4,2 L12,2 12,6 11,6 11,8 14,11 14,20 Q14,22 12,22 L4,22 Q2,22 2,20 L2,11 5,8 5,6 4,6 Z M5.5,3.5 L5.5,4.5 10.5,4.5 10.5,3.5 Z M6.5,6 L6.5,8.6 3.5,11.6 3.5,20 Q3.5,20.5 4,20.5 L12,20.5 Q12.5,20.5 12.5,20 L12.5,11.6 9.5,8.6 9.5,6 Z M4,14 L12,14 12,15.5 4,15.5 Z M18.25,5 L19.75,5 19.75,8.25 23,8.25 23,9.75 19.75,9.75 19.75,13 18.25,13 18.25,9.75 15,9.75 15,8.25 18.25,8.25 Z";
    private const string Flask = "F0 M8,2 L16,2 16,3.5 15,3.5 15,9.8 21.4,19.4 Q23,22 20,22 L4,22 Q1,22 2.6,19.4 L9,9.8 9,3.5 8,3.5 Z M10.5,3.5 L10.5,10.25 4,20 Q3.7,20.5 4.3,20.5 L19.7,20.5 Q20.3,20.5 20,20 L13.5,10.25 13.5,3.5 Z M7,15 L17,15 18,16.5 6,16.5 Z";
    private const string Replenish = "F0 M3,9 L14,9 14,22 3,22 Z M4.5,10.5 L4.5,20.5 12.5,20.5 12.5,10.5 Z M7,10.5 L8.5,10.5 8.5,14 7,14 Z M15,2 L21,2 21,3.5 17.6,3.5 23,8.9 21.9,10 16.5,4.6 16.5,8 15,8 Z";
    private const string ReplaceArrows = "M3,5 L17.4,5 14.7,2.3 15.8,1.2 20.4,5.75 15.8,10.3 14.7,9.2 17.4,6.5 3,6.5 Z M21,17.5 L6.6,17.5 9.3,14.8 8.2,13.7 3.6,18.25 8.2,22.8 9.3,21.7 6.6,19 21,19 Z";
    private const string Available = "F0 M3,3 L21,3 21,21 3,21 Z M4.5,4.5 L4.5,19.5 19.5,19.5 19.5,4.5 Z M7,12 L8.1,10.9 11,13.8 16.1,8.7 17.2,9.8 11,16 Z";
    private const string DoorArrow = "M3,2 L12,2 12,7 10.5,7 10.5,3.5 4.5,3.5 4.5,20.5 10.5,20.5 10.5,17 12,17 12,22 3,22 Z M8,11.25 L19.4,11.25 15.7,7.55 16.8,6.45 22.35,12 16.8,17.55 15.7,16.45 19.4,12.75 8,12.75 Z";
    private const string ArchiveBox = "F0 M2,3 L22,3 22,8 20.5,8 20.5,21 3.5,21 3.5,8 2,8 Z M3.5,4.5 L3.5,6.5 20.5,6.5 20.5,4.5 Z M5,8 L5,19.5 19,19.5 19,8 Z M9,10 L15,10 15,11.5 9,11.5 Z";
    private const string ClearFunnel = "M2,3 L16,3 16,4.5 10,11 10,19 6,21 6,11 2,6.5 Z M3.5,4.5 L3.5,5.9 7.5,10.4 7.5,18.6 8.5,18.1 8.5,10.4 14,4.5 Z M16.3,13.3 L19,16 21.7,13.3 22.8,14.4 20.1,17.1 22.8,19.8 21.7,20.9 19,18.2 16.3,20.9 15.2,19.8 17.9,17.1 15.2,14.4 Z";
    private const string JobSearch = "F0 M4,2 L17,2 17,10 15.5,10 15.5,3.5 5.5,3.5 5.5,20.5 10,20.5 10,22 4,22 Z M7.5,6 L13.5,6 13.5,7.5 7.5,7.5 Z M7.5,10 L11,10 11,11.5 7.5,11.5 Z M16,11 C18.8,11 21,13.2 21,16 C21,18.8 18.8,21 16,21 C13.2,21 11,18.8 11,16 C11,13.2 13.2,11 16,11 Z M16,12.5 C14.1,12.5 12.5,14.1 12.5,16 C12.5,17.9 14.1,19.5 16,19.5 C17.9,19.5 19.5,17.9 19.5,16 C19.5,14.1 17.9,12.5 16,12.5 Z M19,18 L23,22 22,23 18,19 Z";
    private const string ReportEntry = "F0 M4,2 L18,2 18,9 16.5,9 16.5,3.5 5.5,3.5 5.5,20.5 10,20.5 10,22 4,22 Z M8,6 L14,6 14,7.5 8,7.5 Z M8,10 L13,10 13,11.5 8,11.5 Z M8,14 L11,14 11,15.5 8,15.5 Z M19,10 L23,14 15,22 10,23 11,18 Z M19,12.1 L12.4,18.7 12,21 14.3,20.6 20.9,14 Z";
    private const string ClosedFolder = "F0 M2,5 L10,5 12,7 22,7 22,21 2,21 Z M3.5,6.5 L3.5,19.5 20.5,19.5 20.5,8.5 11.4,8.5 9.4,6.5 Z M7,13.5 L8.1,12.4 11,15.3 16.4,9.9 17.5,11 11,17.5 Z";
    private const string ReviewInbox = "F0 M5,3 L19,3 19,12 22,16 22,22 2,22 2,16 5,12 Z M6.5,4.5 L6.5,14 9,14 10.5,17 13.5,17 15,14 17.5,14 17.5,4.5 Z M5,14.5 L3.5,16.5 3.5,20.5 20.5,20.5 20.5,16.5 19,14.5 19,15.5 16,15.5 14.5,18.5 9.5,18.5 8,15.5 5,15.5 Z M8.5,8.5 L9.6,7.4 11,8.8 14.4,5.4 15.5,6.5 11,11 Z";
    private const string WorkspaceTiles = "F0 M3,3 L10.5,3 10.5,10.5 3,10.5 Z M4.5,4.5 L4.5,9 9,9 9,4.5 Z M13.5,3 L21,3 21,10.5 13.5,10.5 Z M15,4.5 L15,9 19.5,9 19.5,4.5 Z M3,13.5 L10.5,13.5 10.5,21 3,21 Z M4.5,15 L4.5,19.5 9,19.5 9,15 Z M13.5,13.5 L21,13.5 21,21 13.5,21 Z M15,15 L15,19.5 19.5,19.5 19.5,15 Z";
    private const string ImportTray = "M11.25,2 L12.75,2 12.75,14.4 16.45,10.7 17.55,11.8 12,17.35 6.45,11.8 7.55,10.7 11.25,14.4 Z M3,15 L4.5,15 4.5,20.5 19.5,20.5 19.5,15 21,15 21,22 3,22 Z";

    public static string? GeometryData(LimsIconKind icon) => icon switch
    {
        LimsIconKind.ReferenceMaterials or LimsIconKind.Standards => Vial,
        LimsIconKind.NewStandard => NewVial,
        LimsIconKind.Preparations => Flask,
        LimsIconKind.Replenishment => Replenish,
        LimsIconKind.Replace => ReplaceArrows,
        LimsIconKind.Availability => Available,
        LimsIconKind.SignOut => DoorArrow,
        LimsIconKind.Archive => ArchiveBox,
        LimsIconKind.Jobs => JobSearch,
        LimsIconKind.Digitization => ReportEntry,
        LimsIconKind.Closures => ClosedFolder,
        LimsIconKind.ReviewQueue => ReviewInbox,
        LimsIconKind.Equivalences => ReplaceArrows,
        LimsIconKind.SwitchWorkspace => WorkspaceTiles,
        LimsIconKind.Import => ImportTray,
        LimsIconKind.Notifications => "F0 M12,2 C8.4,2 6,4.7 6,8 L6,13 Q6,15 4,16 L4,18 20,18 20,16 Q18,15 18,13 L18,8 C18,4.7 15.6,2 12,2 Z M12,3.5 C14.8,3.5 16.5,5.6 16.5,8 L16.5,13 Q16.5,15 18,16.5 L6,16.5 Q7.5,15 7.5,13 L7.5,8 C7.5,5.6 9.2,3.5 12,3.5 Z M9,20 L10.5,19.5 Q12,22 13.5,19.5 L15,20 Q12,24 9,20 Z",
        LimsIconKind.ClearFilters => "F0 " + ClearFunnel,
        _ => null
    };

    public static string? Glyph(LimsIconKind icon) => icon switch
    {
        LimsIconKind.Home => "\uE80F",
        LimsIconKind.Scheduling or LimsIconKind.Calendar => "\uE787",
        LimsIconKind.Results => "\uE9D5", // CheckList: completed analytical results
        LimsIconKind.Quality => "\uEA18", // Shield
        LimsIconKind.Reports => "\uE9F9", // ReportDocument
        LimsIconKind.Administration => "\uE9E9", // Equalizer: controls, not a gear
        LimsIconKind.Summary => "\uE8A9", // ViewAll
        LimsIconKind.Alerts => "\uE7BA", // Warning
        LimsIconKind.Inventory => "\uE7B8", // Package
        LimsIconKind.Traceability => "\uE81C", // History
        LimsIconKind.Documentation or LimsIconKind.ViewDetail => "\uE8A5", // Document
        LimsIconKind.Edit => "\uE70F",
        LimsIconKind.Save => "\uE74E",
        LimsIconKind.Add => "\uE710",
        LimsIconKind.Refresh => "\uE72C",
        LimsIconKind.Mail => "\uE715",
        LimsIconKind.Check => "\uE73E",
        LimsIconKind.Maximize => "\uE740",
        LimsIconKind.Restore => "\uE73F",
        LimsIconKind.Cancel => "\uE711",
        LimsIconKind.Delete => "\uE74D",
        LimsIconKind.Search => "\uE721",
        LimsIconKind.Filter or LimsIconKind.ApplyFilters => "\uE71C",
        LimsIconKind.Export => "\uEDE1", // Export
        LimsIconKind.Print => "\uE749",
        LimsIconKind.ChevronLeft => "\uE76B",
        LimsIconKind.ChevronRight => "\uE76C",
        LimsIconKind.ChevronDown => "\uE70D",
        LimsIconKind.User => "\uE77B",
        LimsIconKind.Lock or LimsIconKind.Access => "\uE72E",
        LimsIconKind.ShowPassword => "\uE890", // View
        LimsIconKind.HidePassword => "\uED1A", // Hide (E8F5 means CalendarReply)
        LimsIconKind.SignIn => "\uE72A",
        LimsIconKind.Error => "\uE783",
        _ => null
    };

    internal static IconElement CreateElement(LimsIconKind icon, double size)
    {
        IconElement element;
        if (GeometryData(icon) is { } data)
        {
            var geometry = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);
            // PathIcon does not scale its Data to Width/Height. Scale the artboard explicitly,
            // with the same factor on both axes, so 16-unit buttons cannot clip a 24-unit path.
            if (size != 24d) geometry.Transform = new ScaleTransform { ScaleX = size / 24d, ScaleY = size / 24d };
            element = new PathIcon { Data = geometry };
        }
        else
        {
            element = new FontIcon { FontFamily = new FontFamily(FontFamilyName), FontSize = size, Glyph = Glyph(icon) ?? string.Empty, IsTextScaleFactorEnabled = false };
        }
        element.Width = element.Height = size;
        element.HorizontalAlignment = HorizontalAlignment.Center;
        element.VerticalAlignment = VerticalAlignment.Center;
        element.IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(element, AccessibilityView.Raw);
        return element;
    }
}
