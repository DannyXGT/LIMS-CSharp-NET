namespace Lims.Domain.ReferencePreparations;

/// <summary>Immutable source and per-analyte snapshots, independent of future source changes.</summary>
public sealed class ReferencePreparationComponent
{
    private ReferencePreparationComponent() { }
    public ReferencePreparationComponent(Guid preparationId, ReferencePreparation source, int position,
        decimal volume, string unit, string snapshotJson, DateTimeOffset now)
    {
        if (preparationId == Guid.Empty || volume <= 0 || position < 0) throw new ArgumentException("Componente inválido.");
        Id = Guid.NewGuid(); PreparationId = preparationId; SourcePreparationId = source.Id;
        SourceType = source.Kind; SourceVersion = source.Version; Position = position;
        VolumeTaken = volume; VolumeUnit = unit; SourceSnapshotJson = snapshotJson; CreatedAt = now;
    }
    public Guid Id { get; private set; }
    public Guid PreparationId { get; private set; }
    public Guid SourcePreparationId { get; private set; }
    public string SourceType { get; private set; } = string.Empty;
    public Guid SourceVersion { get; private set; }
    public int Position { get; private set; }
    public decimal VolumeTaken { get; private set; }
    public string VolumeUnit { get; private set; } = string.Empty;
    public string SourceSnapshotJson { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
