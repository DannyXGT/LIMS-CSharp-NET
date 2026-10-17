using Lims.Contracts.ReferenceMaterials;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Http;

namespace Lims.Desktop.VisualHarness;

// In-memory UI evidence only. Physical persistence and atomic consumption are tested in PostgreSQL.
internal sealed class FixtureCommercialPreparationApi(FixtureApi materials, FixtureIntermediateApi intermediates) : ICommercialPreparationApiClient
{
    public List<CommercialPreparationDetail> Roles { get; } = [];
    public RegisterCommercialPreparationRequest? LastRequest { get; private set; }
    public async Task<ApiCallResult<CommercialPreparationOptions>> OptionsAsync(string kind, string? search, CancellationToken ct)
    {
        var page = (await materials.ListAsync(search, null, null, 1, 100, ct)).Value!;
        var rows = new List<ReferenceMaterialDetail>();
        foreach (var row in page.Items.Where(m => MaterialPresentations.IsCommercial(m.Presentation) && (kind != "Stock" || m.Presentation == MaterialPresentations.Solution)))
            rows.Add((await materials.GetAsync(row.Id, ct)).Value!);
        return Ok(new CommercialPreparationOptions((await materials.GetMethodsAsync(ct)).Value!, (await materials.GetUnitsAsync(ct)).Value!, (await materials.GetLocationsAsync(ct)).Value!, rows));
    }
    public async Task<ApiCallResult<CommercialPreparationDetail>> RegisterAsync(string kind, RegisterCommercialPreparationRequest request, CancellationToken ct)
    {
        LastRequest = request;
        var material = request.NewMaterial is { } input ? (await materials.CreateAsync(input, ct)).Value! : (await materials.GetAsync(request.ExistingMaterialId!.Value, ct)).Value!;
        var existing = Roles.Find(r => r.Kind == kind && r.Material.Id == material.Id);
        if (existing is not null) return Ok(existing);
        var role = new CommercialPreparationDetail(kind, $"{(kind == "Stock" ? "STK" : "INT")}-{material.ReceivedDate:yyyyMMdd}-{Roles.Count + 101:D3}", material, "Usuario de prueba", DateTimeOffset.UtcNow);
        Roles.Add(role);
        if (intermediates.Sources.All(s => s.Id != material.Id))
            intermediates.Sources.Add(new(material.Id, material.Presentation, role.Code, material.Name, material.MethodId, material.Method,
                material.AvailableQuantity!.Value, material.TotalQuantity, material.Unit, material.ReceivedDate, material.ExpirationDate, material.Version,
                material.Commercial!.Components.Select(c => new IntermediateAnalyte(c.Id, c.Name, c.CasNumber, material.Lot, c.CertifiedConcentration, c.ConcentrationUnit, material.Id, c.Id)).ToArray()));
        return Ok(role);
    }
    public async Task<ApiCallResult<CommercialPreparationDetail>> GetAsync(string kind, Guid id, CancellationToken ct)
        => Ok(Roles.Single(r => r.Kind == kind && r.Material.Id == id) with { Material = (await materials.GetAsync(id, ct)).Value! });
    public IEnumerable<StockSummary> Stocks => Roles.Where(r => r.Kind == "Stock").Select(r =>
    {
        var m = r.Material; var c = m.Commercial!.Components[0];
        return new StockSummary(m.Id, r.Code, m.Name, "Comercial / comprado", m.Lot, null, c.CertifiedConcentration, c.ConcentrationUnit,
            m.TotalQuantity, m.Unit, m.ReceivedDate, r.RegisteredBy, m.Status, null, "", m.ExpirationDate, m.AvailableQuantity, true);
    });
    public IEnumerable<IntermediateSummary> Intermediates => Roles.Where(r => r.Kind == "Intermedia").Select(r =>
    {
        var m = r.Material;
        return new IntermediateSummary(m.Id, r.Code, m.Name, m.Method, m.Commercial!.Components.Count, m.TotalQuantity, m.Unit,
            m.AvailableQuantity!.Value, intermediates.Sources.Single(s => s.Id == m.Id).Analytes, m.ReceivedDate, m.ExpirationDate, r.RegisteredBy, m.Status, true);
    });
    private static ApiCallResult<T> Ok<T>(T value) => new(true, value, null, 200);
}
