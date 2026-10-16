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
    public string Scene { get; set; } = "Default";
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
            calculation.FinalVolume, calculation.FinalVolumeUnit, request.PreparationDate, "Usuario de prueba", "Active", calculation.ActualWeight!.Value, source.Unit, request.ExpirationDate, calculation.FinalVolume);
        var detail = new StockDetail(summary, source, calculation, new(Guid.NewGuid(), calculation.ActualWeight!.Value, source.Unit,
            source.AvailableQuantity, Source.AvailableQuantity, "Usuario de prueba", DateTimeOffset.UtcNow), request.Notes,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid(), request.StorageTemperature);
        _prepared.Add(detail);
        return Task.FromResult(Ok(detail));
    }
    public async Task<ApiCallResult<StockPage>> ListAsync(string? search, int page, CancellationToken cancellationToken, PreparationListFilter? filter = null)
    {
        if (Scene == "Loading") await Task.Delay(1800, cancellationToken);
        if (Scene == "Error") throw new HttpRequestException("Fixture: server unavailable");
        IEnumerable<StockSummary> rows = Scene == "Empty" ? [] : _prepared.Select(d => ForScene(d.Preparation));
        if (!string.IsNullOrWhiteSpace(search)) rows = rows.Where(p => p.Code.Contains(search, StringComparison.OrdinalIgnoreCase) || p.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || p.SourceName.Contains(search, StringComparison.OrdinalIgnoreCase) || p.SourceLot.Contains(search, StringComparison.OrdinalIgnoreCase));
        if (filter?.Status is { Length: > 0 } status) rows = rows.Where(p => status == "Active" ? p.Status is "Active" or "Depleted" : p.Status == status);
        if (filter?.Availability is { Length: > 0 } availability) rows = rows.Where(p => availability switch { "Positive" => p.Availability.AvailableVolume > 0, "Low" => p.Availability.RemainingPercent is > 0 and <= 60, "Depleted" => p.Availability.IsDepleted, _ => true });
        if (!string.IsNullOrWhiteSpace(filter?.Method)) rows = rows.Where(p => "Aminas aromáticas".Contains(filter.Method, StringComparison.OrdinalIgnoreCase));
        var all = rows.ToArray();
        return Ok(new StockPage(all.Skip((page - 1) * 25).Take(25).ToArray(), page, 25, all.Length, true));
    }

    public Task<ApiCallResult<StockDetail>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var detail = _prepared.Single(item => item.Preparation.Id == id);
        return Task.FromResult(Ok(detail with { Preparation = ForScene(detail.Preparation), UsageHistory = [] }));
    }
    private StockSummary ForScene(StockSummary row) => Scene switch
    {
        "Low" => row with { AvailableVolume = row.FinalVolume * 0.5m },
        "Depleted" => row with { AvailableVolume = 0 },
        "Expired" => row with { Status = "Expired", ExpirationDate = new(2026, 1, 1), AvailableVolume = row.FinalVolume * 0.75m },
        "Unknown" => row with { AvailableVolume = null },
        _ => row
    };
    private StockSource Snapshot() => new(Source.Id, Source.Name, Source.CasNumber, Source.CatalogNumber, Source.Lot, Source.Brand,
        Source.Method.Name, Source.PurityPercent, Source.AvailableQuantity, Source.TotalQuantity, Source.Unit.Symbol, Source.ExpirationDate,
        Source.Location.Name, Source.Version);
    private static ApiCallResult<T> Ok<T>(T value) => new(true, value, null, 200);
}
