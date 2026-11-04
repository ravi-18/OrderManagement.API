using OrderManagement.API.Common;
using OrderManagement.API.Contracts.Responses;

namespace OrderManagement.API.Services;

/// <summary>
/// Turns a stored idempotency response (status + body) back into a <see cref="Result{T}"/>.
/// </summary>
internal static class IdempotencyReplay
{
    public static Result<OrderResponse> ToResult(int? responseStatus, string? responseBody)
    {
        if (responseStatus is not StatusCodes.Status201Created)
        {
            return Result.Failure<OrderResponse>(MapStoredStatus(responseStatus));
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return Result.Failure<OrderResponse>(
                Error.Conflict("A request with this Idempotency-Key is already in progress."));
        }

        var cached = System.Text.Json.JsonSerializer.Deserialize<OrderResponse>(responseBody, OrderJson.SerializerOptions);
        return cached is null
            ? Result.Failure<OrderResponse>(Error.Conflict("Stored idempotent response could not be replayed."))
            : Result.Success(cached);
    }

    private static Error MapStoredStatus(int? status) => status switch
    {
        StatusCodes.Status400BadRequest =>
            Error.Validation("Stored idempotent response indicates a validation failure."),
        StatusCodes.Status404NotFound =>
            Error.NotFound("Stored idempotent response indicates the resource was not found."),
        StatusCodes.Status409Conflict =>
            Error.Conflict("Stored idempotent response indicates a conflict."),
        StatusCodes.Status422UnprocessableEntity =>
            Error.BusinessRule("Stored idempotent response indicates a business rule violation."),
        _ => Error.Conflict("Stored idempotent response could not be replayed with the original status.")
    };
}
