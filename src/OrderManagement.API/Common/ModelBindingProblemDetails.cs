using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.API.Domain;

namespace OrderManagement.API.Common;

/// <summary>
/// Makes framework-generated 400s (malformed JSON, wrong types, bad query values) use the same
/// ProblemDetails shape as every other error: <c>type</c>/<c>title</c>/<c>detail</c>, <c>code: validation</c>,
/// plus an <c>errors</c> map of field name to messages.
/// </summary>
public static partial class ModelBindingProblemDetails
{
    private const string InvalidBodyMessage = "Request body is missing or is not valid JSON.";

    public static IMvcBuilder AddUniformValidationProblems(this IMvcBuilder builder) =>
        builder.ConfigureApiBehaviorOptions(options =>
            options.InvalidModelStateResponseFactory = context =>
                new ObjectResult(FromModelState(context.ModelState))
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    ContentTypes = { "application/problem+json" }
                });

    public static ProblemDetails FromModelState(ModelStateDictionary modelState)
    {
        var errors = new Dictionary<string, string[]>();
        var hasMalformedBody = false;

        foreach (var (rawKey, entry) in modelState)
        {
            if (entry.Errors.Count == 0)
            {
                continue;
            }

            var key = NormalizeKey(rawKey);
            if (key == "$")
            {
                // A [JsonRequired] miss surfaces as a body-level error even though the JSON is valid.
                var missing = entry.Errors.Select(e => MissingRequiredRegex().Match(e.ErrorMessage)).FirstOrDefault(m => m.Success);
                if (missing is null)
                {
                    hasMalformedBody = true;
                }
                else
                {
                    errors[missing.Groups["name"].Value] = [$"{missing.Groups["name"].Value} is required."];
                }

                continue;
            }

            // MVC adds a "The request field is required." entry for the body parameter whenever
            // deserialization fails; it only repeats the real error and leaks the parameter name.
            if (key == "request" && entry.Errors.All(e => e.ErrorMessage.EndsWith("field is required.", StringComparison.Ordinal)))
            {
                hasMalformedBody = hasMalformedBody || modelState.Count == 1;
                continue;
            }

            errors[key] = entry.Errors.Select(e => Friendly(e)).Distinct().ToArray();
        }

        if (hasMalformedBody && errors.Count == 0)
        {
            errors[""] = [InvalidBodyMessage];
        }

        var detail = errors.Count == 0
            ? InvalidBodyMessage
            : "Request validation failed: " + string.Join(
                "; ",
                errors.Select(kv => kv.Key.Length == 0 ? kv.Value[0] : $"{kv.Key}: {string.Join(" ", kv.Value)}"));

        return ProblemDetailsMapper.Validation(detail, errors);
    }

    private static string NormalizeKey(string key) =>
        key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;

    private static string Friendly(ModelError error)
    {
        var message = string.IsNullOrEmpty(error.ErrorMessage) ? error.Exception?.Message ?? string.Empty : error.ErrorMessage;
        var match = ConversionRegex().Match(message);
        if (!match.Success)
        {
            return message;
        }

        return match.Groups["type"].Value switch
        {
            "System.Guid" => "Value must be a GUID, e.g. aaaaaaaa-0001-4000-8000-000000000002.",
            "System.Int32" => "Value must be a whole number.",
            "System.DateTimeOffset" => "Value must be an ISO 8601 date-time, e.g. 2026-01-31T00:00:00Z.",
            var t when t == typeof(OrderStatus).FullName =>
                $"Value must be one of: {string.Join(", ", Enum.GetNames<OrderStatus>())}.",
            _ => "Value has the wrong type or format."
        };
    }

    [GeneratedRegex(@"could not be converted to (?<type>[\w.+]*\w)")]
    private static partial Regex ConversionRegex();

    [GeneratedRegex(@"missing required properties including: '(?<name>\w+)'")]
    private static partial Regex MissingRequiredRegex();
}
