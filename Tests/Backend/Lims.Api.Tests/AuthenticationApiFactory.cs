using Lims.Application.Authentication;
using Lims.Application.Authentication.Ports;
using Lims.Application.Authentication.Security;
using Lims.Application.Common;
using Lims.Application.ReferenceMaterials;
using Lims.Contracts.Authentication;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferenceMaterials;
using Lims.Domain.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lims.Api.Tests;

public sealed class AuthenticationApiFactory : WebApplicationFactory<Program>
{
    public const string SigningKey = "testing-only-signing-key-32-characters-minimum";
    public static readonly Guid SessionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public const int UserId = 42;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Lims"] = "Host=localhost;Database=never_used;Username=never_used",
                ["Authentication:Jwt:Issuer"] = "Lims.Api.Tests",
                ["Authentication:Jwt:Audience"] = "Lims.Api.Tests.Client",
                ["Authentication:Jwt:SigningKey"] = SigningKey,
                ["Observability:ConsoleExporter"] = "false",
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAuthenticationService>();
            services.RemoveAll<IAuthenticationSessionStore>();
            services.RemoveAll<IReferenceMaterialService>();
            services.AddSingleton<IAuthenticationService, FakeAuthenticationService>();
            services.AddSingleton<IAuthenticationSessionStore, FakeSessionStore>();
            services.AddSingleton<IReferenceMaterialService, FakeReferenceMaterialService>();
        });
    }

    private sealed class FakeReferenceMaterialService : IReferenceMaterialService
    {
        public Task<IReadOnlyList<ReferenceMethodOption>> GetMethodsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReferenceMethodOption>>([new(1, "APEOs")]);

        public Task<IReadOnlyList<ReferenceUnitOption>> GetUnitsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReferenceUnitOption>>([new(2, "Gramo", "g")]);

        public Task<IReadOnlyList<ReferenceLocationOption>> GetLocationsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReferenceLocationOption>>([new(1, "Laboratorio")]);

        public Task<OperationResult<ReferenceMaterialPage>> ListAsync(
            string? search,
            string? status,
            string? method,
            int page,
            int pageSize,
            CancellationToken cancellationToken) => Task.FromResult(OperationResult.Success(
            new ReferenceMaterialPage([], page, pageSize, 0)));

        public Task<OperationResult<ReferenceMaterialDetail>> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(NotFound());

        public Task<OperationResult<ReferenceMaterialDetail>> CreateAsync(
            CreateReferenceMaterialRequest request,
            int actorUserId,
            CancellationToken cancellationToken) => Task.FromResult(NotFound());

        public Task<OperationResult<ReferenceMaterialDetail>> UpdateAsync(
            Guid id,
            UpdateReferenceMaterialRequest request,
            int actorUserId,
            CancellationToken cancellationToken) => Task.FromResult(NotFound());

        public Task<OperationResult<ReferenceMaterialDetail>> ArchiveAsync(
            Guid id,
            ArchiveReferenceMaterialRequest request,
            int actorUserId,
            CancellationToken cancellationToken) => Task.FromResult(NotFound());

        public Task<OperationResult<ReferenceMaterialDetail>> ReplaceAsync(
            Guid id,
            ReplaceReferenceMaterialRequest request,
            int actorUserId,
            CancellationToken cancellationToken) => Task.FromResult(NotFound());

        private static OperationResult<ReferenceMaterialDetail> NotFound() =>
            OperationResult.Failure<ReferenceMaterialDetail>(new OperationError(
                ErrorCodes.NotFound,
                "No encontrado."));
    }

    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        private static readonly UserProfile Profile = new(
            UserId,
            "Test User",
            "test.user@intertek.com",
            "Administrador",
            "Laboratorio Químico",
            ["users.manage"]);

        public Task<OperationResult<AuthenticationResponse>> LoginAsync(
            LoginRequest request,
            CancellationToken cancellationToken)
        {
            var result = request.Identifier == "valid.user"
                ? OperationResult.Success(new AuthenticationResponse(
                    "access-token",
                    DateTimeOffset.UtcNow.AddMinutes(10),
                    "refresh-token",
                    DateTimeOffset.UtcNow.AddHours(12),
                    Profile))
                : OperationResult.Failure<AuthenticationResponse>(new OperationError(
                    ErrorCodes.InvalidCredentials,
                    "El usuario o la contraseña no son válidos."));
            return Task.FromResult(result);
        }

        public Task<OperationResult<AuthenticationResponse>> RefreshAsync(
            RefreshRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Failure<AuthenticationResponse>(new OperationError(
                ErrorCodes.InvalidSession,
                "La sesión no es válida o expiró.")));

        public Task<OperationResult<LogoutResponse>> LogoutAsync(
            Guid sessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Success(new LogoutResponse(sessionId == SessionId)));

        public Task<OperationResult<UserProfile>> GetCurrentUserAsync(
            Guid sessionId,
            int userId,
            CancellationToken cancellationToken) =>
            Task.FromResult(sessionId == SessionId && userId == UserId
                ? OperationResult.Success(Profile)
                : OperationResult.Failure<UserProfile>(new OperationError(
                    ErrorCodes.InvalidSession,
                    "La sesión no es válida o expiró.")));
    }

    private sealed class FakeSessionStore : IAuthenticationSessionStore
    {
        private static readonly AuthSession Session = new(
            SessionId,
            UserId,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1));

        public Task CreateAsync(
            AuthSession session,
            RefreshToken refreshToken,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<RefreshSessionSnapshot?> FindByRefreshTokenHashAsync(
            string tokenHash,
            CancellationToken cancellationToken) => Task.FromResult<RefreshSessionSnapshot?>(null);

        public Task<RefreshRotationStatus> RotateAsync(
            Guid currentTokenId,
            RefreshToken replacement,
            DateTimeOffset instant,
            CancellationToken cancellationToken) => Task.FromResult(RefreshRotationStatus.NotFound);

        public Task<AuthSession?> FindActiveSessionAsync(
            Guid sessionId,
            DateTimeOffset instant,
            CancellationToken cancellationToken) =>
            Task.FromResult<AuthSession?>(sessionId == SessionId ? Session : null);

        public Task<bool> RevokeSessionAsync(
            Guid sessionId,
            DateTimeOffset instant,
            string reason,
            CancellationToken cancellationToken) => Task.FromResult(sessionId == SessionId);
    }
}
