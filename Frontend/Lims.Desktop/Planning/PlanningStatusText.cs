using System.Globalization;
using System.Text.Json;
using Lims.Contracts.Planning;

namespace Lims.Desktop.Planning;

public static class PlanningStatusText
{
    public static bool Digitized(PlanningRow row)=>row.Complete;
    public static bool Pending(PlanningRow row)=>row.OperationalPending;
    public static bool Unknown(PlanningRow row)=>row.ExecutionId is not null&&row.DigitizedAt is null&&row.TestedOn is null;
    public static string Teams(JobDossier d)=>$"JOB: {d.Job}\nCliente: {d.Wip.Buyer??"No informado"}\nIngreso: {d.Wip.InDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??"No informado"}\nVencimiento: {d.Wip.DueDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??"No informado"}\nOn Hold (LIMS): {(d.Control?.OnHold==true?"Sí":"No registrado")}\nEtiqueta: {d.Control?.Label??"No registrada"}\nPruebas registradas: {d.Tests.Count}\nDigitadas: {d.Tests.Count(Digitized)}\nPendientes: {d.Tests.Count(Pending)}\nSin información: {d.Tests.Count(Unknown)}\nPruebas pendientes: {Names(d.Tests.Where(Pending))}\nPruebas sin información: {Names(d.Tests.Where(Unknown))}\nEstado oficial: {d.Official?.SampleStatus??d.Official?.JobStatus??"No verificado"}\nLa cobertura heredada de digitación, On Hold y etiquetas no está verificada. Los conteos no certifican cierre oficial.";
    private static string Names(IEnumerable<PlanningRow> rows)=>string.Join(", ",rows.Select(t=>t.PlanningTest))is{Length:>0} value?value:"Ninguna registrada";
    public static string Action(string action)=>action switch
    {"ReportScheduleReviewed"=>"Horario chequeado","ReportMarked"=>"Prueba marcada en Report List","ReportCorrected"=>"Marcado corregido en Report List","ReportKeepChanged"=>"Permanencia en programaci\u00f3n actualizada","ReportLastPendingCompleted"=>"\u00daltima prueba pendiente completada","OfficialStateImported"=>"Estado TIPS importado","ConsultationNotesEdited"=>"Responsable / observación actualizados","WipImported"=>"WIP importado","WipSourceObserved"=>"Fuente WIP conciliada","WipEdited"=>"WIP corregido","ManualTestAdded"=>"Prueba manual agregada","ManualSourceLinked"=>"Prueba manual vinculada al archivo","TestImported"=>"Prueba importada","IncidentDecision"=>"Decisión de incidencia","IncidentsReconciled"=>"Incidencias conciliadas","ExecutionIdentityResolved"=>"Ejecución conciliada","EquivalenceApplied"=>"Equivalencia aplicada",_=>"Actualización de Planificación"};
    private static string Label(string key)=>key.ToLowerInvariant() switch{"digitizedat"=>"Fecha y hora del marcado (UTC)","testedon"=>"Fecha de prueba","digitizedby"=>"Verificado por","responsible"=>"Responsable","observation"=>"Observación","buyer"=>"Buyer / marca","vendor"=>"Vendor","service"=>"Servicio","indate"=>"Ingreso","duedate"=>"Vencimiento","amount"=>"Importe fuente","currency"=>"Moneda fuente","usd"=>"USD",_=>key};
    public static string Changes(string details)
    {
        try
        {
            using var doc=JsonDocument.Parse(details);var root=doc.RootElement;
            var execution=root.TryGetProperty("executionId",out var identity)?"Ejecución: "+identity+"\n":"";
            if(root.TryGetProperty("execution",out var target)&&target.TryGetProperty("executionId",out var keepIdentity)&&root.TryGetProperty("keepInPlanning",out var keep))
                return $"Ejecución: {keepIdentity}\nMantener en programación: {(keep.GetBoolean()?"Sí":"No")}";
            if(root.TryGetProperty("before",out var before)&&root.TryGetProperty("after",out var after)&&after.ValueKind==JsonValueKind.Object)
            {var values=new List<string>();foreach(var field in after.EnumerateObject()){var old=before.ValueKind==JsonValueKind.Object&&before.TryGetProperty(field.Name,out var b)?b.ToString():"";if(old!=field.Value.ToString())values.Add($"{Label(field.Name)}: {(old.Length==0?"Sin valor":old)} → {(field.Value.ValueKind==JsonValueKind.Null?"Sin valor":field.Value.ToString())}");}return execution+string.Join("\n",values);}
            if(root.TryGetProperty("sessionId",out var session)&&root.TryGetProperty("executions",out var reviewed)&&reviewed.ValueKind==JsonValueKind.Array)
                return "Horario: "+session+"\nEjecuciones revisadas: "+string.Join(", ",reviewed.EnumerateArray().Select(x=>x.GetProperty("executionId").ToString()));
            if(root.TryGetProperty("source",out var source))return string.Join(" · ",source.EnumerateObject().Where(p=>p.Value.ValueKind!=JsonValueKind.Null).Select(p=>$"{Label(p.Name)}: {p.Value}"));
            if(root.TryGetProperty("state",out var state))return "Estado: "+PlanningLabels.State(state.GetString()??"");
            if(root.TryGetProperty("originalTest",out var test))return "Prueba: "+test.GetString();
            return "Cambio registrado con su fuente en procedencia.";
        }
        catch(JsonException){return "Detalle histórico disponible en procedencia técnica.";}
    }
}
