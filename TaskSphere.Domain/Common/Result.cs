namespace TaskSphere.Domain.Common;

/// <summary>
/// Represents the result of an operation, including success/failure, a value, and errors.
/// </summary>
public class Result<T>
{
    private readonly List<Error> _errors = new();

    public bool IsSuccess { get; }
    public T? Value { get; }
    public IReadOnlyList<Error> Errors => _errors.AsReadOnly();

    private Result(bool isSuccess, T? value, IReadOnlyList<Error> errors)
    {
        IsSuccess = isSuccess;
        Value = value;

        if (errors.Any())
            _errors.AddRange(errors);
    }

    /// <summary>
    /// Creates a successful result with the specified value.
    /// </summary>
    public static Result<T> Success(T? value = default) =>
        new Result<T>(true, value, new List<Error>());

    /// <summary>
    /// Creates a failed result with a general error message.
    /// </summary>
    public static Result<T> Failure(string error) =>
        new Result<T>(false, default, new List<Error> { new Error("General.Error", error) });

    /// <summary>
    /// Creates a failed result with one or more specific errors.
    /// </summary>
    public static Result<T> Failure(params Error[] errors) =>
        new Result<T>(false, default, errors);

    /// <summary>
    /// Chains a next operation if this result is successful. Propagates errors on failure.
    /// </summary>
    public Result<TOut> Then<TOut>(Func<T, Result<TOut>> next)
        => IsSuccess ? next(Value!) : Result<TOut>.Failure([.. Errors]);

    /// <summary>
    /// Chains an async next operation if this result is successful. Propagates errors on failure.
    /// </summary>
    public async Task<Result<TOut>> ThenAsync<TOut>(Func<T, Task<Result<TOut>>> next)
        => IsSuccess ? await next(Value!) : Result<TOut>.Failure([.. Errors]);
}

/// <summary>
/// Represents the result of a void operation (no return value).
/// </summary>
public class Result
{
    private readonly List<Error> _errors = new();

    public bool IsSuccess { get; }
    public IReadOnlyList<Error> Errors => _errors.AsReadOnly();

    private Result(bool isSuccess, IReadOnlyList<Error> errors)
    {
        IsSuccess = isSuccess;
        if (errors.Any())
            _errors.AddRange(errors);
    }

    public static Result Success() => new(true, new List<Error>());

    public static Result Failure(string error) =>
        new(false, new List<Error> { new Error("General.Error", error) });

    public static Result Failure(params Error[] errors) => new(false, errors);

    /// <summary>
    /// Converts this void result into a typed result on success using the provided factory.
    /// </summary>
    public Result<T> Then<T>(Func<Result<T>> next)
        => IsSuccess ? next() : Result<T>.Failure([.. Errors]);
}