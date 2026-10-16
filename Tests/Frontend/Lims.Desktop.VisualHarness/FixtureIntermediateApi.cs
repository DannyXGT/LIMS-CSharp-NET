using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Http;

namespace Lims.Desktop.VisualHarness;

/// <summary>Fictitious sources and persistence for real WinUI views. No database or production claims.</summary>
internal sealed class FixtureIntermediateApi : IIntermediateApiClient
{
    public List<IntermediateSource> Sources { get; } = [Source("4-Aminobiphenyl", "92-67-1", 1, 1000), Source("Benzidine", "92-87-5", 2, 500), Source("2-Naphthylamine", "91-59-8", 3, 200)];
    private readonly List<IntermediateDetail> _prepared = [];
    public string Scene { get; set; } = "Default";
    public int CreateCalls { get; private set; }
    public Task<ApiCallResult<IntermediateOptions>> OptionsAsync(CancellationToken cancellationToken) => Task.FromResult(Ok(new IntermediateOptions([new(1, "Aminas aromáticas"), new(2, "Otro método · prueba")], Sources.ToArray())));
    public Task<ApiCallResult<IntermediateCalculation>> PreviewAsync(IntermediateCalculationRequest request, CancellationToken cancellationToken) => Task.FromResult(Ok(IntermediateCalculator.Calculate(request, Sources)));
    public Task<ApiCallResult<IntermediateDetail>> CreateAsync(CreateIntermediateRequest request, CancellationToken cancellationToken)
    {
        CreateCalls++;
        if (_prepared.Find(d => d.Preparation.Id == request.RequestId) is { } previous) return Task.FromResult(Ok(previous));
        var calculation = IntermediateCalculator.Calculate(request.Calculation, Sources);
        if (calculation.ExceedsFinalVolume || !calculation.HasSufficientVolume)
            return Task.FromResult(new ApiCallResult<IntermediateDetail>(false, null, new(ErrorCodes.InvalidState, "Revise disponibilidad y aforo.", ""), 409));
        var now = DateTimeOffset.UtcNow;
        var consumptions = calculation.Components.Select(c => new IntermediateConsumption(Guid.NewGuid(), c.Source.Id, c.Source.Code,
            StockUnits.ConvertVolume(c.VolumeTaken, c.VolumeUnit, c.Source.VolumeUnit), c.Source.VolumeUnit,
            c.Source.AvailableVolume, c.RemainingVolume, "Usuario de prueba", now)).ToArray();
        foreach (var c in calculation.Components)
        {
            var index = Sources.FindIndex(s => s.Id == c.Source.Id); Sources[index] = Sources[index] with { AvailableVolume = c.RemainingVolume, Version = Guid.NewGuid() };
        }
        var code = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"INT-{request.PreparationDate:yyyyMMdd}-{_prepared.Count + 1:D3}");
        var summary = new IntermediateSummary(request.RequestId, code, request.Name, "Aminas aromáticas", calculation.Components.Count,
            calculation.FinalVolume, calculation.VolumeUnit, calculation.FinalVolume, calculation.Results, request.PreparationDate,
            request.ExpirationDate, "Usuario de prueba", "Active");
        var detail = new IntermediateDetail(summary, calculation, consumptions, request.Calculation.DilutedPreparationId, request.Notes, now, now, "Usuario de prueba", Guid.NewGuid());
        _prepared.Add(detail);
        Sources.Add(new(summary.Id, "Intermedia", code, summary.Name, 1, summary.Method, summary.AvailableVolume, summary.FinalVolume, summary.VolumeUnit,
            summary.PreparationDate, summary.ExpirationDate, detail.Version, summary.Results));
        return Task.FromResult(Ok(detail));
    }
    public async Task<ApiCallResult<IntermediatePage>> ListAsync(string? search, int page, CancellationToken cancellationToken, PreparationListFilter? filter = null)
    {
        if (Scene == "Loading") await Task.Delay(1800, cancellationToken);
        if (Scene == "Error") throw new HttpRequestException("Fixture: server unavailable");
        IEnumerable<IntermediateSummary> rows = Scene == "Empty" ? [] : _prepared.Select(d => d.Preparation);
        if (!string.IsNullOrWhiteSpace(search)) rows = rows.Where(p => p.Code.Contains(search, StringComparison.OrdinalIgnoreCase) || p.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || p.Method.Contains(search, StringComparison.OrdinalIgnoreCase));
        if (filter?.Status is { Length: > 0 } status) rows = rows.Where(p => status == "Active" ? p.Status is "Active" or "Depleted" : p.Status == status);
        if (filter?.Availability is { Length: > 0 } availability) rows = rows.Where(p => availability switch { "Positive" => p.Availability.AvailableVolume > 0, "Low" => p.Availability.RemainingPercent is > 0 and <= 60, "Depleted" => p.Availability.IsDepleted, _ => true });
        if (!string.IsNullOrWhiteSpace(filter?.Method)) rows = rows.Where(p => p.Method.Contains(filter.Method, StringComparison.OrdinalIgnoreCase));
        var all = rows.ToArray();
        return Ok(new IntermediatePage(all.Skip((page - 1) * 25).Take(25).ToArray(), page, 25, all.Length, true));
    }

    public Task<ApiCallResult<IntermediateDetail>> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Ok(_prepared.Single(d => d.Preparation.Id == id)));
    private static IntermediateSource Source(string name, string cas, int suffix, decimal concentration) => new(Guid.NewGuid(), "Stock", $"STK-20261007-{suffix:000}", name,
        1, "Aminas aromáticas", 200, 200, "mL", new(2026, 1, 1), new(2027, 6, 24), Guid.NewGuid(), [new(Guid.NewGuid(), name, cas, $"LOTE-VALIDACIÓN-{suffix}", concentration, "mg/L")]);
    private static ApiCallResult<T> Ok<T>(T value) => new(true, value, null, 200);
}
