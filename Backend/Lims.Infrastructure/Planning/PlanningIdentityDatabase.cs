using Lims.Application.Planning;
using Npgsql;

namespace Lims.Infrastructure.Planning;

/// <summary>Uses the host identity database; never stores login credentials in Planning.</summary>
public sealed class PlanningIdentityDatabase(string connectionString) : IAsyncDisposable
{
    public NpgsqlDataSource Source { get; } = NpgsqlDataSource.Create(connectionString);

    public async Task RegisterActorAsync(PlanningDatabase planning, int id, CancellationToken ct)
    {
        await using var host = await Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var user = new NpgsqlCommand("SELECT nombre FROM public.usuarios WHERE id=@id AND activo", host);
        user.Parameters.AddWithValue("id", id);
        var name = await user.ExecuteScalarAsync(ct).ConfigureAwait(false) as string
            ?? throw new PlanningValidationException("Usuario inexistente o inactivo en el LIMS.");
        await using var target = await planning.Source.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var register = new NpgsqlCommand("""
            INSERT INTO planning.actors(id,nombre) VALUES(@id,@name)
            ON CONFLICT(id) DO UPDATE SET nombre=EXCLUDED.nombre
            WHERE planning.actors.nombre IS DISTINCT FROM EXCLUDED.nombre
            """, target);
        register.Parameters.AddWithValue("id", id);
        register.Parameters.AddWithValue("name", name);
        await register.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => Source.DisposeAsync();
}
