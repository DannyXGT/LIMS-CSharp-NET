using Lims.Contracts.ReferencePreparations;
using Lims.Domain.ReferenceMaterials;

namespace Lims.Application.ReferencePreparations;

public static class StockCalculator
{
    public const string MassFormula = "Peso a tomar = C × V ÷ pureza; concentración real = peso tomado × pureza ÷ V (unidades convertidas)";
    public static StockCalculation Calculate(ReferenceMaterial source, StockCalculationRequest request)
    {
        ArgumentNullException.ThrowIfNull(source);
        return StockUnits.Calculate(source.PurityPercent ?? throw new ArgumentException("La preparación por pesada requiere un s?lido con pureza documentada."), source.AvailableQuantity, source.Unit.Symbol, request);
    }
}
