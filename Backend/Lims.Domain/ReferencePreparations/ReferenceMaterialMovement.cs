namespace Lims.Domain.ReferencePreparations;

/// <summary>An immutable debit linked to its preparation, actor, source and balance.</summary>
public sealed class ReferenceMaterialMovement
{
    private ReferenceMaterialMovement() { }
    public ReferenceMaterialMovement(ReferencePreparation preparation, decimal balanceBefore, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        if (balanceBefore < preparation.ActualWeight) throw new ArgumentException("No hay suficiente material disponible.", nameof(balanceBefore));
        Id = Guid.NewGuid();
        PreparationId = preparation.Id;
        Preparation = preparation;
        SourceMaterialId = preparation.SourceMaterialId;
        Quantity = preparation.ActualWeight;
        Unit = preparation.SourceUnit;
        BalanceBefore = balanceBefore;
        BalanceAfter = balanceBefore - Quantity;
        ActorUserId = preparation.PreparedByUserId;
        OccurredAt = now;
    }
    public Guid Id { get; private set; }
    public Guid PreparationId { get; private set; }
    public ReferencePreparation Preparation { get; private set; } = null!;
    public Guid SourceMaterialId { get; private set; }
    public string Kind { get; private set; } = "StockConsumption";
    public decimal Quantity { get; private set; }
    public string Unit { get; private set; } = string.Empty;
    public decimal BalanceBefore { get; private set; }
    public decimal BalanceAfter { get; private set; }
    public int ActorUserId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}
