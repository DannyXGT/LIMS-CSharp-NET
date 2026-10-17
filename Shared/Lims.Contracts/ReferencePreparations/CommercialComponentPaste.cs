using System.Globalization;
using Lims.Contracts.ReferenceMaterials;

namespace Lims.Contracts.ReferencePreparations;

public static class CommercialComponentPaste
{
    public static IReadOnlyList<CertifiedComponent> Parse(string text)
    {
        var results = new List<CertifiedComponent>();
        foreach (var line in text.Replace("\r", "", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\t').Select(f => f.Trim()).ToArray();
            if (results.Count == 0 && fields.Length >= 3 && fields[0].Equals("Compuesto", StringComparison.OrdinalIgnoreCase)) continue;
            if (fields.Length != 4 || fields[0].Length == 0 ||
                !decimal.TryParse(fields[2].Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var concentration) || concentration <= 0)
                throw new ArgumentException("Pegue cuatro columnas: Compuesto, CAS, Concentración y Unidad. Revise todas las filas antes de importar.");
            _ = StockUnits.MilligramsPerLiter(fields[3]);
            if (fields[1].Length > 0 && !ValidCas(fields[1])) throw new ArgumentException($"CAS inválido en {fields[0]}.");
            results.Add(new(Guid.NewGuid(), fields[0], fields[1].Length == 0 ? null : fields[1], concentration, fields[3]));
        }
        if (results.Count == 0) throw new ArgumentException("No se encontraron componentes para pegar.");
        return results;
    }
    private static bool ValidCas(string value)
    {
        var parts = value.Split('-');
        if (parts.Length != 3 || parts[0].Length is < 2 or > 7 || parts[1].Length != 2 || parts[2].Length != 1 || parts.Any(p => !p.All(char.IsAsciiDigit))) return false;
        var digits = parts[0] + parts[1];
        return digits.Reverse().Select((c, i) => (c - '0') * (i + 1)).Sum() % 10 == parts[2][0] - '0';
    }
}
