namespace OrderManagement.API.Contracts.Requests;

public sealed record CreateOrderRequest
{
    public Guid CustomerId { get; init; }
    public string ShippingAddress { get; init; } = string.Empty;
    public List<CreateOrderItemRequest> Items { get; init; } = [];
}

public sealed record CreateOrderItemRequest
{
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }
}
