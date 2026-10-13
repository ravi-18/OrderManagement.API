using OrderManagement.API.Domain;

namespace OrderManagement.API.Tests.Domain;

public sealed class OrderStatusTransitionsTests
{
    public static IEnumerable<object[]> AllFromToPairs()
    {
        foreach (OrderStatus from in Enum.GetValues<OrderStatus>())
        {
            foreach (OrderStatus to in Enum.GetValues<OrderStatus>())
            {
                yield return [from, to];
            }
        }
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Shipped, true)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Delivered, true)]
    public void CanTransition_FRDValidTransitions_ReturnsTrue(
        OrderStatus from,
        OrderStatus to,
        bool expected)
    {
        Assert.Equal(expected, OrderStatusTransitions.CanTransition(from, to));
    }

    [Theory]
    [MemberData(nameof(AllFromToPairs))]
    public void CanTransition_MatchesFrdMatrix(OrderStatus from, OrderStatus to)
    {
        var expected = IsFrdAllowedTransition(from, to);
        Assert.Equal(expected, OrderStatusTransitions.CanTransition(from, to));
    }

    [Theory]
    [InlineData(OrderStatus.Pending, new[] { OrderStatus.Confirmed, OrderStatus.Cancelled })]
    [InlineData(OrderStatus.Confirmed, new[] { OrderStatus.Shipped, OrderStatus.Cancelled })]
    [InlineData(OrderStatus.Shipped, new[] { OrderStatus.Delivered })]
    [InlineData(OrderStatus.Delivered, new OrderStatus[] { })]
    [InlineData(OrderStatus.Cancelled, new OrderStatus[] { })]
    public void GetAllowedTargets_ReturnsFrdTargets(OrderStatus from, OrderStatus[] expected)
    {
        var targets = OrderStatusTransitions.GetAllowedTargets(from);
        Assert.Equal(expected, targets);
    }

    [Fact]
    public void GetAllowedTargets_TerminalStates_ReturnEmptyCollections()
    {
        Assert.Empty(OrderStatusTransitions.GetAllowedTargets(OrderStatus.Delivered));
        Assert.Empty(OrderStatusTransitions.GetAllowedTargets(OrderStatus.Cancelled));
    }

    private static bool IsFrdAllowedTransition(OrderStatus from, OrderStatus to) =>
        (from, to) switch
        {
            (OrderStatus.Pending, OrderStatus.Confirmed) => true,
            (OrderStatus.Pending, OrderStatus.Cancelled) => true,
            (OrderStatus.Confirmed, OrderStatus.Shipped) => true,
            (OrderStatus.Confirmed, OrderStatus.Cancelled) => true,
            (OrderStatus.Shipped, OrderStatus.Delivered) => true,
            _ => false,
        };
}
