namespace Lims.Desktop.Http;

public sealed record ApiOperationFailure(string Message, string? SupportId = null);
