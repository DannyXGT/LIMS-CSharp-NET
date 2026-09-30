using Lims.Application.Authentication.Security;
using Lims.Domain.Authentication;
using Lims.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Lims.Infrastructure.Tests.Persistence;

public sealed class AuthenticationSessionStoreTests
{
    [Fact]
    public async Task CreateAsyncPersistsSessionAndRefreshTokenWithRetryingStrategy()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var session = new AuthSession(Guid.NewGuid(), 42, Guid.NewGuid(), now, now.AddHours(8));
        var refreshToken = CreateToken(session.Id, now, "create");

        RetryingTestExecutionStrategyFactory.Reset();

        await fixture.Store.CreateAsync(session, refreshToken, CancellationToken.None);

        Assert.True(RetryingTestExecutionStrategyFactory.CreatedCount > 0);
        Assert.NotNull(await fixture.Context.AuthSessions.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == session.Id));
        Assert.NotNull(await fixture.Context.RefreshTokens.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == refreshToken.Id));
    }

    [Fact]
    public async Task RotateAsyncPersistsReplacementAndConsumesCurrentTokenWithRetryingStrategy()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var session = new AuthSession(Guid.NewGuid(), 42, Guid.NewGuid(), now, now.AddHours(8));
        var currentToken = CreateToken(session.Id, now, "current");
        await fixture.Store.CreateAsync(session, currentToken, CancellationToken.None);
        var replacement = CreateToken(session.Id, now.AddMinutes(1), "replacement");

        RetryingTestExecutionStrategyFactory.Reset();

        var result = await fixture.Store.RotateAsync(
            currentToken.Id,
            replacement,
            now.AddMinutes(1),
            CancellationToken.None);

        Assert.Equal(RefreshRotationStatus.Rotated, result);
        Assert.True(RetryingTestExecutionStrategyFactory.CreatedCount > 0);
        var persistedCurrent = await fixture.Context.RefreshTokens.AsNoTracking()
            .SingleAsync(token => token.Id == currentToken.Id);
        Assert.Equal(replacement.Id, persistedCurrent.ReplacedByTokenId);
        Assert.Equal(now.AddMinutes(1), persistedCurrent.ConsumedAt);
        Assert.NotNull(await fixture.Context.RefreshTokens.AsNoTracking()
            .SingleOrDefaultAsync(token => token.Id == replacement.Id));
    }

    [Fact]
    public async Task RevokeSessionAsyncRevokesSessionAndTokensWithRetryingStrategy()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var session = new AuthSession(Guid.NewGuid(), 42, Guid.NewGuid(), now, now.AddHours(8));
        var refreshToken = CreateToken(session.Id, now, "revoke");
        await fixture.Store.CreateAsync(session, refreshToken, CancellationToken.None);
        var revokedAt = now.AddMinutes(1);

        RetryingTestExecutionStrategyFactory.Reset();

        var result = await fixture.Store.RevokeSessionAsync(
            session.Id,
            revokedAt,
            "logout",
            CancellationToken.None);

        Assert.True(result);
        Assert.True(RetryingTestExecutionStrategyFactory.CreatedCount > 0);
        var persistedSession = await fixture.Context.AuthSessions.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == session.Id);
        var persistedToken = await fixture.Context.RefreshTokens.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == refreshToken.Id);
        Assert.Equal(revokedAt, persistedSession.RevokedAt);
        Assert.Equal("logout", persistedSession.RevocationReason);
        Assert.Equal(revokedAt, persistedToken.RevokedAt);
    }

    private static RefreshToken CreateToken(Guid sessionId, DateTimeOffset createdAt, string suffix) =>
        new(Guid.NewGuid(), sessionId, $"{suffix.PadRight(64, '0')}", createdAt, createdAt.AddHours(4));

    private sealed class StoreFixture : IAsyncDisposable
    {
        private StoreFixture(SqliteConnection connection, LimsDbContext context)
        {
            Connection = connection;
            Context = context;
            Store = new AuthenticationSessionStore(context);
        }

        public SqliteConnection Connection { get; }

        public LimsDbContext Context { get; }

        public AuthenticationSessionStore Store { get; }

        public static async Task<StoreFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<LimsDbContext>()
                .UseSqlite(connection)
                .ReplaceService<IExecutionStrategyFactory, RetryingTestExecutionStrategyFactory>()
                .ReplaceService<IModelCustomizer, SqliteTestModelCustomizer>()
                .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
                .Options;
            var context = new LimsDbContext(options);
            await CreateAuthenticationTablesAsync(connection);
            return new StoreFixture(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }

        private static async Task CreateAuthenticationTablesAsync(SqliteConnection connection)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE auth_sessions (
                    id TEXT NOT NULL PRIMARY KEY,
                    user_id INTEGER NOT NULL,
                    token_family_id TEXT NOT NULL UNIQUE,
                    created_at TEXT NOT NULL,
                    last_seen_at TEXT NOT NULL,
                    expires_at TEXT NOT NULL,
                    revoked_at TEXT NULL,
                    revocation_reason TEXT NULL,
                    client_name TEXT NULL
                );
                CREATE TABLE auth_refresh_tokens (
                    id TEXT NOT NULL PRIMARY KEY,
                    session_id TEXT NOT NULL,
                    token_hash TEXT NOT NULL UNIQUE,
                    created_at TEXT NOT NULL,
                    expires_at TEXT NOT NULL,
                    consumed_at TEXT NULL,
                    replaced_by_token_id TEXT NULL,
                    revoked_at TEXT NULL,
                    FOREIGN KEY (session_id) REFERENCES auth_sessions (id) ON DELETE CASCADE,
                    FOREIGN KEY (replaced_by_token_id) REFERENCES auth_refresh_tokens (id) ON DELETE RESTRICT
                );
                """;
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class RetryingTestExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        private static int createdCount;

        public static int CreatedCount => Volatile.Read(ref createdCount);

        public static void Reset() => Volatile.Write(ref createdCount, 0);

        public IExecutionStrategy Create()
        {
            Interlocked.Increment(ref createdCount);
            return new RetryingTestExecutionStrategy(dependencies);
        }
    }

    private sealed class RetryingTestExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 3, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }

    private sealed class SqliteTestModelCustomizer(ModelCustomizerDependencies dependencies)
        : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);

            foreach (var property in modelBuilder.Model.GetEntityTypes()
                         .SelectMany(entityType => entityType.GetProperties())
                         .Where(property => property.ClrType == typeof(DateTimeOffset)))
            {
                property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
            }
        }
    }
}
