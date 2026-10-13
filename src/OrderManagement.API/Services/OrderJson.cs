using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderManagement.API.Services;

internal static class OrderJson
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNameCaseInsensitive = true
    };
}
