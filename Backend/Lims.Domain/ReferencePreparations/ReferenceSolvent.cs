namespace Lims.Domain.ReferencePreparations;

public sealed class ReferenceSolvent
{
    private ReferenceSolvent() { }
    public ReferenceSolvent(int id, string name, string abbreviation, bool isActive, int displayOrder)
    { Id = id; Name = name; Abbreviation = abbreviation; IsActive = isActive; DisplayOrder = displayOrder; }
    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Abbreviation { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public int DisplayOrder { get; private set; }
}

public sealed class StockSolvent
{
    private StockSolvent() { }
    public StockSolvent(Guid preparationId, int solventId, decimal percent, int position)
    {
        if (preparationId == Guid.Empty || solventId <= 0 || percent <= 0 || percent > 100 || decimal.Round(percent, 4) != percent || position < 0)
            throw new ArgumentException("La proporción del solvente es inválida.");
        Id = Guid.NewGuid(); PreparationId = preparationId; SolventId = solventId; Percent = percent; Position = position;
    }
    public Guid Id { get; private set; }
    public Guid PreparationId { get; private set; }
    public int SolventId { get; private set; }
    public ReferenceSolvent Solvent { get; private set; } = null!;
    public decimal Percent { get; private set; }
    public int Position { get; private set; }
}
