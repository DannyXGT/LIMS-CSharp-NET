namespace Lims.Application.Common;

public sealed record OperationError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);

public sealed class OperationResult<T>
{
    internal OperationResult(T? value, OperationError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public OperationError? Error { get; }

    public bool IsSuccess => Error is null;

}

public static class OperationResult
{
    public static OperationResult<T> Success<T>(T value) => new(value, null);

    public static OperationResult<T> Failure<T>(OperationError error) => new(default, error);
}
