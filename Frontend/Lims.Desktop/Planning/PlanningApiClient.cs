using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Lims.Contracts.Planning;
using Lims.Contracts.Permissions;
using Lims.Desktop.Http;

namespace Lims.Desktop.Planning;

public sealed record PlanningCall<T>(bool IsSuccess,T? Value,string? Error,int StatusCode);
public sealed class PlanningApiClient(IHttpClientFactory clients)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public async Task<PlanningCall<WorkspaceAccess>> Workspaces(CancellationToken ct)
    {
        var result=await Send<WorkspaceAccess>(HttpMethod.Get,"/api/workspaces",null,ct).ConfigureAwait(false);
        if(result.StatusCode!=404)return result;
        var legacy=await Send<EffectivePermissions>(HttpMethod.Get,"/api/auth/permissions",null,ct).ConfigureAwait(false);
        if(!legacy.IsSuccess||legacy.Value is not {} current)return new(false,null,legacy.Error,legacy.StatusCode);
        var admin=current.Role=="Administrador";
        return new(true,new(admin||PermissionCatalog.All.Any(p=>current.Permissions.Contains(p.Code,StringComparer.Ordinal)),false,admin,current.Permissions,
            "Planificación todavía no está disponible en esta API. Puedes continuar en los módulos actuales autorizados."),null,200);
    }
    public Task<PlanningCall<PlanningPage>> Jobs(PlanningQuery query,CancellationToken ct)=>Send<PlanningPage>(HttpMethod.Get,"/api/planning/jobs?"+Query(query),null,ct);
    public async Task<PlanningCall<ReportListPage>> ReportList(ReportListQuery query,CancellationToken ct)
    {
        var result=await Send<ReportListPage>(HttpMethod.Get,"/api/planning/report-list?"+string.Join('&',new Dictionary<string,string?>{
        ["Search"]=query.Search,["View"]=query.View,["Laboratory"]=query.Laboratory,["Test"]=query.Test,["Service"]=query.Service,["Buyer"]=query.Buyer,
        ["Page"]=query.Page.ToString(CultureInfo.InvariantCulture),["PageSize"]=query.PageSize.ToString(CultureInfo.InvariantCulture),
        ["Sort"]=query.Sort,["Descending"]=query.Descending.ToString(),["VerifiedBy"]=query.VerifiedBy,["Block"]=query.Block?.ToString(CultureInfo.InvariantCulture),
        ["CheckedDate"]=query.CheckedDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),["Revision"]=query.Revision?.ToString(CultureInfo.InvariantCulture),["ColumnFilters"]=query.ColumnFilters,["ExecutionId"]=query.ExecutionId?.ToString(),["Offset"]=query.Offset?.ToString(CultureInfo.InvariantCulture)
        }.Where(p=>!string.IsNullOrEmpty(p.Value)).Select(p=>p.Key+"="+Uri.EscapeDataString(p.Value!))),null,ct).ConfigureAwait(false);
        return result.StatusCode==404?result with { Error="Report List todavía no está disponible en el servidor configurado. Se requiere actualizar el servidor para usar este módulo." }:result;
    }
    public Task<PlanningCall<ReportListResult>> CheckReport(ReportListCheck request,bool correction,CancellationToken ct)=>Send<ReportListResult>(HttpMethod.Post,"/api/planning/report-list/"+(correction?"correction":"check"),request,ct);
    public Task<PlanningCall<ReportListResult>> DigitizeReport(ReportDigitization request,CancellationToken ct)=>Send<ReportListResult>(HttpMethod.Post,"/api/planning/report-list/digitize",request,ct);
    private static string ScheduleQuery(ReportScheduleQuery q)=>string.Join('&',new Dictionary<string,string?>{
        ["Search"]=q.Search,["State"]=q.State,["Page"]=q.Page.ToString(CultureInfo.InvariantCulture),["PageSize"]=q.PageSize.ToString(CultureInfo.InvariantCulture),["Revision"]=q.Revision?.ToString(CultureInfo.InvariantCulture),["GroupByClock"]=q.GroupByClock?"true":null
    }.Where(p=>!string.IsNullOrEmpty(p.Value)).Select(p=>p.Key+"="+Uri.EscapeDataString(p.Value!)));
    public Task<PlanningCall<ReportSchedulePage>> ReportSchedules(ReportScheduleQuery query,CancellationToken ct)=>Send<ReportSchedulePage>(HttpMethod.Get,"/api/planning/report-list/schedules?"+ScheduleQuery(query),null,ct);
    public Task<PlanningCall<ReportScheduleDetail>> ReportSchedule(Guid id,CancellationToken ct,bool groupByClock=false)=>Send<ReportScheduleDetail>(HttpMethod.Get,$"/api/planning/report-list/schedules/{id:D}"+(groupByClock?"?groupByClock=true":""),null,ct);
    public Task<PlanningCall<ReportSchedule>> ReviewSchedule(ReportScheduleReview request,CancellationToken ct)=>Send<ReportSchedule>(HttpMethod.Post,"/api/planning/report-list/schedules/review",request,ct);
    public Task<PlanningCall<ReportClosurePage>> ReportClosures(ReportScheduleQuery query,CancellationToken ct)=>Send<ReportClosurePage>(HttpMethod.Get,"/api/planning/report-list/closures?"+ScheduleQuery(query),null,ct);
    public async Task<PlanningCall<byte[]>> ExportSchedules(ReportScheduleQuery query,CancellationToken ct)
    {
        try{using var response=await clients.CreateClient(ApiClientNames.Authorized).GetAsync("/api/planning/report-list/schedules/export?"+ScheduleQuery(query),ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode?new(true,await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false),null,(int)response.StatusCode):new(false,null,await Error(response,ct).ConfigureAwait(false),(int)response.StatusCode);}
        catch(HttpRequestException){return new(false,null,"No se pudo conectar con la API.",0);}
    }
    public Task<PlanningCall<bool>> KeepReport(ReportListKeep request,CancellationToken ct)=>Send<bool>(HttpMethod.Put,"/api/planning/report-list/keep",request,ct);
    public Task<PlanningCall<ReportListResult>> ScanReport(ReportListScan request,CancellationToken ct)=>Send<ReportListResult>(HttpMethod.Post,"/api/planning/report-list/scan",request,ct);
    public Task<PlanningCall<JobDossier>> Dossier(Guid id,CancellationToken ct)=>Send<JobDossier>(HttpMethod.Get,$"/api/planning/jobs/{id:D}",null,ct);
    public Task<PlanningCall<IReadOnlyList<PlanningNoteSaved>>> SaveNotes(PlanningNoteBatch request,CancellationToken ct)=>Send<IReadOnlyList<PlanningNoteSaved>>(HttpMethod.Post,"/api/planning/notes",request,ct);
    public Task<PlanningCall<bool>> SaveWip(Guid id,WipEdit request,CancellationToken ct)=>Send<bool>(HttpMethod.Put,$"/api/planning/jobs/{id:D}/wip",request,ct);
    public Task<PlanningCall<PlanningJobControl>> SaveControl(Guid id,PlanningControlEdit request,CancellationToken ct)=>Send<PlanningJobControl>(HttpMethod.Put,$"/api/planning/jobs/{id:D}/control",request,ct);
    public Task<PlanningCall<bool>> Manual(Guid id,ManualTestRequest request,CancellationToken ct)=>Send<bool>(HttpMethod.Post,$"/api/planning/jobs/{id:D}/manual-tests",request,ct);
    public Task<PlanningCall<bool>> Incident(Guid id,IncidentDecision request,CancellationToken ct)=>Send<bool>(HttpMethod.Put,$"/api/planning/incidents/{id:D}",request,ct);
    public Task<PlanningCall<IReadOnlyList<PlanningBatch>>> Batches(CancellationToken ct)=>Send<IReadOnlyList<PlanningBatch>>(HttpMethod.Get,"/api/planning/imports",null,ct);
    public Task<PlanningCall<PlanningImportPreview>> Batch(Guid id,CancellationToken ct)=>Send<PlanningImportPreview>(HttpMethod.Get,$"/api/planning/imports/{id:D}",null,ct);
    public Task<PlanningCall<PlanningImportInspection>> InspectImport(Guid id,CancellationToken ct)=>Send<PlanningImportInspection>(HttpMethod.Get,$"/api/planning/imports/{id:D}/inspection",null,ct);
    public Task<PlanningCall<bool>> ReviewDecision(PlanningReviewDecision request,CancellationToken ct)=>Send<bool>(HttpMethod.Put,"/api/planning/reviews/decision",request,ct);
    public Task<PlanningCall<PlanningReviewPage>> Reviews(CancellationToken ct,string? category=null,string? search=null,int page=1)=>Send<PlanningReviewPage>(HttpMethod.Get,"/api/planning/reviews?category="+Uri.EscapeDataString(category??"")+"&search="+Uri.EscapeDataString(search??"")+"&page="+page.ToString(CultureInfo.InvariantCulture),null,ct);
    public Task<PlanningCall<PlanningReviewWorkspace>> ReviewWorkspace(PlanningReviewQuery query,CancellationToken ct)=>Send<PlanningReviewWorkspace>(HttpMethod.Get,"/api/planning/reviews/workspace?"+ReviewQuery(query),null,ct);
    public async Task<PlanningCall<byte[]>> ExportReviews(PlanningReviewQuery query,CancellationToken ct)
    {
        try
        {
            using var response=await clients.CreateClient(ApiClientNames.Authorized).PostAsJsonAsync("/api/planning/reviews/export",query,ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode?new(true,await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false),null,(int)response.StatusCode):new(false,null,await Error(response,ct).ConfigureAwait(false),(int)response.StatusCode);
        }
        catch(HttpRequestException){return new(false,null,"No se pudo conectar con la API.",0);}
    }
    private static string ReviewQuery(PlanningReviewQuery query)=>string.Join('&',new Dictionary<string,string?>{
        ["Category"]=query.Category,["Situation"]=query.Situation,["Search"]=query.Search,["Sort"]=query.Sort,["Descending"]=query.Descending.ToString(),
        ["Page"]=query.Page.ToString(CultureInfo.InvariantCulture),["PageSize"]=query.PageSize.ToString(CultureInfo.InvariantCulture),["ColumnFilters"]=query.ColumnFilters,
        ["Scope"]=query.Scope,["BatchId"]=query.BatchId?.ToString(),["ImportedFrom"]=query.ImportedFrom?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),["ImportedUntil"]=query.ImportedUntil?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        ["Test"]=query.Test,["Laboratory"]=query.Laboratory,["Origin"]=query.Origin,["SelectedKeys"]=query.SelectedKeys
    }.Where(p=>!string.IsNullOrEmpty(p.Value)).Select(p=>p.Key+"="+Uri.EscapeDataString(p.Value!)));
    public Task<PlanningCall<PlanningImportResult>> Confirm(Guid id,CancellationToken ct)=>Send<PlanningImportResult>(HttpMethod.Post,$"/api/planning/imports/{id:D}/confirm",null,ct);
    public Task<PlanningCall<IReadOnlyList<PlanningEquivalence>>> Equivalences(CancellationToken ct)=>Send<IReadOnlyList<PlanningEquivalence>>(HttpMethod.Get,"/api/planning/equivalences",null,ct);
    public Task<PlanningCall<bool>> SaveEquivalence(EquivalenceDecision request,CancellationToken ct)=>Send<bool>(HttpMethod.Put,"/api/planning/equivalences",request,ct);
    public Task<PlanningCall<LegacyEquivalencePreview>> LegacyEquivalences(CancellationToken ct)=>Send<LegacyEquivalencePreview>(HttpMethod.Get,"/api/planning/equivalences/legacy",null,ct);
    public Task<PlanningCall<bool>> ImportLegacyEquivalences(LegacyEquivalenceConfirmation request,CancellationToken ct)=>Send<bool>(HttpMethod.Post,"/api/planning/equivalences/legacy",request,ct);
    public Task<PlanningCall<bool>> Resolve(SourceResolution request,CancellationToken ct)=>Send<bool>(HttpMethod.Post,"/api/planning/source-resolution",request,ct);
    public Task<PlanningCall<IReadOnlyList<PlanningAccessUser>>> Users(CancellationToken ct)=>Send<IReadOnlyList<PlanningAccessUser>>(HttpMethod.Get,"/api/planning/access/users",null,ct);
    public Task<PlanningCall<bool>> SaveAccess(int id,PlanningAccessChange request,CancellationToken ct)=>Send<bool>(HttpMethod.Put,$"/api/planning/access/users/{id}",request,ct);
    public Task<PlanningCall<PlanningImportPreview>> Preview(string summaryName,byte[] summary,string reportName,byte[] report,ImportCut cut,CancellationToken ct)
    {
        var form=new MultipartFormDataContent();
        form.Add(new ByteArrayContent(summary),"summary",summaryName);form.Add(new ByteArrayContent(report),"report",reportName);
        form.Add(new StringContent(cut.Window),"window");form.Add(new StringContent(cut.Day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),"day");
        form.Add(new StringContent(cut.Policy),"policy");
        if(cut.TargetInDate is{} target)form.Add(new StringContent(target.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),"targetInDate");
        return Send<PlanningImportPreview>(HttpMethod.Post,"/api/planning/imports/preview",form,ct);
    }
    public Task<PlanningCall<PlanningImportPreview>> PreviewSupplement(string name,byte[] bytes,DateOnly day,CancellationToken ct)
    {
        var form=new MultipartFormDataContent();form.Add(new ByteArrayContent(bytes),"supplement",name);
        form.Add(new StringContent(day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),"day");
        return Send<PlanningImportPreview>(HttpMethod.Post,"/api/planning/imports/supplement/preview",form,ct);
    }
    public Task<PlanningCall<PlanningImportPreview>> PreviewReactTables(DateOnly day,CancellationToken ct)=>Send<PlanningImportPreview>(HttpMethod.Post,"/api/planning/imports/react/preview",new ReactTableReviewRequest(day),ct);
    public async Task<PlanningCall<byte[]>> Export(PlanningQuery query,CancellationToken ct)
    {
        try
        {
            using var response=await clients.CreateClient(ApiClientNames.Authorized).GetAsync("/api/planning/export?"+Query(query),ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode?new(true,await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false),null,(int)response.StatusCode):new(false,null,await Error(response,ct).ConfigureAwait(false),(int)response.StatusCode);
        }
        catch(HttpRequestException){return new(false,null,"No se pudo conectar con la API.",0);}
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){return new(false,null,"La consulta excedió el tiempo de espera.",0);}
    }
    private async Task<PlanningCall<T>> Send<T>(HttpMethod method,string path,object? body,CancellationToken ct)
    {
        try
        {
            using var request=new HttpRequestMessage(method,path);
            request.Content=body is HttpContent content?content:body is null?null:JsonContent.Create(body,options:Json);
            using var response=await clients.CreateClient(ApiClientNames.Authorized).SendAsync(request,ct).ConfigureAwait(false);
            if(!response.IsSuccessStatusCode)return new(false,default,await Error(response,ct).ConfigureAwait(false),(int)response.StatusCode);
            if(response.StatusCode==System.Net.HttpStatusCode.NoContent)return new(true,default,null,204);
            return new(true,await response.Content.ReadFromJsonAsync<T>(Json,ct).ConfigureAwait(false),null,(int)response.StatusCode);
        }
        catch(HttpRequestException){return new(false,default,"No se pudo conectar con la API.",0);}
        catch(JsonException){return new(false,default,"La respuesta de la API no corresponde a esta versión de Planificación.",0);}
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){return new(false,default,"La operación excedió el tiempo de espera. Consulte el historial antes de reintentar.",0);}
    }
    private static async Task<string> Error(HttpResponseMessage response,CancellationToken ct)
    {
        try
        {
            using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            foreach(var key in new[]{"detail","message","title"})if(document.RootElement.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String)return value.GetString()!;
            if(document.RootElement.TryGetProperty("error",out var error)&&error.TryGetProperty("message",out var message))return message.GetString()!;
        }
        catch(JsonException){/* The status is sufficient for a non-JSON response. */}
        return response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized?"No tiene autorización vigente para esta operación.":"La API no pudo completar la operación.";
    }
    private static string Query(PlanningQuery query)=>string.Join('&',new Dictionary<string,string?>
    {
        ["Search"]=query.Search,["Test"]=query.Test,["Laboratory"]=query.Laboratory,["Service"]=query.Service,["State"]=query.State,
        ["Buyer"]=query.Buyer,["Vendor"]=query.Vendor,["Equivalence"]=query.Equivalence,["Incident"]=query.Incident,
        ["SearchMode"]=query.SearchMode,["InFrom"]=query.InFrom?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),["InUntil"]=query.InUntil?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),["Overdue"]=query.Overdue.ToString(),
        ["Scope"]=query.Scope,["OnHold"]=query.OnHold?.ToString(),["Tagged"]=query.Tagged?.ToString(),["Label"]=query.Label,["ColumnFilters"]=query.ColumnFilters,["BatchId"]=query.BatchId?.ToString(),
        ["From"]=query.From?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),["Until"]=query.Until?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        ["Sort"]=query.Sort,["Descending"]=query.Descending.ToString(),["Page"]=query.Page.ToString(CultureInfo.InvariantCulture),["PageSize"]=query.PageSize.ToString(CultureInfo.InvariantCulture)
    }.Where(p=>!string.IsNullOrEmpty(p.Value)).Select(p=>p.Key+"="+Uri.EscapeDataString(p.Value!)));
}
