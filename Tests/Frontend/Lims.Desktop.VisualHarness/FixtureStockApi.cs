extern alias StockEngine;
using StockCalculator = StockEngine::Lims.Application.ReferencePreparations.StockCalculator;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Http;
using Lims.Domain.ReferenceMaterials;

namespace Lims.Desktop.VisualHarness;

/// <summary>Fictitious UI data; calculations use the production application engine.</summary>
internal sealed class FixtureStockApi : IStockApiClient
{
    public ReferenceMaterial Source { get; } = new(Guid.NewGuid(), "4-Aminobiphenyl · validación", "92-67-1", "CRM-VALIDACIÓN",
        new ReferenceMethod(1, "Aminas aromáticas", true), 99.11m, "G1151231", "Marca de prueba", new DateOnly(2026, 1, 1),
        new DateOnly(2027, 6, 24), 250m, new ReferenceUnit(3, "Miligramo", "mg", true), 1, "Ambiente",
        new ReferenceLocation(1, "Laboratorio", true), 1, DateTimeOffset.UtcNow);
    private readonly List<StockDetail> _prepared = [];
    public int CreateCalls { get; private set; }
    public Task<ApiCallResult<StockOptions>> OptionsAsync(CancellationToken cancellationToken) => Task.FromResult(Ok(new StockOptions(["mg/L", "g/L", "µg/L", "µg/mL"], ["mL", "L"])));
    public Task<ApiCallResult<StockSourcePage>> SourcesAsync(string? search, int page, CancellationToken cancellationToken) => Task.FromResult(Ok(new StockSourcePage([Snapshot()], page, 25, 1)));
    public Task<ApiCallResult<StockCalculation>> PreviewAsync(StockCalculationRequest request, CancellationToken cancellationToken) => Task.FromResult(Ok(StockCalculator.Calculate(Source, request)));
    public Task<ApiCallResult<StockDetail>> CreateAsync(CreateStockRequest request, CancellationToken cancellationToken)
    {
        CreateCalls++;
        if (_prepared.Find(item => item.Preparation.Id == request.RequestId) is { } previous) return Task.FromResult(Ok(previous));
        var source = Snapshot();
        var calculation = StockCalculator.Calculate(Source, request.Calculation);
        Source.Consume(calculation.ActualWeight!.Value, 1, DateTimeOffset.UtcNow, DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).DateTime));
        var code = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"STK-{request.PreparationDate:yyyyMMdd}-{_prepared.Count + 1:D3}");
        var summary = new StockSummary(request.RequestId, code, request.Name, source.Name, source.Lot,
            calculation.TargetConcentration, calculation.ActualConcentration!.Value, calculation.ConcentrationUnit,
            calculation.FinalVolume, calculation.FinalVolumeUnit, request.PreparationDate, "Usuario de prueba", "Active", calculation.ActualWeight!.Value, source.Unit, request.ExpirationDate);
        var detail = new StockDetail(summary, source, calculation, new(Guid.NewGuid(), calculation.ActualWeight!.Value, source.Unit,
            source.AvailableQuantity, Source.AvailableQuantity, "Usuario de prueba", DateTimeOffset.UtcNow), request.Notes,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid(), request.StorageTemperature);
        _prepared.Add(detail);
        return Task.FromResult(Ok(detail));
    }
    public Task<ApiCallResult<StockPage>> ListAsync(string? search, int page, CancellationToken cancellationToken) => Task.FromResult(Ok(new StockPage(_prepared.Select(item => item.Preparation).ToArray(), page, 25, _prepared.Count)));
    public Task<ApiCallResult<StockDetail>> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Ok(_prepared.Single(item => item.Preparation.Id == id)));
    private StockSource Snapshot() => new(Source.Id, Source.Name, Source.CasNumber, Source.CatalogNumber, Source.Lot, Source.Brand,
        Source.Method.Name, Source.PurityPercent, Source.AvailableQuantity, Source.TotalQuantity, Source.Unit.Symbol, Source.ExpirationDate,
        Source.Location.Name, Source.Version);
    private static ApiCallResult<T> Ok<T>(T value) => new(true, value, null, 200);
}
