using OrderManagement.API.Contracts.Responses;
using OrderManagement.API.Domain;

namespace OrderManagement.API.Services;

internal static class OrderMapper
{
    public static OrderResponse ToResponse(Order order) => new()
    {
        Id = order.Id,
        CustomerId = order.CustomerId,
        ShippingAddress = order.ShippingAddress,
        Status = order.Status.ToString(),
        CreatedAt = order.CreatedAt,
        UpdatedAt = order.UpdatedAt,
        Items = order.Items.Select(item => new OrderItemResponse
        {
            ProductId = item.ProductId,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice
        }).ToList()
    };
}
