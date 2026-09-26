namespace Lims.Contracts.Authentication;

public sealed record LoginRequest(string Identifier, string Password, string? ClientName = null);
