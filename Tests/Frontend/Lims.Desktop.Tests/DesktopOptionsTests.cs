using Lims.Desktop.Services;

namespace Lims.Desktop.Tests;

public sealed class DesktopOptionsTests
{
    [Fact]
    public void GetValidatedBaseAddressAcceptsHttps()
    {
        var options = CreateOptions("https://lims.example/");

        var result = options.GetValidatedBaseAddress();

        Assert.Equal("https://lims.example/", result.AbsoluteUri);
    }

    [Fact]
    public void GetValidatedBaseAddressAcceptsApprovedInternalHttpEndpoint()
    {
        var options = CreateOptions("http://10.226.248.191:8080/");

        var result = options.GetValidatedBaseAddress();

        Assert.Equal("http://10.226.248.191:8080/", result.AbsoluteUri);
    }

    [Fact]
    public void GetValidatedBaseAddressRejectsOtherHttpEndpoints()
    {
        var options = CreateOptions("http://10.226.248.192:8080/");

        Assert.Throws<InvalidOperationException>(() => options.GetValidatedBaseAddress());
    }

    private static DesktopOptions CreateOptions(string apiBaseUrl) => new()
    {
        ApiBaseUrl = apiBaseUrl,
        CorporateDomain = "intertek.com",
        EnvironmentName = "Development",
        RequestTimeoutSeconds = 30,
    };
}
