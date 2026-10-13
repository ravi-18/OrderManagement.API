using OrderManagement.API.Contracts.Requests;
using OrderManagement.API.Validators;

namespace OrderManagement.API.Tests.Validators;

public sealed class CreateOrderRequestValidatorTests
{
    private readonly CreateOrderRequestValidator _validator = new();

    [Fact]
    public void Validate_EmptyItems_IsValidationFailure()
    {
        var result = _validator.Validate(ValidRequest() with { Items = [] });

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Code);
        Assert.Contains("item", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_QuantityZeroOrNegative_IsValidationFailure()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            Items = [new CreateOrderItemRequest { ProductId = Guid.NewGuid(), Quantity = 0 }]
        });

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Code);
        Assert.Contains("quantity", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_EmptyAddress_IsValidationFailure()
    {
        var result = _validator.Validate(ValidRequest() with { ShippingAddress = "  " });

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Code);
        Assert.Contains("address", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_EmptyCustomerId_IsValidationFailure()
    {
        var result = _validator.Validate(ValidRequest() with { CustomerId = Guid.Empty });

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Code);
    }

    [Fact]
    public void Validate_ValidRequest_Succeeds()
    {
        var result = _validator.Validate(ValidRequest());

        Assert.True(result.IsSuccess);
    }

    private static CreateOrderRequest ValidRequest() => new()
    {
        CustomerId = Guid.NewGuid(),
        ShippingAddress = "Jl. Sudirman 1",
        Items = [new CreateOrderItemRequest { ProductId = Guid.NewGuid(), Quantity = 2 }]
    };
}
