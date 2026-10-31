using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace OrderManagement.API.OpenApi;

/// <summary>Documents the Idempotency-Key header as required (the controller validates it manually, so
/// the binder-derived schema would otherwise mark it optional).</summary>
public sealed class IdempotencyKeyHeaderFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var parameter = operation.Parameters?.OfType<OpenApiParameter>()
            .FirstOrDefault(p => p.In == ParameterLocation.Header && p.Name == "Idempotency-Key");
        if (parameter is null)
        {
            return;
        }

        parameter.Required = true;
        parameter.Description =
            "Client-chosen unique key (1-256 chars). Retrying with the same key and body replays the original 201; " +
            "the same key with a different body returns 422.";
        parameter.Example = JsonValue.Create("demo-create-1");
    }
}
