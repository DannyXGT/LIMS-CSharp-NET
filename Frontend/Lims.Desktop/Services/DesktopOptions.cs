namespace Lims.Desktop.Services;

public sealed class DesktopOptions
{
    public const string ConfigurationSection = "Desktop";
    private const string ApprovedInternalHttpHost = "10.226.248.191";
    private const int ApprovedInternalHttpPort = 8080;

    public string ApiBaseUrl { get; init; } = string.Empty;

    public string EnvironmentName { get; init; } = "Production";

    public string CorporateDomain { get; init; } = "intertek.com";

    public int RequestTimeoutSeconds { get; init; } = 30;

    public Uri GetValidatedBaseAddress()
    {
        if (!Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !IsApprovedInternalHttpEndpoint(uri)))
        {
            throw new InvalidOperationException(
                $"Desktop:ApiBaseUrl must be an absolute HTTPS URL or the approved internal endpoint http://{ApprovedInternalHttpHost}:{ApprovedInternalHttpPort}/.");
        }

        if (RequestTimeoutSeconds is < 5 or > 120)
        {
            throw new InvalidOperationException("Desktop:RequestTimeoutSeconds must be between 5 and 120.");
        }

        if (string.IsNullOrWhiteSpace(CorporateDomain) || CorporateDomain.Contains('@', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Desktop:CorporateDomain is invalid.");
        }

        return uri;
    }

    private static bool IsApprovedInternalHttpEndpoint(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp &&
        string.Equals(uri.Host, ApprovedInternalHttpHost, StringComparison.OrdinalIgnoreCase) &&
        uri.Port == ApprovedInternalHttpPort &&
        uri.AbsolutePath == "/" &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment);
}
