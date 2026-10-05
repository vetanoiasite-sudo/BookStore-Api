using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Common;

/// <summary>
/// Base for every controller. Controllers stay thin: they bind input, call one
/// application service and wrap the result in <see cref="ApiResponse"/>.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult<ApiResponse<T>> Success<T>(T data, string? message = null) =>
        Ok(ApiResponse<T>.Ok(data, message));

    protected ActionResult<ApiResponse> Success(string? message = null) =>
        Ok(ApiResponse.Ok(message));

    protected ActionResult<ApiResponse<T>> CreatedResource<T>(string location, T data, string? message = null) =>
        Created(location, ApiResponse<T>.Ok(data, message));
}
