using Lims.DesignSystem.Presentation;

namespace Lims.Desktop.Tests;

public sealed class PopupPresentationTests
{
    private static readonly int[] FilteredIds = [1, 3];
    private static readonly int[] AllIds = [1, 2, 3];
    private sealed record Option(int Id, string Name);

    [Theory]
    [InlineData(true, 0.965f, 170, 105)]
    [InlineData(false, 1f, 60, 60)]
    public void ReducedMotionPreservesMinimalFadeAndRemovesPressScale(bool enabled, float scale, int open, int close)
    {
        Assert.Equal(scale, InteractionMotion.PressedScale(enabled));
        Assert.Equal(open, InteractionMotion.PopupDuration(true, enabled));
        Assert.Equal(close, InteractionMotion.PopupDuration(false, enabled));
    }

    [Fact]
    public void FilteringIgnoresAccentsAndPreservesCatalogueIdentity()
    {
        var methods = new[] { new Option(12, "Método Ácido"), new Option(42, "Bisphenol") };
        var filter = new LocalSelectFilter();
        filter.Load(methods, item => ((Option)item).Name);
        var visible = filter.Visible;
        filter.Apply("  ACIDO  ");
        Assert.Same(visible, filter.Visible);
        Assert.Same(methods[0], Assert.Single(filter.Visible));
        filter.Apply("xyz");
        Assert.Empty(filter.Visible);
        filter.Apply("");
        Assert.Equal(methods, filter.Visible.Cast<Option>());
        Assert.Same(methods[1], filter.Visible[1]);
    }

    [Fact]
    public void FilteringKeepsSourceOrderAcrossSuccessiveQueries()
    {
        var methods = new[] { new Option(1, "Alpha"), new Option(2, "Beta"), new Option(3, "Alpine") };
        var filter = new LocalSelectFilter();
        filter.Load(methods, item => ((Option)item).Name);
        filter.Apply("al");
        Assert.Equal(FilteredIds, filter.Visible.Cast<Option>().Select(x => x.Id));
        filter.Apply("a");
        Assert.Equal(AllIds, filter.Visible.Cast<Option>().Select(x => x.Id));
        filter.Load(null, _ => "");
        Assert.Empty(filter.Visible);
    }

    [Theory]
    [InlineData(100, 200, 300, 700, false)]
    [InlineData(100, 580, 300, 700, true)]
    [InlineData(100, 440, 300, 520, true)]
    public void PopupChoosesAvailableSideAndProtectsFooter(double x, double y, double height, double bottom, bool above)
    {
        var placement = PopupLayout.Place(x, y, 260, 36, 260, height, 1366, 100, bottom);
        Assert.Equal(above, placement.Above);
        Assert.Equal(x, placement.X);
        Assert.InRange(placement.Y, 100, bottom);
        Assert.True(placement.Y + placement.Height <= bottom);
        Assert.Equal(260, placement.Width);
    }

    [Fact]
    public void PopupNearRightEdgeStaysInsideRoot()
    {
        var placement = PopupLayout.Place(1200, 550, 160, 36, 296, 300, 1366, 8, 760);
        Assert.Equal(1062, placement.X);
        Assert.True(placement.X + placement.Width <= 1358);
        Assert.True(placement.Above);
    }

    [Fact]
    public void PopupHeightShrinksWhenNeitherSideFits()
    {
        var placement = PopupLayout.Place(100, 200, 200, 36, 200, 340, 800, 100, 400);
        Assert.Equal(161, placement.Height);
        Assert.False(placement.Above);
        Assert.Equal(400, placement.Y + placement.Height);
    }
}
