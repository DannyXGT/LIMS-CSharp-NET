using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using Lims.Contracts.Planning;

namespace Lims.Application.Planning;

public static class PlanningImportReader
{
    public static PlanningImportInput Read(string summaryName, byte[] summary, string reportName, byte[] report, ImportCut cut, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cut);
        if (cut.Window is not ("First" or "Noon" or "Last")) throw new PlanningValidationException("Seleccione primera carga, mediodía o última carga.");
        var issues = new List<ImportIssue>();
        var rows = new List<PlanningSourceRow>();
        var files = new List<FileCoverage>();
        ReadFile("Summary", summaryName, summary, ["Report #", "Reg Date", "Delivery Date"]);
        ReadFile("Report List", reportName, report, ["Report_no", "Test_Name"]);
        var fingerprint = Hash(Encoding.UTF8.GetBytes($"{PlanningLimits.ReaderProfile}|{cut.Window}|{cut.Day:yyyy-MM-dd}|{Hash(summary)}|{Hash(report)}"));
        return new(cut, summary, report, Path.GetFileName(summaryName), Path.GetFileName(reportName), fingerprint, files, rows, issues);

        void ReadFile(string file, string name, byte[] bytes, string[] headers)
        {
            try
            {
                var table = XlsxTableReader.Read(bytes, headers, ct);
                var columns = table.Headers.Select((h, i) => (h, i)).Where(x => x.h.Length > 0).ToDictionary(x => x.h, x => x.i, StringComparer.OrdinalIgnoreCase);
                var dates = new List<DateOnly>();
                var unverifiedUsd = 0; var externalClosed = 0;
                foreach (var row in table.Rows)
                {
                    ct.ThrowIfCancellationRequested();
                    string? Cell(string header, int max = 2048)
                    {
                        if (!columns.TryGetValue(header, out var index)) return null;
                        var cell = row.Cells.GetValueOrDefault(index);
                        var value = cell?.Value.Trim();
                        if (cell?.Formula == true || cell?.Type == "e" || value?.Length > max || value?.Any(ch=>char.IsControl(ch)&&ch is not ('\r' or '\n' or '\t')) == true)
                            throw new InvalidDataException($"{file}, fila {row.Number}: campo {header} calculado, inválido o demasiado largo.");
                        return string.IsNullOrEmpty(value) ? null : value;
                    }
                    DateOnly? Date(string header)
                    {
                        if (!columns.TryGetValue(header, out var index)) return null;
                        return XlsxTableReader.Date(row.Cells.GetValueOrDefault(index), table.Date1904);
                    }
                    var job = Cell(file == "Summary" ? "Report #" : "Report_no", 200)?.ToUpperInvariant();
                    if (string.IsNullOrWhiteSpace(job)) { issues.Add(new(file, row.Number, "JOB_REQUIRED", "Falta JOB.", true)); continue; }
                    var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
                    // Archive original headers/values without interpreting unverified columns as rules.
                    foreach (var header in table.Headers.Where(h=>h.Length>0)) fields[header] = Cell(header,16384);
                    WipValues? wip = null;
                    PlanningOfficialState? official=null;
                    string? test = null;
                    if (file == "Summary")
                    {
                        var registered = Date("Reg Date"); var due = Date("Delivery Date");
                        if (registered is { } d) dates.Add(d);
                        decimal? amount = null;
                        var rawAmount = Cell("PI Total Amt");
                        if (rawAmount is not null)
                        {
                            if (!decimal.TryParse(rawAmount, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed) || parsed < 0 || parsed > 99999999999999.9999m)
                                issues.Add(new(file, row.Number, "AMOUNT_INVALID", "PI Total Amt no es un decimal no negativo verificable.", true));
                            else amount = parsed;
                        }
                        var currency = Cell("PI Currency", 20)?.ToUpperInvariant();
                        var usd = currency == "USD" ? amount : null;
                        decimal? rate=null;
                        if(Cell("PI ExRate") is {} rawRate)
                        {
                            if(!decimal.TryParse(rawRate,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var parsedRate)||parsedRate<=0)
                                issues.Add(new(file,row.Number,"RATE_INVALID","PI ExRate debe ser un decimal positivo; no se calcula USD con una tasa supuesta.",true));
                            else rate=parsedRate;
                        }
                        // Verified legacy Summary rule: GTQ / PI ExRate. Never assume a missing rate equals one.
                        if(currency=="GTQ"&&rate is >0&&amount is not null)
                        {
                            usd=Math.Round(amount.Value/rate.Value,2,MidpointRounding.AwayFromZero);
                            if(usd>99999999999999.9999m)issues.Add(new(file,row.Number,"USD_OUT_OF_RANGE","USD convertido excede el límite financiero.",true));
                        }
                        if(amount is not null&&usd is null)unverifiedUsd++;
                        wip = new(Cell("Buyer", 512), Cell("Vendor", 512), Cell("Service Type", 200), registered, due, amount, currency, usd);
                        string? Status(string a,string b)
                        {
                            var first=Cell(a,200);var second=Cell(b,200);
                            if(first is not null&&second is not null&&first!=second)issues.Add(new(file,row.Number,"STATUS_CONFLICT",$"{a} y {b} son incompatibles. Revise el encabezado de autoridad.",true));
                            return first??second;
                        }
                        official=new(Status("Sample Status","Status Sample"),Status("Job Status","Status Job"),Date("Close Date"),rate,Cell("Customer_status",200));
                        if (registered is null || due is null) issues.Add(new(file, row.Number, "WIP_DATES_INCOMPLETE", "Información WIP incompleta: no se inventan fechas."));
                        if (Date("Close Date") is not null) externalClosed++;
                    }
                    else
                    {
                        test = Cell("Test_Name");
                        if (test is null) issues.Add(new(file, row.Number, "TEST_REQUIRED", "Test_Name no informado. Se conserva la fila en Datos incompletos, sin inventar ejecución ni equivalencia."));
                    }
                    rows.Add(new(file, table.Sheet, row.Number, job, test, wip, JsonSerializer.Serialize(fields),official));
                }
                var min = dates.Count > 0 ? dates.Min() : (DateOnly?)null;
                if (unverifiedUsd > 0) issues.Add(new(file, 0, "USD_UNVERIFIED", $"{unverifiedUsd:N0} importes sin moneda USD explícita ni conversión GTQ con PI ExRate positivo (regla del HTML original). USD se conserva desconocido; cada valor original queda en procedencia."));
                if (externalClosed > 0) issues.Add(new(file, 0, "EXTERNAL_CLOSE", $"{externalClosed:N0} referencias con Close Date externo. Se conserva separado del estado y la digitación; no cierra ni elimina registros locales."));
                var max = dates.Count > 0 ? dates.Max() : (DateOnly?)null;
                files.Add(new(file, Path.GetFileName(name), Hash(bytes), table.Rows.Count, min, max,
                    file == "Summary" ? "Reg Date observado; no acredita fechas ausentes del archivo" : "Report List observado no acredita cobertura temporal: no contiene fechas verificadas"));
                if (file == "Summary" && dates.Count > 0)
                {
                    var expected = cut.Window == "First" ? cut.Day.AddDays(-1) : cut.Day;
                    if (dates.Any(d => d != expected)) issues.Add(new(file, 0, "COVERAGE_DIFFERENCE", $"Reg Date no coincide íntegramente con {expected:yyyy-MM-dd}. Revise cobertura; no es un error estructural."));
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or XmlException or FormatException or OverflowException or ArgumentException)
            { issues.Add(new(file, 0, "FILE_INVALID", ex is InvalidDataException ? ex.Message : "XLSX o valores fuera del contrato verificado.", true)); }
        }
    }
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
