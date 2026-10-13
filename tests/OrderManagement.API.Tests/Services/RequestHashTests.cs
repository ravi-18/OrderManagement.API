using OrderManagement.API.Contracts.Requests;
using OrderManagement.API.Services;

namespace OrderManagement.API.Tests.Services;

public sealed class RequestHashTests
{
    [Fact]
    public void Compute_SamePayload_IsStable()
    {
        var request = Sample();

        var first = RequestHash.Compute(request);
        var second = RequestHash.Compute(request);

        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.Equal(first, second);
    }

    [Fact]
    public void Compute_DifferentPayload_Differs()
    {
        var a = Sample();
        var b = Sample() with { ShippingAddress = "Different street" };

        Assert.NotEqual(RequestHash.Compute(a), RequestHash.Compute(b));
    }

    private static CreateOrderRequest Sample() => new()
    {
        CustomerId = Guid.Parse("bbbbbbbb-0001-4000-8000-000000000001"),
        ShippingAddress = "Jl. Sudirman 1",
        Items = [new CreateOrderItemRequest { ProductId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001"), Quantity = 2 }]
    };
}
