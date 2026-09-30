using Lims.Contracts.Errors;

namespace Lims.Desktop.Http;

public sealed record ApiCallResult<T>(
    bool IsSuccess,
    T? Value,
    ApiError? Error,
    int StatusCode);
