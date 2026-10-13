namespace OrderManagement.API.Contracts.Responses;

public sealed record OrderResponse
{
    public Guid Id { get; init; }
    public Guid CustomerId { get; init; }
    public required string ShippingAddress { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public required IReadOnlyList<OrderItemResponse> Items { get; init; }
}

public sealed record OrderItemResponse
{
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}
