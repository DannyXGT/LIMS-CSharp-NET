using System.Xml.Linq;

namespace Lims.Desktop.Tests;

public sealed class ThemeResourceTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static XDocument Source(string name) => XDocument.Load(Path.Combine(AppContext.BaseDirectory, "ThemeSources", name));

    [Fact]
    public void ApplicationAndDetachedDialogsUseTheCorporateTheme()
    {
        // A dark window root alone leaves Application.Resources and detached dialogs light.
        Assert.Equal("Dark", (string?)Source("App.xaml").Root!.Attribute("RequestedTheme"));
        var dialog = Source("LimsControls.xaml").Descendants().Single(e => (string?)e.Attribute(Xaml + "Key") == "LimsEditorDialogStyle");
        Assert.Contains(dialog.Elements(), e => (string?)e.Attribute("Property") == "RequestedTheme" && (string?)e.Attribute("Value") == "Dark");
    }

    [Fact]
    public void EveryCorporateBrushHasASystemContrastEquivalent()
    {
        var theme = Source("LimsTheme.xaml");
        var dark = theme.Descendants().Single(e => (string?)e.Attribute(Xaml + "Key") == "Dark");
        var contrast = theme.Descendants().Single(e => (string?)e.Attribute(Xaml + "Key") == "HighContrast");
        foreach (var brush in dark.Elements().Where(e => e.Name.LocalName.EndsWith("Brush", StringComparison.Ordinal)))
        {
            var equivalent = contrast.Elements().Single(e => (string?)e.Attribute(Xaml + "Key") == (string?)brush.Attribute(Xaml + "Key"));
            Assert.StartsWith("{ThemeResource SystemColor", (string?)equivalent.Attribute("Color"));
        }
    }

    [Fact]
    public void NativeSelectionAndDisabledTextKeepSystemContrastColors()
    {
        var contrast = Source("LimsControls.xaml").Descendants().Single(e => (string?)e.Attribute(Xaml + "Key") == "HighContrast");
        string? Color(string key) => (string?)contrast.Elements().Single(e => (string?)e.Attribute(Xaml + "Key") == key).Attribute("Color");
        Assert.Equal("{ThemeResource SystemColorHighlightColor}", Color("ListViewItemBackgroundSelected"));
        Assert.Equal("{ThemeResource SystemColorHighlightTextColor}", Color("CalendarViewSelectedForeground"));
        Assert.Equal("{ThemeResource SystemColorGrayTextColor}", Color("TextControlPlaceholderForegroundDisabled"));
    }
}
