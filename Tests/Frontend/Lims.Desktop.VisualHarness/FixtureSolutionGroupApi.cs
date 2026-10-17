using Lims.Contracts.Errors;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Http;

namespace Lims.Desktop.VisualHarness;

/// <summary>Only UI fixtures. Shares the Intermediate fixture's volume store; no network or database.</summary>
internal sealed class FixtureSolutionGroupApi : ISolutionGroupApiClient
{
    private readonly FixtureIntermediateApi _intermediate;
    private readonly List<SolutionGroupDetail> _groups = [];
    public int CreateCalls { get; private set; }
    public string Scene { get; set; } = "Default";
    public FixtureSolutionGroupApi(FixtureIntermediateApi intermediate)
    {
        _intermediate = intermediate;
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).Date);
        var stocks = intermediate.Sources.Where(s => s.Kind == "Stock").ToArray();
        intermediate.CreateAsync(new(Guid.NewGuid(), "Intermedia multianalito · validación visual",
            new(1, 100, "mL", stocks.Select(s => new IntermediateComponentRequest(s.Id, s.Version, 10, "mL")).ToArray()),
            today, today.AddDays(200), "Fixture visual; sin persistencia real"), default).GetAwaiter().GetResult();
        intermediate.CreateAsync(new(Guid.NewGuid(), "Intermedia independiente para estándar interno · referencia de nombre extenso para comprobar ajuste y consulta de trazabilidad",
            new(1, 100, "mL", intermediate.Sources.Where(s => s.Kind == "Stock").Select(s => new IntermediateComponentRequest(s.Id, s.Version, 5, "mL")).ToArray()),
            today, today.AddDays(200), "Segundo origen exclusivamente fixture visual"), default).GetAwaiter().GetResult();
    }
    public Task<ApiCallResult<IntermediateOptions>> OptionsAsync(string kind, CancellationToken cancellationToken) => Task.FromResult(Ok(new IntermediateOptions([new(1, "Aminas aromáticas")], _intermediate.Sources.Where(s => s.Kind == "Intermedia" && s.AvailableVolume > 0).ToArray())));
    public Task<ApiCallResult<SolutionCalculation>> PreviewAsync(string kind, SolutionCalculationRequest request, CancellationToken cancellationToken) => Task.FromResult(Ok(SolutionGroupCalculator.Calculate(request, _intermediate.Sources)));
    public Task<ApiCallResult<SolutionGroupDetail>> CreateAsync(string kind, CreateSolutionGroupRequest request, CancellationToken cancellationToken)
    {
        CreateCalls++;
        if (Scene == "CreateError") return Task.FromResult(new ApiCallResult<SolutionGroupDetail>(false, null, new(ErrorCodes.InvalidState, "El origen cambió. Actualice su disponibilidad.", "VISUAL-409"), 409));
        if (_groups.Find(g => g.Preparation.Id == request.RequestId) is { } previous) return Task.FromResult(Ok(previous));
        var calculation = SolutionGroupCalculator.Calculate(request.Calculation, _intermediate.Sources);
        if (!calculation.HasSufficientVolume) return Task.FromResult(new ApiCallResult<SolutionGroupDetail>(false, null, new(ErrorCodes.InvalidState, "Saldo insuficiente.", ""), 409));
        var now = DateTimeOffset.UtcNow;
        var movements = calculation.Debits.Select(d => new IntermediateConsumption(Guid.NewGuid(), d.Source.Id, d.Source.Code, d.Quantity, d.Source.VolumeUnit, d.Source.AvailableVolume, d.RemainingVolume, "Usuario de prueba", now)).ToArray();
        foreach (var d in calculation.Debits)
        {
            var index = _intermediate.Sources.FindIndex(s => s.Id == d.Source.Id);
            _intermediate.Sources[index] = d.Source with { AvailableVolume = d.RemainingVolume, Version = Guid.NewGuid() };
        }
        var summary = new SolutionGroupSummary(request.RequestId, kind, request.Code, request.Name ?? request.Code, "Aminas aromáticas", calculation.Parts.Count,
            string.Join(" · ", calculation.Debits.Select(d => d.Source.Code)), request.PreparationDate, request.ExpirationDate, "Usuario de prueba", "Active");
        var result = new SolutionGroupDetail(summary, calculation, movements, request.Notes, request.StorageTemperature, now, Guid.NewGuid());
        _groups.Add(result); return Task.FromResult(Ok(result));
    }
    public async Task<ApiCallResult<SolutionGroupPage>> ListAsync(string kind, string? search, int page, CancellationToken cancellationToken, PreparationListFilter? filter = null)
    {
        if (Scene == "Error") throw new HttpRequestException("Fixture connection failure");
        if (Scene == "Loading") await Task.Delay(1800, cancellationToken);
        var rows = _groups.Where(g => g.Preparation.Kind == kind).Select(g => g.Preparation);
        if (!string.IsNullOrWhiteSpace(search)) rows = rows.Where(p => (p.Code + p.Name + p.Method + p.Origins).Contains(search, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(filter?.Status)) rows = rows.Where(p => p.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter?.Method)) rows = rows.Where(p => p.Method.Contains(filter.Method, StringComparison.OrdinalIgnoreCase));
        var all = rows.ToArray(); return Ok(new SolutionGroupPage(all.Skip((page - 1) * 25).Take(25).ToArray(), page, 25, all.Length));
    }
    public Task<ApiCallResult<SolutionGroupDetail>> GetAsync(string kind, Guid id, CancellationToken cancellationToken) => Task.FromResult(Ok(_groups.Single(g => g.Preparation.Id == id && g.Preparation.Kind == kind)));
    private static ApiCallResult<T> Ok<T>(T value) => new(true, value, null, 200);
}
