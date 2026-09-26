namespace Lims.Application.Authentication;

public sealed class AuthenticationPolicy
{
    public const string ConfigurationSection = "Authentication:Session";

    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan RefreshTokenLifetime { get; init; } = TimeSpan.FromHours(12);

    public void Validate()
    {
        if (AccessTokenLifetime <= TimeSpan.Zero || AccessTokenLifetime > TimeSpan.FromMinutes(30))
        {
            throw new InvalidOperationException("Access token lifetime must be between 1 second and 30 minutes.");
        }

        if (RefreshTokenLifetime <= AccessTokenLifetime || RefreshTokenLifetime > TimeSpan.FromDays(30))
        {
            throw new InvalidOperationException("Refresh token lifetime must be greater than access lifetime and at most 30 days.");
        }
    }
}
