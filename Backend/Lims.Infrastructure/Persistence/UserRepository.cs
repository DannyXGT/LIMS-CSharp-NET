using Lims.Application.Authentication.Ports;
using Lims.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lims.Infrastructure.Persistence;

public sealed class UserRepository(LimsDbContext dbContext) : IUserRepository
{
    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        dbContext.Users
            .AsSplitQuery()
            .Include(user => user.Role)
            .Include(user => user.Department)
            .Include(user => user.Permissions)
            .SingleOrDefaultAsync(
                user => EF.Functions.ILike(user.Email, normalizedEmail),
                cancellationToken);

    public Task<User?> FindByIdAsync(int userId, CancellationToken cancellationToken) =>
        dbContext.Users
            .AsSplitQuery()
            .Include(user => user.Role)
            .Include(user => user.Department)
            .Include(user => user.Permissions)
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public async Task UpdatePasswordHashAsync(
        User user,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.ReplacePasswordHash(passwordHash);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
