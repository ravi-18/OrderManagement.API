namespace OrderManagement.API.Common;

public sealed record Error(string Code, string Message, string? Detail = null)
{
    public static Error NotFound(string message, string? detail = null) =>
        new("not_found", message, detail);

    public static Error Conflict(string message, string? detail = null) =>
        new("conflict", message, detail);

    public static Error Validation(string message, string? detail = null) =>
        new("validation", message, detail);

    public static Error BusinessRule(string message, string? detail = null) =>
        new("business_rule", message, detail);
}
