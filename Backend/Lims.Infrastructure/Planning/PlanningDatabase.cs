using Npgsql;

namespace Lims.Infrastructure.Planning;

public sealed class PlanningDatabase(string connectionString,string? reportTrackingTable=null) : IAsyncDisposable
{
    public NpgsqlDataSource Source { get; } = NpgsqlDataSource.Create(connectionString);
    public IReadOnlyList<string> ReportTrackingTables {get;}=TrackingTables(reportTrackingTable);
    private static string[] TrackingTables(string? configured)
    {
        var name=string.IsNullOrWhiteSpace(configured)?null:configured.Contains('.')?configured.Trim():"public."+configured.Trim();
        if(name is not null&&!System.Text.RegularExpressions.Regex.IsMatch(name,@"^[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*$"))throw new ArgumentException("Tabla de tracking AR inválida.",nameof(configured));
        return new[]{name,"public.tabla_programacion_analitica","public.tabla_programacion"}.OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
    }
    public ValueTask DisposeAsync() => Source.DisposeAsync();
}
