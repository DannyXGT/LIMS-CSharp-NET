namespace Lims.Contracts.Errors;

public sealed record ApiError(
    string Code,
    string Message,
    string CorrelationId,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);
