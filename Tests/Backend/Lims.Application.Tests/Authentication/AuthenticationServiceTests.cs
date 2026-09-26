using Lims.Application.Authentication;
using Lims.Application.Authentication.Ports;
using Lims.Application.Authentication.Security;
using Lims.Contracts.Authentication;
using Lims.Contracts.Errors;
using Lims.Domain.Authentication;
using Lims.Domain.Identity;

namespace Lims.Application.Tests.Authentication;

public sealed class AuthenticationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ValidLoginCreatesSessionAndReturnsCompleteProfile()
    {
        var user = CreateUser(isActive: true);
        var users = new FakeUserRepository(user);
        var sessions = new FakeSessionStore();
        var sut = CreateService(users, sessions, passwordVerified: true);

        var result = await sut.LoginAsync(
            new LoginRequest("chemist.user", "correct-password", "test-client"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(user.Email, result.Value.Profile.Email);
        Assert.Equal("Administrador", result.Value.Profile.Role);
        Assert.Equal("Laboratorio Químico", result.Value.Profile.Department);
        Assert.Equal(["chemical.dashboard.view"], result.Value.Profile.Permissions);
        Assert.Equal("access-token", result.Value.AccessToken);
        Assert.NotNull(sessions.CreatedSession);
        Assert.Equal(user.Id, sessions.CreatedSession.UserId);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task InactiveUserOrWrongPasswordReturnsSameGenericFailure(
        bool isActive,
        bool passwordVerified)
    {
        var sut = CreateService(
            new FakeUserRepository(CreateUser(isActive)),
            new FakeSessionStore(),
            passwordVerified);

        var result = await sut.LoginAsync(
            new LoginRequest("chemist.user", "password"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.InvalidCredentials, result.Error?.Code);
        Assert.Equal("El usuario o la contraseña no son válidos.", result.Error?.Message);
    }

    [Fact]
    public async Task ConsumedRefreshTokenRevokesWholeSessionAsReuse()
    {
        var user = CreateUser(isActive: true);
        var sessions = new FakeSessionStore();
        var session = new AuthSession(
            Guid.NewGuid(),
            user.Id,
            Guid.NewGuid(),
            Now,
            Now.AddHours(12));
        var token = new RefreshToken(
            Guid.NewGuid(),
            session.Id,
            "refresh-hash",
            Now,
            Now.AddHours(12));
        token.Consume(Now.AddMinutes(1), Guid.NewGuid());
        sessions.RefreshSnapshot = new RefreshSessionSnapshot(session, token);
        var sut = CreateService(new FakeUserRepository(user), sessions, passwordVerified: true);

        var result = await sut.RefreshAsync(
            new RefreshRequest("refresh-token"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.InvalidSession, result.Error?.Code);
        Assert.Equal(session.Id, sessions.RevokedSessionId);
        Assert.Equal("refresh_token_reuse", sessions.RevocationReason);
    }

    private static AuthenticationService CreateService(
        IUserRepository users,
        IAuthenticationSessionStore sessions,
        bool passwordVerified) =>
        new(
            users,
            sessions,
            new FakePasswordHashService(passwordVerified),
            new FakeAccessTokenService(),
            new FakeRefreshTokenService(),
            new UserIdentifierNormalizer(new CorporateIdentityPolicy()),
            new AuthenticationPolicy(),
            new FixedTimeProvider(Now));

    private static User CreateUser(bool isActive) =>
        new(
            7,
            "Chemist User",
            "chemist.user@intertek.com",
            "$argon2id$v=19$m=65536,t=3,p=4$fixture",
            isActive,
            new Role(1, "Administrador"),
            new Department(2, "Laboratorio Químico"),
            [new UserPermissionGrant(7, "chemical.dashboard.view")]);

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class FakeUserRepository(User? user) : IUserRepository
    {
        public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult(user?.Email == normalizedEmail ? user : null);

        public Task<User?> FindByIdAsync(int userId, CancellationToken cancellationToken) =>
            Task.FromResult(user?.Id == userId ? user : null);

        public Task UpdatePasswordHashAsync(
            User currentUser,
            string passwordHash,
            CancellationToken cancellationToken)
        {
            currentUser.ReplacePasswordHash(passwordHash);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSessionStore : IAuthenticationSessionStore
    {
        public AuthSession? CreatedSession { get; private set; }

        public RefreshSessionSnapshot? RefreshSnapshot { get; set; }

        public Guid? RevokedSessionId { get; private set; }

        public string? RevocationReason { get; private set; }

        public Task CreateAsync(
            AuthSession session,
            RefreshToken refreshToken,
            CancellationToken cancellationToken)
        {
            CreatedSession = session;
            return Task.CompletedTask;
        }

        public Task<RefreshSessionSnapshot?> FindByRefreshTokenHashAsync(
            string tokenHash,
            CancellationToken cancellationToken) =>
            Task.FromResult(RefreshSnapshot);

        public Task<RefreshRotationStatus> RotateAsync(
            Guid currentTokenId,
            RefreshToken replacement,
            DateTimeOffset instant,
            CancellationToken cancellationToken) =>
            Task.FromResult(RefreshRotationStatus.Rotated);

        public Task<AuthSession?> FindActiveSessionAsync(
            Guid sessionId,
            DateTimeOffset instant,
            CancellationToken cancellationToken) =>
            Task.FromResult(CreatedSession?.Id == sessionId && CreatedSession.IsActiveAt(instant)
                ? CreatedSession
                : null);

        public Task<bool> RevokeSessionAsync(
            Guid sessionId,
            DateTimeOffset instant,
            string reason,
            CancellationToken cancellationToken)
        {
            RevokedSessionId = sessionId;
            RevocationReason = reason;
            return Task.FromResult(true);
        }
    }

    private sealed class FakePasswordHashService(bool verified) : IPasswordHashService
    {
        public PasswordCheckResult Verify(string password, string encodedHash) => new(verified, false);

        public string Hash(string password) => "new-hash";
    }

    private sealed class FakeAccessTokenService : IAccessTokenService
    {
        public IssuedAccessToken Issue(
            User user,
            AuthSession session,
            DateTimeOffset issuedAt,
            TimeSpan lifetime) =>
            new("access-token", issuedAt.Add(lifetime));
    }

    private sealed class FakeRefreshTokenService : IRefreshTokenService
    {
        public GeneratedRefreshToken Generate() => new("refresh-token", "refresh-hash");

        public string Hash(string token) => "refresh-hash";
    }
}
