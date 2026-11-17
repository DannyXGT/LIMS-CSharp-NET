using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Lims.Application.Planning;

public sealed record SpreadsheetCell(string Value, string Type, bool Formula);
public sealed record SpreadsheetRow(int Number, IReadOnlyDictionary<int, SpreadsheetCell> Cells);
public sealed record SpreadsheetTable(string Sheet, bool Date1904, IReadOnlyList<string> Headers, List<SpreadsheetRow> Rows);

/// <summary>Bounded, read-only SpreadsheetML reader. No Office, macros, formulas or external resource access.</summary>
public static class XlsxTableReader
{
    public const int MaxRows = 50000;
    private const int MaxColumns = 128;
    private const int MaxText = 16384;
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XmlReaderSettings Settings = new()
    { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64L * 1024 * 1024 };

    public static SpreadsheetTable Read(byte[] bytes, IReadOnlyList<string> requiredHeaders, CancellationToken ct = default)
    {
        if (bytes.Length == 0 || bytes.Length > PlanningLimits.MaxFileBytes) throw new InvalidDataException("Tamaño XLSX inválido (máximo 5 MiB).");
        using var stream = new MemoryStream(bytes, false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count > 200 || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.Ordinal).Count() != zip.Entries.Count ||
            zip.Entries.Any(e => e.Length > 64L * 1024 * 1024) || zip.Entries.Sum(e => e.Length) > 100L * 1024 * 1024)
            throw new InvalidDataException("El contenido expandido del XLSX excede los límites o tiene partes duplicadas.");
        if (zip.Entries.Any(e => e.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("No se admiten macros.");
        var workbook = Xml(zip, "xl/workbook.xml");
        var date1904 = workbook.Descendants(Ns + "workbookPr").FirstOrDefault()?.Attribute("date1904")?.Value is "1" or "true";
        var relationships = Xml(zip, "xl/_rels/workbook.xml.rels").Root!.Elements().ToDictionary(e => (string)e.Attribute("Id")!);
        var strings = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is { } shared)
        {
            using var input = shared.Open(); using var reader = XmlReader.Create(input, Settings);
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "si" || reader.NamespaceURI != Ns.NamespaceName) continue;
                using var subtree = reader.ReadSubtree();
                var element = XElement.Load(subtree);
                var text = string.Concat(element.Descendants(Ns + "t").Where(e => !e.Ancestors(Ns + "rPh").Any()).Select(e => e.Value));
                if (text.Length > MaxText || strings.Count >= 200000) throw new InvalidDataException("La tabla de textos excede los límites.");
                strings.Add(text);
            }
        }
        var candidates = new List<SpreadsheetTable>();
        foreach (var sheet in workbook.Descendants(Ns + "sheet"))
        {
            ct.ThrowIfCancellationRequested();
            var id = (string?)sheet.Attribute(XName.Get("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"));
            if (id is null || !relationships.TryGetValue(id, out var rel) || (string?)rel.Attribute("TargetMode") == "External")
                throw new InvalidDataException("Relación de hoja inválida o externa.");
            var target = (string?)rel.Attribute("Target") ?? throw new InvalidDataException("Hoja sin ruta.");
            var part = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
            if (part.Split('/').Any(s => s is "." or "..") || part.Contains('\\') || !part.StartsWith("xl/worksheets/", StringComparison.Ordinal))
                throw new InvalidDataException("Ruta de hoja no admitida.");
            using var input = Entry(zip, part).Open(); using var reader = XmlReader.Create(input, Settings);
            string[]? headers = null; var rows = new List<SpreadsheetRow>(); var previousRow = 0; var scanned = 0;
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "row" || reader.NamespaceURI != Ns.NamespaceName) continue;
                if (++scanned > MaxRows + 20) throw new InvalidDataException("Máximo 50000 registros por hoja.");
                using var subtree = reader.ReadSubtree(); var row = XElement.Load(subtree);
                if (!int.TryParse((string?)row.Attribute("r"), out var number) || number <= previousRow || number > 1048576)
                    throw new InvalidDataException("Coordenadas de fila inválidas o repetidas.");
                previousRow = number;
                var cells = new Dictionary<int, SpreadsheetCell>();
                foreach (var cell in row.Elements(Ns + "c"))
                {
                    var reference = (string?)cell.Attribute("r") ?? throw new InvalidDataException("Celda sin coordenada.");
                    var col = 0; var i = 0;
                    while (i < reference.Length && reference[i] is >= 'A' and <= 'Z') { col = checked(col * 26 + reference[i] - 'A' + 1); i++; }
                    if (col is < 1 or > MaxColumns || !int.TryParse(reference.AsSpan(i), out var cellRow) || cellRow != number || cells.ContainsKey(col - 1))
                        throw new InvalidDataException("Coordenadas de celda inválidas o repetidas.");
                    var type = (string?)cell.Attribute("t") ?? "n";
                    var value = cell.Element(Ns + "v")?.Value ?? "";
                    if (type == "s")
                    {
                        if (!int.TryParse(value, out var index) || index < 0 || index >= strings.Count) throw new InvalidDataException("Índice de texto inválido.");
                        value = strings[index];
                    }
                    else if (type == "inlineStr") value = string.Concat(cell.Descendants(Ns + "t").Select(e => e.Value));
                    if (value.Length > MaxText) throw new InvalidDataException("El texto de una celda excede el límite.");
                    cells.Add(col - 1, new(value, type, cell.Element(Ns + "f") is not null));
                }
                if (cells.Values.All(c => string.IsNullOrWhiteSpace(c.Value))) continue;
                if (headers is null)
                {
                    if (scanned > 20) break;
                    var values = Enumerable.Range(0, cells.Keys.Max() + 1).Select(i => cells.GetValueOrDefault(i)?.Value.Trim() ?? "").ToArray();
                    if (requiredHeaders.All(h => values.Contains(h, StringComparer.Ordinal)))
                    {
                        if (cells.Values.Any(c => c.Formula) || values.Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).Count() != values.Count(v => v.Length > 0))
                            throw new InvalidDataException("Encabezados repetidos o calculados.");
                        headers = values;
                    }
                }
                else
                {
                    if (rows.Count >= MaxRows) throw new InvalidDataException("Máximo 50000 registros por hoja.");
                    rows.Add(new(number, cells));
                }
            }
            var sheetName = (string?)sheet.Attribute("name") ?? "Hoja";
            if (sheetName.Length > 128) throw new InvalidDataException("Nombre de hoja demasiado largo.");
            if (headers is not null) candidates.Add(new(sheetName, date1904, headers, rows));
        }
        if (candidates.Count != 1) throw new InvalidDataException(candidates.Count == 0 ? "No se encontró una hoja con los encabezados requeridos: " + string.Join(", ", requiredHeaders) : "Más de una hoja coincide; el archivo es ambiguo.");
        if (candidates[0].Rows.Count == 0) throw new InvalidDataException("La hoja no contiene registros.");
        return candidates[0];
    }
    public static DateOnly? Date(SpreadsheetCell? cell, bool date1904)
    {
        if (cell is null || string.IsNullOrWhiteSpace(cell.Value)) return null;
        if (cell.Formula || cell.Type == "e") throw new FormatException("Fecha calculada o con error.");
        if (cell.Type == "n" && double.TryParse(cell.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
        {
            if (!double.IsFinite(serial) || serial < (date1904 ? 0 : 1) || (!date1904 && Math.Floor(serial) == 60)) throw new FormatException("Serial de fecha inválido.");
            try { return DateOnly.FromDateTime(date1904 ? new DateTime(1904, 1, 1).AddDays(serial) : DateTime.FromOADate(serial < 60 ? serial + 1 : serial)); }
            catch (ArgumentException) { throw new FormatException("Fecha fuera de rango."); }
        }
        if (DateOnly.TryParseExact(cell.Value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        throw new FormatException("Fecha no admitida; se requiere serial Excel o yyyy-MM-dd.");
    }
    private static ZipArchiveEntry Entry(ZipArchive zip, string name) => zip.GetEntry(name) ?? throw new InvalidDataException("Falta una parte requerida del XLSX.");
    private static XDocument Xml(ZipArchive zip, string name)
    { using var input = Entry(zip, name).Open(); using var reader = XmlReader.Create(input, Settings); return XDocument.Load(reader); }
}
