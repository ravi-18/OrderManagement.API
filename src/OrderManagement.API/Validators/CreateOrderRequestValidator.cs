using OrderManagement.API.Common;
using OrderManagement.API.Contracts.Requests;

namespace OrderManagement.API.Validators;

public sealed class CreateOrderRequestValidator
{
    public const int ShippingAddressMaxLength = 500;

    // Bounds the per-request stock UPDATEs and keeps the serialized 201 body within the
    // 8000-character IdempotencyRecords.ResponseBody column.
    public const int MaxItems = 40;

    public const int MaxQuantity = 1000;

    public Result Validate(CreateOrderRequest request)
    {
        // Collect every problem so a client fixes the whole request in one round trip.
        var errors = new Dictionary<string, string[]>();

        if (request.CustomerId == Guid.Empty)
        {
            errors["customerId"] = ["CustomerId is required."];
        }

        if (string.IsNullOrWhiteSpace(request.ShippingAddress))
        {
            errors["shippingAddress"] = ["ShippingAddress is required."];
        }
        else if (request.ShippingAddress.Trim().Length > ShippingAddressMaxLength)
        {
            errors["shippingAddress"] = ["ShippingAddress must be 500 characters or fewer."];
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            errors["items"] = ["Order must contain at least one item."];
        }
        else if (request.Items.Count > MaxItems)
        {
            // Checked before the per-item loop so an oversized order can't grow the error map.
            errors["items"] = [$"Order must contain {MaxItems} items or fewer."];
        }
        else
        {
            for (var i = 0; i < request.Items.Count; i++)
            {
                if (request.Items[i] is null)
                {
                    errors[$"items[{i}]"] = ["Item is required."];
                    continue;
                }

                if (request.Items[i].ProductId == Guid.Empty)
                {
                    errors[$"items[{i}].productId"] = ["ProductId is required."];
                }

                if (request.Items[i].Quantity <= 0)
                {
                    errors[$"items[{i}].quantity"] = ["Item quantity must be greater than zero."];
                }
                else if (request.Items[i].Quantity > MaxQuantity)
                {
                    errors[$"items[{i}].quantity"] = [$"Item quantity must be {MaxQuantity} or fewer."];
                }
            }
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(Error.Validation(errors));
    }
}
