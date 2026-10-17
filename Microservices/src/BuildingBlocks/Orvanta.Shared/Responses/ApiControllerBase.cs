using Microsoft.AspNetCore.Mvc;

namespace Orvanta.Shared.Responses;

/// <summary>
/// Base controller that always writes <see cref="ResponseCls{T}"/> with the
/// HTTP status matching <c>StatusCode</c>.
/// </summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult<ResponseCls<T>> Respond<T>(ServiceResult<T> result) =>
        StatusCode(result.StatusCode, result.ToResponse());

    protected ActionResult<ResponseCls<T>> Respond<T>(ResponseCls<T> response) =>
        StatusCode(response.StatusCode, response);
}
