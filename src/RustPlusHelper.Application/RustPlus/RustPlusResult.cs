namespace RustPlusHelper.Application.RustPlus;

public sealed record RustPlusError(string Code, string Message);

public sealed record RustPlusResult<T>
{
    private RustPlusResult(bool isSuccess, T? data, RustPlusError? error)
    {
        IsSuccess = isSuccess;
        Data = data;
        Error = error;
    }

    public bool IsSuccess { get; }

    public T? Data { get; }

    public RustPlusError? Error { get; }

    public static RustPlusResult<T> Success(T data) => new(true, data, null);

    public static RustPlusResult<T> Failure(string code, string message) =>
        new(false, default, new RustPlusError(code, message));
}
