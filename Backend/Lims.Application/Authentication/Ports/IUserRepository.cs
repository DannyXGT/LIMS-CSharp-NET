using Lims.Domain.Identity;

namespace Lims.Application.Authentication.Ports;

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    Task<User?> FindByIdAsync(int userId, CancellationToken cancellationToken);

    Task UpdatePasswordHashAsync(User user, string passwordHash, CancellationToken cancellationToken);
}
