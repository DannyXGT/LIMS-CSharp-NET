using System.Globalization;
using Lims.Api.Authentication;
using Lims.Application.Planning;
using Lims.Contracts.Permissions;
using Lims.Contracts.Planning;
using Lims.Infrastructure.Planning;
using Microsoft.AspNetCore.Http.Features;

namespace Lims.Api.Planning;

internal static class PlanningEndpoints
{
    public static void AddLimsPlanning(this IServiceCollection services,string connectionString,string identityConnectionString)
    {
        services.AddSingleton(provider=>new PlanningDatabase(connectionString,provider.GetRequiredService<IConfiguration>()["Planning:ReportTrackingTable"]));
        services.AddSingleton(new PlanningIdentityDatabase(identityConnectionString));
        services.AddScoped<IPlanningStore,PostgresPlanningStore>();
        services.Configure<FormOptions>(options=>options.MultipartBodyLengthLimit=12*1024*1024);
        foreach(var permission in PlanningPermissions.All)
            services.AddAuthorizationBuilder().AddPolicy(permission,p=>p.RequireAuthenticatedUser().RequireAssertion(c=>c.User.IsInRole("Administrador") || c.User.HasClaim("permission",permission)));
    }
    public static void MapPlanningEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/workspaces",(HttpContext context)=>
        {
            var admin=context.User.IsInRole("Administrador");
            var codes=context.User.FindAll("permission").Select(c=>c.Value).ToHashSet(StringComparer.Ordinal);
            bool Has(string p)=>admin||codes.Contains(p);
            return Results.Ok(new WorkspaceAccess(admin||PermissionCatalog.All.Any(p=>codes.Contains(p.Code)),Has(PlanningPermissions.View),admin,
                admin?codes.Concat(PlanningPermissions.All).Distinct(StringComparer.Ordinal).ToArray():codes.ToArray()));
        }).RequireAuthorization();
        var group=endpoints.MapGroup("/api/planning").WithTags("Planning").RequireAuthorization(PlanningPermissions.View);
        group.AddEndpointFilter(async(context,next)=>
        {
            try
            {
                if (!HttpMethods.IsGet(context.HttpContext.Request.Method) &&
                    !context.HttpContext.Request.Path.StartsWithSegments("/api/planning/access") &&
                    !context.HttpContext.Request.Path.StartsWithSegments("/api/planning/reviews/export"))
                {
                    var services=context.HttpContext.RequestServices;
                    await services.GetRequiredService<PlanningIdentityDatabase>().RegisterActorAsync(
                        services.GetRequiredService<PlanningDatabase>(),Actor(context.HttpContext),
                        context.HttpContext.RequestAborted).ConfigureAwait(false);
                }
                return await next(context).ConfigureAwait(false);
            }
            catch(PlanningConflictException e){return Results.Problem(statusCode:409,title:"Conflicto de revisión",detail:e.Message);}
            catch(PlanningValidationException e){return Results.Problem(statusCode:400,title:"Validación de Planificación",detail:e.Message);}
        });
        group.MapGet("/jobs",async([AsParameters]PlanningQuery query,IPlanningStore store,CancellationToken ct)=>Results.Ok(await store.QueryAsync(query,ct).ConfigureAwait(false)));
        group.MapGet("/report-list",async([AsParameters]ReportListQuery query,IPlanningStore store,CancellationToken ct)=>Results.Ok(await store.ReportListAsync(query,ct).ConfigureAwait(false)));
        group.MapPost("/report-list/digitize",async(ReportDigitization request,IPlanningStore store,HttpContext context,CancellationToken ct)=>
        {
            if(request.AdminOverride&&!context.User.IsInRole("Administrador"))return Results.Forbid();
            return Results.Ok(await store.DigitizeReportAsync(request,Actor(context),context.User.IsInRole("Administrador"),ct).ConfigureAwait(false));
        }).RequireAuthorization(PlanningPermissions.ReportMark);
        group.MapGet("/report-list/schedules",async([AsParameters]ReportScheduleQuery query,IPlanningStore store,CancellationToken ct)=>Results.Ok(await store.ReportSchedulesAsync(query,ct).ConfigureAwait(false)));
        group.MapGet("/report-list/schedules/{id:guid}",async(Guid id,IPlanningStore store,CancellationToken ct,bool groupByClock=false)=>Results.Ok(await store.ReportScheduleAsync(id,ct,groupByClock).ConfigureAwait(false)));
        group.MapPost("/report-list/schedules/review",async(ReportScheduleReview request,IPlanningStore store,HttpContext context,CancellationToken ct)=>Results.Ok(await store.ReviewReportScheduleAsync(request,Actor(context),ct).ConfigureAwait(false))).RequireAuthorization(PlanningPermissions.ReportReview);
        group.MapGet("/report-list/schedules/export",async([AsParameters]ReportScheduleQuery query,IPlanningStore store,CancellationToken ct)=>Results.File(await store.ExportReviewedSchedulesAsync(query,ct).ConfigureAwait(false),"text/csv; charset=utf-8","Horarios chequeados.csv"));
        group.MapGet("/report-list/closures",async([AsParameters]ReportScheduleQuery query,IPlanningStore store,CancellationToken ct)=>Results.Ok(await store.ReportClosuresAsync(query,ct).ConfigureAwait(false)));
        group.MapGet("/closures",async([AsParameters]ReportClosureQuery query,IPlanningStore store,CancellationToken ct)=>Results.Ok(await store.ClosureWorkspaceAsync(query,ct).ConfigureAwait(false)));
        group.MapGet("/closures/detail",async(string job,string fingerprint,IPlanningStore store,CancellationToken ct)=>Results.Ok(await store.ClosureDetailAsync(job,fingerprint,ct).ConfigureAwait(false)));
        group.MapGet("/closures/export",async([AsParameters]ReportClosureQuery query,IPlanningStore store,CancellationToken ct)=>Results.File(await store.ExportClosuresAsync(query,ct).ConfigureAwait(false),"text/csv; charset=utf-8","Cierres.csv"));
        group.MapPut("/closures/follow-up",async(ReportClosureFollowUp request,IPlanningStore store,HttpContext context,CancellationToken ct)=>Results.Ok(await store.SaveClosureFollowUpAsync(request,Actor(context),ct).ConfigureAwait(false))).RequireAuthorization(PlanningPermissions.ReportReview);
        group.MapPost("/report-list/check",async(ReportListCheck request,IPlanningStore store,HttpContext context,CancellationToken ct)=>
        {
            if(!request.Checked||request.AdminOverride)return Results.Problem(statusCode:403,detail:"Las correcciones requieren permiso de revisión.");
            return Results.Ok(await store.CheckReportAsync(request,Actor(context),false,ct).ConfigureAwait(false));
        }).RequireAuthorization(PlanningPermissions.ReportMark);
        group.MapPost("/report-list/correction",async(ReportListCheck request,IPlanningStore store,HttpContext context,CancellationToken ct)=>
            Results.Ok(await store.CheckReportAsync(request,Actor(context),context.User.IsInRole("Administrador"),ct).ConfigureAwait(false))).RequireAuthorization(PlanningPermissions.ReportReview);
        group.MapPut("/report-list/keep",async(ReportListKeep request,IPlanningStore store,HttpContext context,CancellationToken ct)=>
        {await store.KeepReportAsync(request,Actor(context),ct).ConfigureAwait(false);return Results.NoContent();}).RequireAuthorization(PlanningPermissions.ReportReview);
        group.MapPost("/report-list/scan",async(ReportListScan request,IPlanningStore store,HttpContext context,CancellationToken ct)=>
            Results.Ok(await store.ScanReportAsync(request,Actor(context),ct).ConfigureAwait(false))).RequireAuthorization(PlanningPermissions.ReportMark);
        group.MapPost("/notes",async(PlanningNoteBatch request,IPlanningStore store,HttpContext context,CancellationToken ct)=>Results.Ok(await store.EditNotesAsync(request,Actor(context),ct).ConfigureAwait(false))).RequireAuthorization(PlanningPermissions.EditWip);
        group.MapGet("/export",async([AsParameters]PlanningQuery query,IPlanningStore store,CancellationToken ct)=>Results.File(await store.ExportAsync(query,ct).ConfigureAwait(false),"text/csv; charset=utf-8","Consulta de Jobs.csv"));
        group.MapGet("/jobs/{id:guid}",async(Guid id,IPlanningStore store,CancellationToken ct)=>
        {var dossier=await store.DossierAsync(id,ct).ConfigureAwait(false);return dossier is null?Results.NotFound():Results.Ok(dossier);});
        group.MapPut("/jobs/{id:guid}/wip",async(Guid id,WipEdit request,IPlanningStore store,HttpContext context,CancellationToken ct)=>
        {await store.EditWipAsync(id,request,Actor(context),ct).ConfigureAwait(false);return Results.NoContent();}).RequireAuthorization(PlanningPermissions.EditWip);
        group.MapPut("/jobs/{id:guid}/control",async(Guid id,PlanningControlEdit request,IPlanningStore store,HttpContext context,CancellationToken ct)=>
            Results.Ok(await store.SaveControlAsync(id,request,Actor(context),ct).ConfigureAwait(false))).RequireAuthorization(PlanningPermissions.EditWip);
        group.MapPost("/jobs/{id:guid}/manual-tests",async(Guid id,ManualTestRequest request,IPlanningStore store,HttpContext context,CancellationToken ct)=>
        {await store.AddManualTestAsync(id,request,Actor(context),ct).ConfigureAwait(false);return Results.NoContent();}).RequireAuthorization(PlanningPermissions.ManualTests);
        group.MapPut("/incidents/{id:guid}",async(Guid id,IncidentDecision request,IPlanningStore store,HttpContext context,CancellationToken ct)=>
        {await store.DecideIncidentAsync(id,request,Actor(context),ct).ConfigureAwait(false);return Results.NoContent();}).RequireAuthorization(PlanningPermissions.EditWip);
        group.MapGet("/imports",async(IPlanningStore store,CancellationToken ct)=>Results.Ok(await store.BatchesAsync(ct).ConfigureAwait(false)));
        group.MapGet("/imports/{id:guid}",async(Guid id,IPlanningStore store,CancellationToken ct)=>
        {var preview=await store.BatchPreviewAsync(id,ct).ConfigureAwait(false);return preview is null?Results.NotFound():Results.Ok(preview);}).RequireAuthorization(PlanningPermissions.Import);
        group.MapGet("/reviews",async(string? category,string? search,int? page,int? pageSize,IPlanningStore store,CancellationToken ct)=>Results.Ok(await store.ReviewPageAsync(category,search,page??1,pageSize??200,ct).ConfigureAwait(false)));
        group.MapGet("/reviews/workspace",async([AsParameters]PlanningReviewQuery query,IPlanningStore store,CancellationToken ct)=>Results.Ok(await store.ReviewWorkspaceAsync(query,ct).ConfigureAwait(false)));
        group.MapPut("/reviews/decision",async(PlanningReviewDecision request,IPlanningStore store,HttpContext context,CancellationToken ct)=>{await store.DecideReviewAsync(request,Actor(context),ct).ConfigureAwait(false);return Results.NoContent();}).RequireAuthorization(PlanningPermissions.EditWip);
        group.MapGet("/imports/{id:guid}/inspection",async(Guid id,IPlanningStore store,CancellationToken ct)=>{var result=await store.InspectImportAsync(id,ct).ConfigureAwait(false);return result is null?Results.NotFound():Results.Ok(result);}).RequireAuthorization(PlanningPermissions.Import);
        group.MapGet("/reviews/export",async([AsParameters]PlanningReviewQuery query,IPlanningStore store,CancellationToken ct)=>Results.File(await store.ExportReviewsAsync(query,ct).ConfigureAwait(false),"text/csv; charset=utf-8","Revision de importaciones.csv"));
        group.MapPost("/reviews/export",async(PlanningReviewQuery query,IPlanningStore store,CancellationToken ct)=>Results.File(await store.ExportReviewsAsync(query,ct).ConfigureAwait(false),"text/csv; charset=utf-8","Revision de importaciones.csv"));
        group.MapPost("/imports/preview",Preview).RequireAuthorization(PlanningPermissions.Import);
        group.MapPost("/imports/supplement/preview",PreviewSupplement).RequireAuthorization(PlanningPermissions.Import);
        group.MapPost("/imports/react/preview",async(ReactTableReviewRequest request,IPlanningStore store,HttpContext context,CancellationToken ct)=>Results.Ok(await store.PreviewReactTablesAsync(Actor(context),request.Day,ct).ConfigureAwait(false))).RequireAuthorization(PlanningPermissions.Import);
        group.MapPost("/imports/{id:guid}/confirm",async(Guid id,IPlanningStore store,HttpContext context,CancellationToken ct)=>Results.Ok(await store.ConfirmAsync(id,Actor(context),ct).ConfigureAwait(false))).RequireAuthorization(PlanningPermissions.Import);
        group.MapGet("/equivalences",async(IPlanningStore store,CancellationToken ct)=>Results.Ok(await store.EquivalencesAsync(ct).ConfigureAwait(false)));
        group.MapGet("/equivalences/legacy",async(IPlanningStore store,CancellationToken ct)=>Results.Ok(await store.LegacyEquivalencesAsync(ct).ConfigureAwait(false))).RequireAuthorization(PlanningPermissions.Equivalences);
        group.MapPost("/equivalences/legacy",async(LegacyEquivalenceConfirmation request,IPlanningStore store,HttpContext context,CancellationToken ct)=>
        {await store.ImportLegacyEquivalencesAsync(request,Actor(context),ct).ConfigureAwait(false);return Results.NoContent();}).RequireAuthorization(PlanningPermissions.Equivalences);
        group.MapPut("/equivalences",async(EquivalenceDecision request,IPlanningStore store,HttpContext context,CancellationToken ct)=>
        {await store.SaveEquivalenceAsync(request,Actor(context),ct).ConfigureAwait(false);return Results.NoContent();}).RequireAuthorization(PlanningPermissions.Equivalences);
        group.MapPost("/source-resolution",async(SourceResolution request,IPlanningStore store,HttpContext context,CancellationToken ct)=>
        {await store.ResolveSourceAsync(request,Actor(context),ct).ConfigureAwait(false);return Results.NoContent();}).RequireAuthorization(PlanningPermissions.ManualTests);
        var admin=group.MapGroup("/access").RequireAuthorization(LimsPolicies.RequireAdministrator);
        admin.MapGet("/users",async(IPlanningStore store,CancellationToken ct)=>Results.Ok(await store.AccessUsersAsync(ct).ConfigureAwait(false)));
        admin.MapPut("/users/{id:int}",async(int id,PlanningAccessChange request,IPlanningStore store,HttpContext context,CancellationToken ct)=>
        {await store.SaveAccessAsync(id,request,Actor(context),ct).ConfigureAwait(false);return Results.NoContent();});
    }
    private static async Task<IResult> Preview(HttpContext context,IPlanningStore store,CancellationToken ct)
    {
        if(!context.Request.HasFormContentType)return Results.BadRequest(new{message="Seleccione Summary y Report List XLSX."});
        var form=await context.Request.ReadFormAsync(ct).ConfigureAwait(false);
        var summary=form.Files.GetFile("summary");var report=form.Files.GetFile("report");
        if(summary is null||report is null||summary.Length is <=0 or >PlanningLimits.MaxFileBytes||report.Length is <=0 or >PlanningLimits.MaxFileBytes)
            return Results.BadRequest(new{message="Se requieren dos XLSX, máximo 5 MiB cada uno."});
        if(!DateOnly.TryParseExact(form["day"],"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day))return Results.BadRequest(new{message="Fecha de corte inválida."});
        using var s=new MemoryStream();using var r=new MemoryStream();
        await summary.CopyToAsync(s,ct).ConfigureAwait(false);await report.CopyToAsync(r,ct).ConfigureAwait(false);
        DateOnly? target=null;
        if(!string.IsNullOrWhiteSpace(form["targetInDate"]))
        {if(!DateOnly.TryParseExact(form["targetInDate"],"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var parsed))return Results.BadRequest(new{message="Fecha de ingreso de pruebas inválida."});target=parsed;}
        var policy=string.IsNullOrWhiteSpace(form["policy"])?"HtmlReact":form["policy"].ToString();
        if(policy!="HtmlReact")return Results.BadRequest(new{message="La importación requiere las reglas de Consulta de Jobs y React."});
        var input=PlanningImportReader.Read(summary.FileName,s.ToArray(),report.FileName,r.ToArray(),new(form["window"].ToString(),day,policy,target),ct);
        return Results.Ok(await store.PreviewAsync(input,Actor(context),ct).ConfigureAwait(false));
    }
    private static async Task<IResult> PreviewSupplement(HttpContext context,IPlanningStore store,CancellationToken ct)
    {
        if(!context.Request.HasFormContentType)return Results.BadRequest(new{message="Seleccione el XLSX complementario."});
        var form=await context.Request.ReadFormAsync(ct).ConfigureAwait(false);var file=form.Files.GetFile("supplement");
        if(file is null||file.Length is <=0 or >PlanningLimits.MaxFileBytes)return Results.BadRequest(new{message="Se requiere un XLSX complementario de máximo 5 MiB."});
        if(!DateOnly.TryParseExact(form["day"],"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day))return Results.BadRequest(new{message="Día operativo inválido."});
        using var bytes=new MemoryStream();await file.CopyToAsync(bytes,ct).ConfigureAwait(false);
        return Results.Ok(await store.PreviewAsync(PlanningImportReader.ReadSupplement(file.FileName,bytes.ToArray(),day,ct),Actor(context),ct).ConfigureAwait(false));
    }
    private static int Actor(HttpContext context)=>ReferenceMaterials.ReferenceMaterialEndpoints.TryGetActorUserId(context,out var id)?id:throw new PlanningValidationException("Sesión sin identidad válida.");
}
