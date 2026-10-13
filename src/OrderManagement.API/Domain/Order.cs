namespace OrderManagement.API.Domain;

public class Order
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public required string ShippingAddress { get; set; }
    public OrderStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public uint RowVersion { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}
