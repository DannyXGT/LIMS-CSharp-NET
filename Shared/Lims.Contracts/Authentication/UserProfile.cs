namespace Lims.Contracts.Authentication;

public sealed record UserProfile(
    int Id,
    string Name,
    string Email,
    string Role,
    string? Department,
    IReadOnlyList<string> Permissions);
