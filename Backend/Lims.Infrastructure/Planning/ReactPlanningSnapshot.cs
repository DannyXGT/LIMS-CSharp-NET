using System.Globalization;
using System.Text;
using System.Text.Json;
using Lims.Application.Planning;
using Lims.Contracts.Planning;

namespace Lims.Infrastructure.Planning;

public sealed partial class PostgresPlanningStore
{
    public async Task<PlanningImportPreview> PreviewReactTablesAsync(int actor,DateOnly day,CancellationToken ct)
    {
        var rows=new List<PlanningSourceRow>();string token;
        await using(var c=await database.Source.OpenConnectionAsync(ct).ConfigureAwait(false))
        await using(var tx=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct).ConfigureAwait(false))
        {
            await RequireReactSchema(c,tx,ct).ConfigureAwait(false);token=await ReadReactStateToken(c,tx,ct).ConfigureAwait(false);
            await using var cmd=Command(c,tx,"SELECT to_jsonb(a)::text FROM public.planificacion_activa a ORDER BY id LIMIT 50001");
            await using var r=await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while(await r.ReadAsync(ct).ConfigureAwait(false))
            {
                if(rows.Count==50000)throw new PlanningValidationException("La conciliación de tablas admite hasta 50,000 filas por revisión.");
                var raw=r.GetString(0);using var doc=JsonDocument.Parse(raw);var j=doc.RootElement;
                string? Get(params string[] names)=>names.Select(name=>j.TryGetProperty(name,out var value)&&value.ValueKind!=JsonValueKind.Null?value.ToString():null).FirstOrDefault(value=>!string.IsNullOrWhiteSpace(value));
                DateOnly? Date(params string[] names)=>DateOnly.TryParse(Get(names),CultureInfo.InvariantCulture,out var value)?value:null;
                decimal? Number(string name)=>decimal.TryParse(Get(name),NumberStyles.Number,CultureInfo.InvariantCulture,out var value)?value:null;
                var job=Get("jobs","report_no");var original=Get("test_name_tecnico","prueba","nombre_prueba_plan");
                if(job is null||original is null)throw new PlanningValidationException($"La fila React {j.GetProperty("id").GetInt64()} requiere JOB y prueba verificables antes de conciliar.");
                rows.Add(new("React Active","public.planificacion_activa",rows.Count+1,job.Trim().ToUpperInvariant(),original,
                    new(Get("buyer_excel2","cliente","buyer_name"),Get("vendor"),Get("servicio","service_type"),Date("fecha_ingreso","reg_date"),Date("due_date","delivery_date"),Number("pi_total_amt"),Get("pi_currency"),Number("usd")),
                    raw,new(Get("sample_status","status_tips"),CloseDate:Date("close_date"),CustomerStatus:Get("customer_status")),
                    Get("laboratorio","lab"),Get("nombre_prueba_plan","prueba"),j.GetProperty("id").GetInt64()));
            }
        }
        var bytes=Encoding.UTF8.GetBytes("["+string.Join(',',rows.Select(row=>row.Fields))+"]");
        var fingerprint=PlanningImportReader.Hash(Encoding.UTF8.GetBytes($"{PlanningImportRules.Version}|ReactTable|{day:yyyy-MM-dd}|{token}"));
        var input=new PlanningImportInput(new("Noon",day,"ReactTable"),[],bytes,"","public.planificacion_activa",fingerprint,
            [new("React Active","public.planificacion_activa",PlanningImportReader.Hash(bytes),rows.Count,null,null,"Instantánea de filas existentes de React, con sus identificadores originales; no es un archivo TIPS")],rows,[],token);
        return await PreviewAsync(input,actor,ct).ConfigureAwait(false);
    }
    private static PlanningEquivalence[] ReactSnapshotMappings(PlanningImportInput input,IReadOnlyList<PlanningEquivalence> catalogue)=>input.Rows.GroupBy(r=>PlanningReconciler.TestKey(r.Test!)).Select(g=>
    {
        var authoritative=catalogue.SingleOrDefault(m=>PlanningReconciler.TestKey(m.OriginalTest)==g.Key);
        if(authoritative is not null)return authoritative;
        var choices=g.Select(r=>(r.SourceEquivalence,r.SourceLaboratory)).Distinct().ToArray();var first=g.First();
        return new PlanningEquivalence(first.Test!,choices.Length==1?first.SourceEquivalence:null,choices.Length==1?first.SourceLaboratory:null,
            choices.Length==1&&PlanningImportRules.Token(first.SourceLaboratory)=="NO PROGRAMAR",0);
    }).ToArray();
}
