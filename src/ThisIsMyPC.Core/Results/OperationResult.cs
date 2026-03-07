namespace ThisIsMyPC.Core.Results;

public record OperationResult<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public string? ErrorMessage { get; init; }
    public ErrorCategory? ErrorCategory { get; init; }
    public Exception? Exception { get; init; }

#pragma warning disable CA1000 // Static factory methods on generic type are the prescribed API
    public static OperationResult<T> Success(T value) => new() { IsSuccess = true, Value = value };
    public static OperationResult<T> Failure(string message, ErrorCategory category, Exception? ex = null)
        => new() { IsSuccess = false, ErrorMessage = message, ErrorCategory = category, Exception = ex };
#pragma warning restore CA1000
}
