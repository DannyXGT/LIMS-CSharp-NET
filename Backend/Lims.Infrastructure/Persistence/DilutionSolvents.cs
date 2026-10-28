using Lims.Contracts.ReferencePreparations;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Persistence;

internal static class DilutionSolvents
{
    public static Task<SolventOption[]> CatalogAsync(LimsDbContext db, CancellationToken ct) =>
        db.ReferenceSolvents.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.DisplayOrder)
            .Select(s => new SolventOption(s.Id, s.Name, s.Abbreviation, s.IsActive, s.DisplayOrder)).ToArrayAsync(ct);
}
