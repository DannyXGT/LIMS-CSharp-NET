using Lims.Application.Common;
using Lims.Application.Permissions;
using Lims.Contracts.Errors;
using Lims.Contracts.Permissions;
using Lims.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lims.Infrastructure.Persistence;

public sealed class PermissionRepository(LimsDbContext db, PermissionOptions options, TimeProvider clock) : IPermissionRepository
{
    private IQueryable<User> Users => db.Users.AsNoTracking().Include(u => u.Role).Include(u => u.Department);
    private Task<bool> IsAdmin(int actor, CancellationToken ct) => Users.AnyAsync(u => u.Id == actor && u.IsActive && u.Role != null && u.Role.Name == "Administrador", ct);
    private static OperationResult<T> Denied<T>() => OperationResult.Failure<T>(new(ErrorCodes.Forbidden, "No tiene autorización para administrar los permisos de este usuario."));
    private static PermissionUser Display(User u) => new(u.Id, u.Name, u.Email, u.Role?.Name ?? "Sin rol", u.Department!.Name);
    private Task<User?> Eligible(int user, CancellationToken ct) => Users.SingleOrDefaultAsync(u => u.Id == user && u.IsActive && u.Department != null && u.Department.Name == PermissionCatalog.DepartmentName, ct);

    public async Task<OperationResult<PermissionUsers>> UsersAsync(int actor, string? search, CancellationToken ct)
    {
        if (!await IsAdmin(actor, ct).ConfigureAwait(false)) return Denied<PermissionUsers>();
        var exists = await db.Departments.AnyAsync(d => d.Name == PermissionCatalog.DepartmentName, ct).ConfigureAwait(false);
        var query = Users.Where(u => u.IsActive && u.Department != null && u.Department.Name == PermissionCatalog.DepartmentName);
        var users = await query.OrderBy(u => u.Name).ToArrayAsync(ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(search)) users = users.Where(u => u.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) || u.Email.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        return OperationResult.Success(new PermissionUsers(users.Select(Display).ToArray(), exists));
    }
    public async Task<OperationResult<UserPermissionState>> GetAsync(int actor, int user, CancellationToken ct)
    {
        if (!await IsAdmin(actor, ct).ConfigureAwait(false)) return Denied<UserPermissionState>();
        var target = await Eligible(user, ct).ConfigureAwait(false);
        return target is null ? Denied<UserPermissionState>() : OperationResult.Success(await State(target, ct).ConfigureAwait(false));
    }
    private async Task<UserPermissionState> State(User user, CancellationToken ct)
    {
        var set = await db.Set<UserPermissionSet>().AsNoTracking().SingleOrDefaultAsync(p => p.UserId == user.Id, ct).ConfigureAwait(false);
        var grants = await db.Set<UserPermissionAssignment>().AsNoTracking().Where(p => p.UserId == user.Id && p.IsGranted).Select(p => p.Code).ToArrayAsync(ct).ConfigureAwait(false);
        return new(Display(user), grants, set?.Version ?? Guid.Empty, set is not null);
    }
    public async Task<OperationResult<UserPermissionState>> SaveAsync(int actor, int user, SavePermissionsRequest request, CancellationToken ct)
    {
        if (request.Permissions is null || request.Permissions.Count > PermissionCatalog.All.Count ||
            request.Permissions.Distinct(StringComparer.Ordinal).Count() != request.Permissions.Count ||
            request.Permissions.Any(code => !PermissionCatalog.All.Any(p => p.Code == code)))
            return OperationResult.Failure<UserPermissionState>(new(ErrorCodes.ValidationError, "Seleccione permisos válidos, sin duplicados."));
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct).ConfigureAwait(false);
            if (!await IsAdmin(actor, ct).ConfigureAwait(false)) return Denied<UserPermissionState>();
            var target = await Eligible(user, ct).ConfigureAwait(false);
            if (target is null) return Denied<UserPermissionState>();
            var set = await db.Set<UserPermissionSet>().SingleOrDefaultAsync(p => p.UserId == user, ct).ConfigureAwait(false);
            if ((set?.Version ?? Guid.Empty) != request.Version) return Conflict();
            var firstConfiguration = set is null;
            if (set is null) { set = new() { UserId = user }; db.Add(set); }
            var assignments = await db.Set<UserPermissionAssignment>().Where(p => p.UserId == user).ToDictionaryAsync(p => p.Code, ct).ConfigureAwait(false);
            var at = clock.GetUtcNow();
            foreach (var code in PermissionCatalog.All.Select(p => p.Code))
            {
                var before = assignments.TryGetValue(code, out var assignment) && assignment.IsGranted;
                var after = request.Permissions.Contains(code, StringComparer.Ordinal);
                if (before == after && !firstConfiguration) continue;
                if (assignment is null) { assignment = new() { UserId = user, Code = code }; db.Add(assignment); }
                assignment.IsGranted = after; assignment.ActorUserId = actor; assignment.ChangedAt = at;
                // First configuration records every explicit decision, including denied permissions.
                db.Add(new UserPermissionAudit { Id = Guid.NewGuid(), UserId = user, Code = code, ActorUserId = actor, Before = before, After = after, At = at });
            }
            set.Version = Guid.NewGuid();
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return OperationResult.Success(await State(target, ct).ConfigureAwait(false));
            }
            catch (DbUpdateConcurrencyException) { return Conflict(); }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { return Conflict(); }
        }).ConfigureAwait(false);
    }
    private static OperationResult<UserPermissionState> Conflict() => OperationResult.Failure<UserPermissionState>(new(ErrorCodes.Conflict, "Los permisos cambiaron. Recargue el usuario antes de guardar."));
    public async Task<OperationResult<PermissionHistoryPage>> HistoryAsync(int actor, int user, int page, CancellationToken ct)
    {
        if (!await IsAdmin(actor, ct).ConfigureAwait(false) || await Eligible(user, ct).ConfigureAwait(false) is null) return Denied<PermissionHistoryPage>();
        page = Math.Clamp(page, 1, 100000);
        var query = db.Set<UserPermissionAudit>().AsNoTracking().Where(p => p.UserId == user);
        var count = await query.CountAsync(ct).ConfigureAwait(false);
        var entries = await query.OrderByDescending(p => p.At).ThenBy(p => p.Id).Skip((page - 1) * 30).Take(30).ToArrayAsync(ct).ConfigureAwait(false);
        var names = await db.Users.Where(u => entries.Select(p => p.ActorUserId).Contains(u.Id) || u.Id == user).Select(u => new { u.Id, u.Name }).ToDictionaryAsync(u => u.Id, u => u.Name, ct).ConfigureAwait(false);
        return OperationResult.Success(new PermissionHistoryPage(entries.Select(p => new PermissionHistory(p.Code, names[user], names[p.ActorUserId], p.Before == p.After ? "Confirmado" : p.After ? "Otorgado" : "Revocado", p.Before, p.After, p.At)).ToArray(), page, count));
    }
    public async Task<EffectivePermissions?> EffectiveAsync(int user, CancellationToken ct)
    {
        var target = await Users.Include(u => u.Permissions).SingleOrDefaultAsync(u => u.Id == user && u.IsActive, ct).ConfigureAwait(false);
        if (target is null) return null;
        var unrelated = target.Permissions.Select(p => p.Key).Where(code => !PermissionCatalog.All.Any(p => p.Code == code)).ToArray();
        if (target.Role?.Name == "Administrador") return new("Administrador", PermissionCatalog.All.Select(p => p.Code).Concat(unrelated).Distinct(StringComparer.Ordinal).ToArray(), options.EnforceIndividualPermissions);
        var configured = await db.Set<UserPermissionSet>().AnyAsync(p => p.UserId == user, ct).ConfigureAwait(false);
        var granted = configured && target.Department?.Name != PermissionCatalog.DepartmentName ? [] : configured
            ? await db.Set<UserPermissionAssignment>().Where(p => p.UserId == user && p.IsGranted).Select(p => p.Code).ToArrayAsync(ct).ConfigureAwait(false)
            : options.EnforceIndividualPermissions ? [] : Legacy(target.Permissions.Select(p => p.Key));
        return new(target.Role?.Name ?? string.Empty, granted.Concat(unrelated).Distinct(StringComparer.Ordinal).ToArray(), options.EnforceIndividualPermissions || configured);
    }
    private static string[] Legacy(IEnumerable<string> permissions)
    {
        var codes = permissions.ToHashSet(StringComparer.Ordinal);
        if (codes.Contains("reference_materials.view")) { codes.Add(PermissionCatalog.StockView); codes.Add(PermissionCatalog.IntermediateView); }
        if (codes.Contains("reference_materials.create")) { codes.Add(PermissionCatalog.StockCreate); codes.Add(PermissionCatalog.IntermediateCreate); }
        if (codes.Contains("reference_materials.create") && codes.Contains("reference_materials.archive")) codes.Add(PermissionCatalog.Replace);
        return codes.ToArray();
    }
}
