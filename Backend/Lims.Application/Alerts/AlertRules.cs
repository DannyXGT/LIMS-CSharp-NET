using Lims.Contracts.Alerts;

namespace Lims.Application.Alerts;

public sealed record AlertSource(Guid Id, string SourceType, AlertModule Modules, int DepartmentId, string Name,
    string Code, string Lot, string MaterialType, decimal? Percent, decimal Available, string Unit,
    DateOnly Expiration, bool Operational, string Search = "");
public sealed record AlertCondition(string Category, string Code, AlertPriority Priority, string Title,
    string Description, string Recommendation);

public static class AlertRules
{
    public static string? Validate(AlertSettings s)
    {
        if (s.ExpirationDays is null || s.ExpirationDays.Length is < 1 or > 12 || s.ExpirationDays.Any(d => d is < 1 or > 3660) ||
            s.ExpirationDays.Distinct().Count() != s.ExpirationDays.Length) return "Defina intervalos únicos entre 1 y 3660 días.";
        if (s.CriticalPercent <= 0 || s.LowPercent <= s.CriticalPercent || s.LowPercent > 100)
            return "El umbral crítico debe ser mayor que 0 y menor que el bajo; el bajo no debe superar 100 %.";
        if (!s.InternalEnabled) return "El centro interno debe permanecer habilitado para conservar la trazabilidad.";
        if (s.DailyHour is < 0 or > 23 || s.DailyMinute is < 0 or > 59) return "Revise el horario del resumen.";
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(s.TimeZoneId); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { return "Seleccione una zona horaria válida."; }
        if (s.Scopes is null || s.Recipients is null || s.Scopes.Count > 5 || s.Recipients.Count > 5000 ||
            s.Scopes.Any(a => !Enum.IsDefined(a.Module) || a.Module == AlertModule.None || a.DepartmentId <= 0) ||
            s.Scopes.Select(a => a.Module).Distinct().Count() != s.Scopes.Count ||
            s.Recipients.Select(r => r.UserId).Distinct().Count() != s.Recipients.Count)
            return "Revise los ámbitos y destinatarios; no se permiten duplicados.";
        return null;
    }

    public static IReadOnlyList<AlertCondition> Evaluate(AlertSource source, AlertSettings settings, DateOnly today)
    {
        if (!source.Operational) return [];
        var result = new List<AlertCondition>(2);
        var days = source.Expiration.DayNumber - today.DayNumber;
        if (days < 0)
            result.Add(new("Expiration", "Expired", AlertPriority.High, "Material vencido", $"{source.Name}, lote {source.Lot}, venció el {source.Expiration:dd/MM/yyyy}.", "Retirar del uso y gestionar su reemplazo conforme al procedimiento autorizado."));
        else
        {
            var interval = settings.ExpirationDays.Where(d => days <= d).Order().FirstOrDefault();
            if (interval > 0)
                result.Add(new("Expiration", $"Due{interval}", days <= 1 ? AlertPriority.High : days <= 7 ? AlertPriority.Warning : AlertPriority.Information,
                    "Próximo vencimiento", $"{source.Name}, lote {source.Lot}: " + (days == 0 ? "vence hoy." : $"vence en {days} días."), "Revisar el consumo previsto y preparar la reposición antes del vencimiento."));
        }
        if (source.Percent is { } percent && percent <= settings.LowPercent)
        {
            var code = percent <= 0 ? "Depleted" : percent <= settings.CriticalPercent ? "Critical" : "Low";
            var title = code == "Depleted" ? "Material agotado" : code == "Critical" ? "Disponibilidad crítica" : "Disponibilidad baja";
            result.Add(new("Availability", code, code == "Low" ? AlertPriority.Warning : AlertPriority.Critical, title,
                $"{source.Name}, lote {source.Lot}: queda {percent:0.##} % ({source.Available:0.######} {source.Unit}).",
                "Revisar la disponibilidad y gestionar una reposición real; leer el aviso no resuelve esta condición."));
        }
        return result;
    }

    public static string ViewPermission(AlertModule module) => module switch
    {
        AlertModule.Standards => "reference_materials.view", AlertModule.Stock => "stock.view",
        AlertModule.Intermedia => "intermedia.view", AlertModule.Curva => "curva.view", AlertModule.AQS => "aqs.view", _ => ""
    };
    public static bool CanSee(AlertModule source, IEnumerable<string> permissions) =>
        Enum.GetValues<AlertModule>().Any(m => m != AlertModule.None && source.HasFlag(m) && permissions.Contains(ViewPermission(m), StringComparer.Ordinal));
}
