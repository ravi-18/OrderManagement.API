using System.Text.Json.Serialization;
using OrderManagement.API.Domain;

namespace OrderManagement.API.Contracts.Requests;

public sealed record UpdateOrderStatusRequest
{
    [JsonRequired]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OrderStatus Status { get; init; }
}
