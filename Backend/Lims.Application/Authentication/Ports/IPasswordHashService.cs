namespace Lims.Application.Authentication.Ports;

public interface IPasswordHashService
{
    PasswordCheckResult Verify(string password, string encodedHash);

    string Hash(string password);
}

public readonly record struct PasswordCheckResult(bool Verified, bool NeedsRehash);
