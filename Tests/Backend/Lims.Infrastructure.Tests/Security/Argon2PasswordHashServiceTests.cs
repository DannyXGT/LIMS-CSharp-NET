using System.Text;
using Isopoh.Cryptography.Argon2;
using Isopoh.Cryptography.SecureArray;
using Lims.Infrastructure.Security;

namespace Lims.Infrastructure.Tests.Security;

public sealed class Argon2PasswordHashServiceTests
{
    private const string Password = "LegacyTest!2026";

    [Fact]
    public void HashProducesCurrentArgon2idPhcFormatThatCanBeVerified()
    {
        var service = new Argon2PasswordHashService();

        var encodedHash = service.Hash(Password);
        var result = service.Verify(Password, encodedHash);

        Assert.StartsWith("$argon2id$v=19$m=65536,t=3,p=4$", encodedHash, StringComparison.Ordinal);
        Assert.True(result.Verified);
        Assert.False(result.NeedsRehash);
        Assert.False(service.Verify("incorrect", encodedHash).Verified);
    }

    [Fact]
    public void VerifyAcceptsPasslibCompatibleArgon2idPhcEncoding()
    {
        var encodedHash = CreateDeterministicPhcVector();
        var service = new Argon2PasswordHashService();

        var result = service.Verify(Password, encodedHash);

        Assert.StartsWith(
            "$argon2id$v=19$m=65536,t=3,p=4$MDEyMzQ1Njc4OWFiY2RlZg$",
            encodedHash,
            StringComparison.Ordinal);
        Assert.True(result.Verified);
        Assert.False(result.NeedsRehash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-phc-hash")]
    [InlineData("$argon2id$malformed")]
    public void VerifyRejectsInvalidOrMalformedHashes(string encodedHash)
    {
        var service = new Argon2PasswordHashService();

        var result = service.Verify(Password, encodedHash);

        Assert.False(result.Verified);
        Assert.False(result.NeedsRehash);
    }

    private static string CreateDeterministicPhcVector()
    {
        var configuration = new Argon2Config
        {
            Type = Argon2Type.HybridAddressing,
            Version = Argon2Version.Nineteen,
            TimeCost = 3,
            MemoryCost = 65_536,
            Lanes = 4,
            Threads = 4,
            Password = Encoding.UTF8.GetBytes(Password),
            Salt = Encoding.ASCII.GetBytes("0123456789abcdef"),
            HashLength = 32,
        };

        using var argon2 = new Argon2(configuration);
        using SecureArray<byte> hash = argon2.Hash();
        return configuration.EncodeString(hash.Buffer);
    }
}
