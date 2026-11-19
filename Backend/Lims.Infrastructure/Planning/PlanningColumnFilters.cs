using System.Globalization;
using System.Text.Json;
using Lims.Application.Planning;

namespace Lims.Infrastructure.Planning;

public sealed partial class PostgresPlanningStore
{
    private const string OfficialStateSql="coalesce(nullif(e.official_fields->>'sampleStatus',''),nullif(e.official_fields->>'jobStatus',''),'No informado')";
    private const string DigitizationSql="CASE WHEN x.digitized_at IS NOT NULL AND x.tested_on IS NOT NULL THEN 'Digitado' WHEN x.digitized_at IS NOT NULL OR x.tested_on IS NOT NULL THEN 'Información incompleta' ELSE 'Sin información' END";
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
                "JobState"=>OfficialStateSql,"Job"=>"e.job||' '||coalesce(jc.label,'')","OriginalTest"=>"x.original_test","Equivalence"=>"x.equivalence",
                "Laboratory"=>"x.laboratory","Service"=>"e.service","Buyer"=>"e.buyer","Vendor"=>"e.vendor",
                "Responsible"=>"x.responsible","Observation"=>"x.observation","DigitizedBy"=>"x.digitized_by",
                "DigitizedAt"=>"to_char(x.digitized_at AT TIME ZONE 'America/Guatemala','HH24:MI:SS')",
                "DigitizationState"=>DigitizationSql,"Origin"=>"CASE WHEN x.origin='ReportList' THEN 'Report List' WHEN x.origin='Manual' THEN 'Manual' ELSE 'Summary' END","ExecutionType"=>"x.execution_type",
                "InDate"=>"e.in_date","DueDate"=>"e.due_date","TestedOn"=>"x.tested_on","JobLabel"=>"jc.label",
                "OnHold"=>"coalesce(jc.on_hold,false)","NoProgram"=>"coalesce(x.no_program,false)","Usd"=>"e.usd",
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
