namespace Lims.Domain.ReferenceMaterials;

/// <summary>A functional entry pointing at a real bottle. This entity owns no inventory.</summary>
public sealed class ReferenceCommercialRole
{
    private ReferenceCommercialRole() { }
    public ReferenceCommercialRole(Guid materialId, string kind, string code, Guid requestId, string fingerprint,
        int actor, DateTimeOffset now)
    {
        if (materialId == Guid.Empty || requestId == Guid.Empty || kind is not ("Stock" or "Intermedia") ||
            string.IsNullOrWhiteSpace(code) || fingerprint.Length != 64 || actor <= 0) throw new ArgumentException("Ingreso comercial inválido.");
        MaterialId = materialId; Kind = kind; Code = code; RequestId = requestId;
        RequestFingerprint = fingerprint; RegisteredByUserId = actor; RegisteredAt = now;
    }
    public Guid MaterialId { get; private set; }
    public ReferenceMaterial Material { get; private set; } = null!;
    public string Kind { get; private set; } = "";
    public string Code { get; private set; } = "";
    public Guid RequestId { get; private set; }
    public string RequestFingerprint { get; private set; } = "";
    public int RegisteredByUserId { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; }
}
