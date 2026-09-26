using System.Runtime.InteropServices;
using Windows.Security.Credentials;

namespace Lims.Desktop.Services;

public sealed class PasswordVaultSecureStorage : ISecureStorage
{
    private const string Resource = "Lims.Desktop.Authentication";
    private const string UserName = "refresh-token";
    private readonly PasswordVault _vault = new();

    public Task<string?> ReadRefreshTokenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var credential = _vault.Retrieve(Resource, UserName);
            credential.RetrievePassword();
            return Task.FromResult<string?>(credential.Password);
        }
        catch (COMException)
        {
            return Task.FromResult<string?>(null);
        }
    }

    public Task WriteRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);
        cancellationToken.ThrowIfCancellationRequested();
        RemoveExisting();
        _vault.Add(new PasswordCredential(Resource, UserName, refreshToken));
        return Task.CompletedTask;
    }

    public Task ClearRefreshTokenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoveExisting();
        return Task.CompletedTask;
    }

    private void RemoveExisting()
    {
        try
        {
            _vault.Remove(_vault.Retrieve(Resource, UserName));
        }
        catch (COMException)
        {
            // Credential Locker reports a missing credential through COM.
        }
    }
}
