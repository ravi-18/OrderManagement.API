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
            _ => Unexpected()
        };

    public static ProblemDetails FromError(Error error) =>
        error.Code switch
        {
            "not_found" => Create(StatusCodes.Status404NotFound, "Not Found", DetailOrMessage(error), error.Code),
            "conflict" => Create(StatusCodes.Status409Conflict, "Conflict", DetailOrMessage(error), error.Code),
            "validation" => Create(StatusCodes.Status400BadRequest, "Bad Request", DetailOrMessage(error), error.Code),
            "business_rule" => Create(
                StatusCodes.Status422UnprocessableEntity,
                "Unprocessable Entity",
                DetailOrMessage(error),
                error.Code),
            _ => Unexpected()
        };

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
