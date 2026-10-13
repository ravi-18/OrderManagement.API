namespace OrderManagement.API.Domain;

public static class OrderStatusTransitions
{
    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> AllowedTargets =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
            [OrderStatus.Confirmed] = [OrderStatus.Shipped, OrderStatus.Cancelled],
            [OrderStatus.Shipped] = [OrderStatus.Delivered],
            [OrderStatus.Delivered] = [],
            [OrderStatus.Cancelled] = [],
        };

    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        AllowedTargets[from].Contains(to);

    public static IReadOnlyCollection<OrderStatus> GetAllowedTargets(OrderStatus from) =>
        AllowedTargets[from];
}
