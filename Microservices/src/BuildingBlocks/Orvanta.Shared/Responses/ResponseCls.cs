using System.Text.Json.Serialization;

namespace Orvanta.Shared.Responses;

/// <summary>
/// Common API response envelope used by every Orvanta microservice.
/// Every endpoint returns this shape so clients never depend on a
/// service-specific contract:
/// <code>
/// { "statusCode": 200, "statusMessage": "...", "statusObject": { ... } }
/// </code>
/// </summary>
public class ResponseCls<T>
{
    /// <summary>HTTP-aligned status code (200, 201, 400, 401, 404, 409, 500...).</summary>
    public int StatusCode { get; init; }

    /// <summary>Human readable message that the UI can show directly.</summary>
    public string StatusMessage { get; init; } = string.Empty;

    /// <summary>Payload. Null for failures or for operations without a result.</summary>
    public T? StatusObject { get; init; }

    [JsonIgnore]
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    public static ResponseCls<T> Success(T? statusObject, string statusMessage = "Success", int statusCode = 200) =>
        new() { StatusCode = statusCode, StatusMessage = statusMessage, StatusObject = statusObject };

    public static ResponseCls<T> Failure(int statusCode, string statusMessage, T? statusObject = default) =>
        new() { StatusCode = statusCode, StatusMessage = statusMessage, StatusObject = statusObject };
}

/// <summary>Non-generic variant for responses without a payload.</summary>
public sealed class ResponseCls : ResponseCls<object>
{
    public static ResponseCls Ok(string statusMessage = "Success") =>
        new() { StatusCode = 200, StatusMessage = statusMessage };

    public static ResponseCls Fail(int statusCode, string statusMessage, object? statusObject = null) =>
        new() { StatusCode = statusCode, StatusMessage = statusMessage, StatusObject = statusObject };
}
