using Lims.Application.Common;
using Lims.Application.ReferencePreparations;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;
using Lims.Domain.ReferenceMaterials;
using Lims.Domain.ReferencePreparations;

namespace Lims.Application.Tests.ReferenceMaterials;

public sealed class StockServiceTests
{
    [Theory]
    [InlineData(2025, 10, 4, 2026, 10, 4, true)] // Before the recorded source receipt.
    [InlineData(2028, 1, 1, 2028, 12, 31, false)] // After source expiration, but before today.
    [InlineData(2030, 1, 1, 2031, 1, 1, false)] // Future preparation.
    [InlineData(2025, 10, 4, 2025, 10, 3, false)] // Stock expiration before preparation.
    public async Task HistoricalEntryAllowsPreparationBeforeReceiptWhileKeepingOtherDateChecks(
        int year, int month, int day, int expirationYear, int expirationMonth, int expirationDay, bool allowed)
    {
        var source = StockCalculatorTests.Material("mg", 100, 150);
        var repository = new PreparationRepository(source);
        var clock = new FixedTimeProvider();
        var service = new StockService(repository, clock);
        var date = new DateOnly(year, month, day);
        var expiration = new DateOnly(expirationYear, expirationMonth, expirationDay);
        var request = new CreateStockRequest(Guid.NewGuid(), "Stock historico",
            StockCalculatorTests.Request(source) with { ActualWeight = 50 }, date, null,
            expiration, "Ambiente", "Analito", [new StockSolventInput(1, 100)], StockSolventComposition.Basis);

        var result = await service.CreateAsync(request, 42, CancellationToken.None);

        Assert.Equal(allowed, result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 1, 1), source.ReceivedDate);
        if (allowed)
        {
            Assert.NotNull(repository.Prepared);
            Assert.Equal(date, repository.Prepared.PreparationDate);
            Assert.Equal(expiration, repository.Prepared.ExpirationDate);
            Assert.Equal(50m, repository.Prepared.ActualWeight);
            Assert.Equal(42, repository.Prepared.PreparedByUserId);
        }
        else
        {
            Assert.Equal(ErrorCodes.ValidationError, result.Error?.Code);
            Assert.Null(repository.Prepared);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2029, 1, 1, 12, 0, 0, TimeSpan.Zero);
    }

    // Exercises the service callback used by the transactional repository; no database writes.
    private sealed class PreparationRepository(ReferenceMaterial source) : IStockRepository
    {
        public ReferencePreparation? Prepared { get; private set; }
        public Task<OperationResult<StockDetail>> CreateAsync(Guid requestId, string fingerprint,
            Func<ReferenceMaterial, long, OperationResult<ReferencePreparation>> prepare, Guid sourceId,
            DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var result = prepare(source, 1);
            Prepared = result.Value;
            return Task.FromResult(result.IsSuccess
                ? OperationResult.Success<StockDetail>(null!)
                : OperationResult.Failure<StockDetail>(result.Error!));
        }
        public Task<IReadOnlyList<SolventOption>> SolventsAsync(string? search, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StockSourcePage> SourcesAsync(string? search, DateOnly today, int page, int pageSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReferenceMaterial?> FindSourceAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StockPage> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken, PreparationListFilter? filter = null) => throw new NotSupportedException();
        public Task<StockDetail?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
