using OrderManagement.API.Common;
using OrderManagement.API.Contracts.Requests;

namespace OrderManagement.API.Validators;

public sealed class CreateOrderRequestValidator
{
    public const int ShippingAddressMaxLength = 500;

    public Result Validate(CreateOrderRequest request)
    {
        if (request.CustomerId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("CustomerId is required."));
        }

        if (string.IsNullOrWhiteSpace(request.ShippingAddress))
        {
            return Result.Failure(Error.Validation("ShippingAddress is required."));
        }

        if (request.ShippingAddress.Trim().Length > ShippingAddressMaxLength)
        {
            return Result.Failure(Error.Validation("ShippingAddress must be 500 characters or fewer."));
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Result.Failure(Error.Validation("Order must contain at least one item."));
        }

        if (request.Items.Any(item => item.ProductId == Guid.Empty))
        {
            return Result.Failure(Error.Validation("ProductId is required."));
        }

        if (request.Items.Any(item => item.Quantity <= 0))
        {
            return Result.Failure(Error.Validation("Item quantity must be greater than zero."));
        }

        return Result.Success();
    }
}
