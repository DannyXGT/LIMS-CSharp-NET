namespace Lims.Desktop.Services;

public interface ISecureStorage
{
    Task<string?> ReadRefreshTokenAsync(CancellationToken cancellationToken);

    Task WriteRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken);

    Task ClearRefreshTokenAsync(CancellationToken cancellationToken);
}
