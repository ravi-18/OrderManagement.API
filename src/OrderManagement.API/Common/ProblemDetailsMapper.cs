using Microsoft.AspNetCore.Mvc;
using DomainValidationException = OrderManagement.API.Common.ValidationException;

namespace OrderManagement.API.Common;

public static class ProblemDetailsMapper
{
    public static ProblemDetails FromException(Exception exception) =>
        exception switch
        {
            NotFoundException ex => Create(StatusCodes.Status404NotFound, "Not Found", ex.Message, "not_found"),
            ConflictException ex => Create(StatusCodes.Status409Conflict, "Conflict", ex.Message, "conflict"),
            DomainValidationException ex => Create(StatusCodes.Status400BadRequest, "Bad Request", ex.Message, "validation"),
            BusinessRuleException ex => Create(
                StatusCodes.Status422UnprocessableEntity,
                "Unprocessable Entity",
                ex.Message,
                "business_rule"),
            // Raised by Kestrel while reading the body, e.g. over MaxRequestBodySize.
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } ex => Create(
                StatusCodes.Status413PayloadTooLarge,
                "Payload Too Large",
                ex.Message,
                "payload_too_large"),
            BadHttpRequestException ex => Create(ex.StatusCode, "Bad Request", ex.Message, "bad_request"),
            _ => Unexpected()
        };

    public static ProblemDetails Validation(string detail, IReadOnlyDictionary<string, string[]>? errors)
    {
        var problem = Create(StatusCodes.Status400BadRequest, "Bad Request", detail, "validation");
        if (errors is { Count: > 0 })
        {
            problem.Extensions["errors"] = errors;
        }

        return problem;
    }

    public static ProblemDetails FromError(Error error) =>
        error.Code switch
        {
            "not_found" => Create(StatusCodes.Status404NotFound, "Not Found", DetailOrMessage(error), error.Code),
            "conflict" => Create(StatusCodes.Status409Conflict, "Conflict", DetailOrMessage(error), error.Code),
            "validation" => Validation(DetailOrMessage(error), error.Errors),
            "business_rule" => Create(
                StatusCodes.Status422UnprocessableEntity,
                "Unprocessable Entity",
                DetailOrMessage(error),
                error.Code),
            _ => Unexpected()
        };

    public static ProblemDetails RateLimited() =>
        Create(StatusCodes.Status429TooManyRequests, "Too Many Requests", "Too many requests. Retry later.", "rate_limited");

    private static string DetailOrMessage(Error error) =>
        string.IsNullOrWhiteSpace(error.Detail) ? error.Message : error.Detail;

    private static ProblemDetails Unexpected() =>
        Create(
            StatusCodes.Status500InternalServerError,
            "Internal Server Error",
            "An unexpected error occurred.",
            "internal_error");

    private static ProblemDetails Create(int status, string title, string detail, string code) =>
        new()
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.io/{status}",
            Extensions = { ["code"] = code }
        };
}
