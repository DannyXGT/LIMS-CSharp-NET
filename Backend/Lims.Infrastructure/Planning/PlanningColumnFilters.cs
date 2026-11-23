using System.Globalization;
using System.Text.Json;
using Lims.Application.Planning;

namespace Lims.Infrastructure.Planning;

public sealed partial class PostgresPlanningStore
{
    private const string PlanningTestSql="CASE WHEN x.id IS NULL THEN 'WIP sin pruebas' WHEN x.no_program OR upper(coalesce(trim(x.laboratory),''))='NO PROGRAMAR' THEN 'NO PROGRAMAR' ELSE coalesce(nullif(trim(x.equivalence),''),'Sin equivalencia') END";
    private const string OfficialStateSql="coalesce(nullif(e.official_fields->>'sampleStatus',''),nullif(e.official_fields->>'jobStatus',''),'No informado')";
    private const string DigitizationSql="CASE WHEN x.digitized_at IS NOT NULL AND x.tested_on IS NOT NULL THEN 'Digitado' WHEN x.digitized_at IS NOT NULL OR x.tested_on IS NOT NULL THEN 'Información incompleta' ELSE 'Sin información' END";
    private static string ScheduleReviewField(string field)=>"(SELECT "+field+" FROM planning.report_session_members rm JOIN planning.report_schedule_status rg ON rg.id=rm.session_id LEFT JOIN planning.actors ra ON ra.id=rg.reviewed_by WHERE rm.execution_id=x.id AND rm.digitization_version=x.version AND x.digitized_at=rg.operational_at ORDER BY rg.saved_at DESC,rg.id LIMIT 1)";
    private static readonly string ScheduleReviewStateSql="coalesce("+ScheduleReviewField("CASE WHEN rg.exceptions>0 THEN 'Revisar excepciones' WHEN rg.reviewed_at IS NOT NULL THEN 'Chequeado' ELSE 'Pendiente' END")+",CASE WHEN x.digitized_at IS NOT NULL AND x.tested_on IS NOT NULL THEN 'Sin sesión de chequeo' ELSE 'Pendiente de digitación' END)";
    private static (string Sql,IReadOnlyList<(string,object?)> Args) ColumnConditions(string? json)
    {
        if(string.IsNullOrWhiteSpace(json))return ("",[]);
        if(json.Length>12000)throw new PlanningValidationException("Demasiados filtros de columna.");
        Dictionary<string,string>? filters;
        try{filters=JsonSerializer.Deserialize<Dictionary<string,string>>(json);}catch(JsonException){throw new PlanningValidationException("Filtros de columna inválidos.");}
        if(filters is null||filters.Count>24)throw new PlanningValidationException("Filtros de columna inválidos.");
        var sql=new System.Text.StringBuilder();var args=new List<(string,object?)>();
        foreach(var (key,value) in filters)
        {
            if(string.IsNullOrWhiteSpace(value))continue;
            if(value.Length>2000||value.Any(char.IsControl))throw new PlanningValidationException("Valor de filtro inválido.");
            var expression=key switch
            {
                "JobState"=>OfficialStateSql,"Job"=>"e.job||' '||coalesce(jc.label,'')","PlanningTest"=>PlanningTestSql,"OriginalTest"=>"x.original_test","Equivalence"=>"x.equivalence",
                "Laboratory"=>"x.laboratory","Service"=>"e.service","Buyer"=>"e.buyer","Vendor"=>"e.vendor",
                "Responsible"=>"x.responsible","Observation"=>"x.observation","DigitizedBy"=>"x.digitized_by",
                "DigitizedAt"=>"to_char(x.digitized_at AT TIME ZONE 'America/Guatemala','HH24:MI:SS')",
                "DigitizationState"=>DigitizationSql,"Origin"=>"CASE WHEN x.origin='ReportList' THEN 'Report List' WHEN x.origin='Manual' THEN 'Manual' ELSE 'Summary' END","ExecutionType"=>"x.execution_type",
                "InDate"=>"e.in_date","DueDate"=>"e.due_date","TestedOn"=>"x.tested_on","JobLabel"=>"jc.label",
                "OnHold"=>"coalesce(jc.on_hold,false)","NoProgram"=>"coalesce(x.no_program,false)","Usd"=>"e.usd",
                "ScheduleReviewState"=>ScheduleReviewStateSql,"ScheduleReviewedBy"=>ScheduleReviewField("ra.nombre"),
                "ScheduleReviewedAt"=>"to_char("+ScheduleReviewField("rg.reviewed_at")+" AT TIME ZONE 'America/Guatemala','YYYY-MM-DD HH24:MI:SS')","ExecutionId"=>"x.id::text",
                _=>throw new PlanningValidationException("Columna de filtro no admitida: "+key)
            };
            var param="column"+args.Count.ToString(CultureInfo.InvariantCulture);
            if(key is "InDate" or "DueDate" or "TestedOn")
            {
                if(!DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day))throw new PlanningValidationException("Seleccione una fecha válida en el calendario.");
                sql.Append(CultureInfo.InvariantCulture,$" AND {expression}=@{param}::date");args.Add((param,day));
            }
            else if(key is "OnHold" or "NoProgram")
            {
                if(!bool.TryParse(value,out var flag))throw new PlanningValidationException("Filtro de estado inválido.");
                sql.Append(CultureInfo.InvariantCulture,$" AND {expression}=@{param}");args.Add((param,flag));
            }
            else if(key=="Usd")
            {
                if(!decimal.TryParse(value,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var amount))throw new PlanningValidationException("USD debe ser un número con punto decimal.");
                sql.Append(CultureInfo.InvariantCulture,$" AND {expression}=@{param}::numeric");args.Add((param,amount));
            }
            else{sql.Append(CultureInfo.InvariantCulture,$" AND strpos(lower(coalesce({expression},'')),lower(@{param}))>0");args.Add((param,value.Trim()));}
        }
        return (sql.ToString(),args);
    }
}
