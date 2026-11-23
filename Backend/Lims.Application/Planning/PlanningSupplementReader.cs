using System.Text;
using System.Text.Json;
using Lims.Contracts.Planning;

namespace Lims.Application.Planning;

public static partial class PlanningImportReader
{
    private static readonly string[] SupplementHeaders=["Ingreso","Due","Job","Test","Lab"];
    private static string SupplementHeader(string header)=>PlanningImportRules.Token(header).Replace('_',' ') switch
    {
        "FECHA INGRESO" or "FECHA DE INGRESO" or "IN DATE"=>"Ingreso",
        "DUE DATE" or "DUE"=>"Due", "JOB" or "JOBS"=>"Job",
        "PRUEBA" or "TEST"=>"Test", "LABORATORIO" or "LAB" or "LABORATORY"=>"Lab",var name=>name
    };
    public static PlanningImportInput ReadSupplement(string name,byte[] bytes,DateOnly day,CancellationToken ct=default)
    {
        try{return ReadSupplementCore(name,bytes,day,ct);}
        catch(Exception e)when(e is InvalidDataException or System.Xml.XmlException or FormatException or OverflowException or ArgumentException)
        {throw new PlanningValidationException("XLSX complementario inválido: "+e.Message);}
    }
    private static PlanningImportInput ReadSupplementCore(string name,byte[] bytes,DateOnly day,CancellationToken ct)
    {
        var rows=new List<PlanningSourceRow>();var issues=new List<ImportIssue>();
        var table=XlsxTableReader.ReadWithHeaderKey(bytes,SupplementHeaders,SupplementHeader,ct);
        var columns=table.Headers.Select((value,index)=>(Key:SupplementHeader(value),index)).ToDictionary(c=>c.Key,c=>c.index,StringComparer.Ordinal);
        foreach(var row in table.Rows)
        {
            string? Cell(string key)
            {
                var value=row.Cells.GetValueOrDefault(columns[key]);
                if(value?.Formula==true||value?.Type=="e"||value?.Value.Length>2048)throw new PlanningValidationException($"Side 2 fila {row.Number}: campo calculado o inválido.");
                return string.IsNullOrWhiteSpace(value?.Value)?null:value.Value.Trim();
            }
            var job=Cell("Job")?.ToUpperInvariant();var test=Cell("Test");var lab=Cell("Lab");
            _=Cell("Ingreso");_=Cell("Due");
            if(job is null||test is null){issues.Add(new("Side2",row.Number,"IDENTITY_REQUIRED","Side 2 requiere JOB y prueba en cada fila.",true));continue;}
            if(job.Length>200||lab?.Length>200)throw new PlanningValidationException($"Side 2 fila {row.Number}: JOB o laboratorio demasiado largo.");
            var incoming=XlsxTableReader.Date(row.Cells.GetValueOrDefault(columns["Ingreso"]),table.Date1904);
            var due=XlsxTableReader.Date(row.Cells.GetValueOrDefault(columns["Due"]),table.Date1904);
            if(incoming is null||due is null)issues.Add(new("Side2",row.Number,"DATES_REQUIRED","Side 2 requiere ingreso y Due Date verificables.",true));
            var fields=table.Headers.Select((header,i)=>(header,Value:row.Cells.GetValueOrDefault(i)?.Value)).ToDictionary(c=>c.header,c=>c.Value);
            // Laboratory is a supplied field of this complementary file, not an inferred catalogue decision.
            rows.Add(new("Side2",table.Sheet,row.Number,job,test,new(null,null,null,incoming,due,null,null,null),JsonSerializer.Serialize(fields),SourceLaboratory:lab));
        }
        var cut=new ImportCut("Noon",day,"HtmlReactSide2");
        var fingerprint=Hash(Encoding.UTF8.GetBytes($"{PlanningLimits.ReaderProfile}|{cut.Policy}|{day:yyyy-MM-dd}|{Hash(bytes)}"));
        return new(cut,[],bytes,"",Path.GetFileName(name),fingerprint,[new("Side2",Path.GetFileName(name),Hash(bytes),table.Rows.Count,null,null,"Fechas y laboratorio declarados por el archivo complementario; sin estados oficiales inventados")],rows,issues);
    }
    public static IReadOnlyList<PlanningEquivalence> SupplementMappings(PlanningImportInput input)=>input.Rows.GroupBy(r=>PlanningReconciler.TestKey(r.Test!)).Select(g=>
    {
        var labs=g.Select(r=>r.SourceLaboratory).Distinct().ToArray();
        if(labs.Length!=1)throw new PlanningValidationException($"Side 2 declara distintos laboratorios para {g.First().Test}.");
        return new PlanningEquivalence(g.First().Test!,g.First().Test,labs[0],PlanningImportRules.Token(labs[0])=="NO PROGRAMAR",0);
    }).ToArray();
}
