using Isopoh.Cryptography.Argon2;
using Lims.Application.Authentication.Ports;

namespace Lims.Infrastructure.Security;

public sealed class Argon2PasswordHashService : IPasswordHashService
{
    private const string CurrentParameters = "$argon2id$v=19$m=65536,t=3,p=4$";

    public PasswordCheckResult Verify(string password, string encodedHash)
    {
        if (string.IsNullOrEmpty(password) ||
            string.IsNullOrWhiteSpace(encodedHash) ||
            !encodedHash.StartsWith("$argon2", StringComparison.Ordinal))
        {
            return new PasswordCheckResult(false, false);
        }

        try
        {
            var verified = Argon2.Verify(encodedHash, password);
            return new PasswordCheckResult(
                verified,
                verified && !encodedHash.StartsWith(CurrentParameters, StringComparison.Ordinal));
        }
        catch (ArgumentException)
        {
            return new PasswordCheckResult(false, false);
        }
        catch (InvalidOperationException)
        {
            return new PasswordCheckResult(false, false);
        }
    }

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return Argon2.Hash(
            password,
            timeCost: 3,
            memoryCost: 65_536,
            parallelism: 4,
            type: Argon2Type.HybridAddressing,
            hashLength: 32);
    }
}
