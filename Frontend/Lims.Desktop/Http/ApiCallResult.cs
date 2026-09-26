using Lims.Contracts.Errors;

namespace Lims.Desktop.Http;

internal sealed record ApiCallResult<T>(
    bool IsSuccess,
    T? Value,
    ApiError? Error,
    int StatusCode);
