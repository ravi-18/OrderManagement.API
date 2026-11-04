using System.Text.Json;
using OrderManagement.API.Contracts.Responses;
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
    public void Validate_ShippingAddressOver500_IsValidationFailure()
    {
        var result = _validator.Validate(ValidRequest() with { ShippingAddress = new string('A', 501) });

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Code);
        Assert.Contains("500", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ShippingAddressAt500_Succeeds()
    {
        var result = _validator.Validate(ValidRequest() with { ShippingAddress = new string('A', 500) });

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_MultipleProblems_ReportsAllWithFieldKeys()
    {
        var result = _validator.Validate(new CreateOrderRequest
        {
            CustomerId = Guid.Empty,
            ShippingAddress = " ",
            Items = [new CreateOrderItemRequest { ProductId = Guid.NewGuid(), Quantity = 0 }]
        });

        Assert.True(result.IsFailure);
        var errors = result.Error!.Errors!;
        Assert.Equal(["customerId", "items[0].quantity", "shippingAddress"], errors.Keys.Order().ToArray());
        Assert.Contains("CustomerId", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("ShippingAddress", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ValidRequest_Succeeds()
    {
        var result = _validator.Validate(ValidRequest());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_TooManyItems_IsValidationFailure()
    {
        var result = _validator.Validate(ValidRequest() with { Items = ItemsOfCount(CreateOrderRequestValidator.MaxItems + 1) });

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Code);
        Assert.Contains("items", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_MaxItems_IsSuccess()
    {
        var result = _validator.Validate(ValidRequest() with { Items = ItemsOfCount(CreateOrderRequestValidator.MaxItems) });

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_QuantityAboveLimit_IsValidationFailure()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            Items = [new CreateOrderItemRequest { ProductId = Guid.NewGuid(), Quantity = 1001 }]
        });

        Assert.True(result.IsFailure);
        Assert.Contains("items[0].quantity", result.Error!.Errors!.Keys);
    }

    [Fact]
    public void Validate_QuantityAtLimit_IsSuccess()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            Items = [new CreateOrderItemRequest { ProductId = Guid.NewGuid(), Quantity = 1000 }]
        });

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_NullItem_IsValidationFailure()
    {
        var result = _validator.Validate(ValidRequest() with { Items = [null!] });

        Assert.True(result.IsFailure);
        Assert.Contains("items[0]", result.Error!.Errors!.Keys);
    }

    [Fact]
    public void Validate_TooManyInvalidItems_ReportsOnlyTheCountError()
    {
        var items = Enumerable.Range(0, CreateOrderRequestValidator.MaxItems + 1)
            .Select(_ => new CreateOrderItemRequest { ProductId = Guid.Empty, Quantity = 0 })
            .ToList();

        var result = _validator.Validate(ValidRequest() with { Items = items });

        Assert.Equal(["items"], result.Error!.Errors!.Keys);
    }

    [Fact]
    public void MaxItems_WorstCaseResponse_FitsIdempotencyResponseBodyColumn()
    {
        var response = new OrderResponse
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            ShippingAddress = new string('日', CreateOrderRequestValidator.ShippingAddressMaxLength),
            Status = "Confirmed",
            CreatedAt = DateTimeOffset.MaxValue,
            UpdatedAt = DateTimeOffset.MaxValue,
            Items = Enumerable.Range(0, CreateOrderRequestValidator.MaxItems)
                .Select(_ => new OrderItemResponse
                {
                    ProductId = Guid.NewGuid(),
                    Quantity = int.MaxValue,
                    UnitPrice = 9999999999.99m
                })
                .ToList()
        };

        // Same casing and default encoder (non-ASCII escaped to \uXXXX) as the internal OrderJson options.
        var body = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.True(body.Length <= 8000, $"Serialized body was {body.Length} characters.");
    }

    private static List<CreateOrderItemRequest> ItemsOfCount(int count) =>
        Enumerable.Range(0, count)
            .Select(_ => new CreateOrderItemRequest { ProductId = Guid.NewGuid(), Quantity = 1 })
            .ToList();

    private static CreateOrderRequest ValidRequest() => new()
    {
        CustomerId = Guid.NewGuid(),
        ShippingAddress = "Jl. Sudirman 1",
        Items = [new CreateOrderItemRequest { ProductId = Guid.NewGuid(), Quantity = 2 }]
    };
}
