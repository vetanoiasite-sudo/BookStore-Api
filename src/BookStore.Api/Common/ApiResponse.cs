using System.Text.Json.Serialization;

namespace BookStore.Api.Common;

/// <summary>
/// The single response envelope used by every endpoint, success or failure.
/// Clients can therefore branch on one shape only.
/// </summary>
public class ApiResponse
{
    public bool Success { get; init; }

    public string? Message { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyCollection<ApiError>? Errors { get; init; }

    public static ApiResponse Ok(string? message = null) =>
        new() { Success = true, Message = message };

    public static ApiResponse Fail(string message, IReadOnlyCollection<ApiError>? errors = null) =>
        new() { Success = false, Message = message, Errors = errors ?? [] };
}

/// <summary>Envelope carrying a payload.</summary>
public sealed class ApiResponse<T> : ApiResponse
{
    /// <summary>
    /// The payload. Always written, even when null, because omitting it is not open to
    /// a payload that is a value type: "no data" and <c>false</c> would then look the
    /// same on the wire, and a client reading a boolean answer could not tell them
    /// apart.
    /// </summary>
    public T? Data { get; init; }

    public static ApiResponse<T> Ok(T data, string? message = null) =>
        new() { Success = true, Data = data, Message = message };
}

/// <summary>
/// One error entry. <paramref name="Field"/> is null for errors that are not
/// tied to a specific input field.
/// </summary>
/// <param name="Code">Stable machine-readable code.</param>
/// <param name="Message">Human readable message.</param>
/// <param name="Field">Camel-cased field name, when applicable.</param>
public sealed record ApiError(string Code, string Message, string? Field = null);
