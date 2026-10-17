namespace Orvanta.Shared.Responses;

/// <summary>
/// Result returned by application services. Controllers translate it into
/// <see cref="ResponseCls{T}"/> so business logic stays free of HTTP concerns.
/// </summary>
public sealed record ServiceResult<T>(int StatusCode, string Message, T? Value = default)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    public static ServiceResult<T> Ok(T value, string message = "Success") => new(200, message, value);
    public static ServiceResult<T> Created(T value, string message = "Created") => new(201, message, value);
    public static ServiceResult<T> Fail(int statusCode, string message, T? value = default) => new(statusCode, message, value);

    public ResponseCls<T> ToResponse() =>
        IsSuccess ? ResponseCls<T>.Success(Value, Message, StatusCode) : ResponseCls<T>.Failure(StatusCode, Message, Value);
}
