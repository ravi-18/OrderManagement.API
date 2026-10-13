using Microsoft.EntityFrameworkCore;
using OrderManagement.API.Abstractions;
using OrderManagement.API.Common;
using OrderManagement.API.Contracts.Requests;
using OrderManagement.API.Contracts.Responses;
using OrderManagement.API.Data;
using OrderManagement.API.Domain;
using OrderManagement.API.Validators;

namespace OrderManagement.API.Services;

public sealed class OrderService(
    AppDbContext db,
    IIdempotencyService idempotency,
    IStockService stock,
    CreateOrderRequestValidator validator) : IOrderService
{
    public async Task<Result<OrderResponse>> CreateAsync(
        string idempotencyKey,
        CreateOrderRequest request,
        CancellationToken ct)
    {
        var validation = validator.Validate(request);
        if (validation.IsFailure)
        {
            return Result.Failure<OrderResponse>(validation.Error!);
        }

        var requestHash = RequestHash.Compute(request);
        var begin = await idempotency.BeginAsync(idempotencyKey, requestHash, ct);

        switch (begin.Outcome)
        {
            case IdempotencyBeginOutcome.Replay:
                return DeserializeReplay(begin.ResponseBody);

            case IdempotencyBeginOutcome.Conflict when begin.ConflictReason == IdempotencyConflictReason.HashMismatch:
                return Result.Failure<OrderResponse>(
                    Error.BusinessRule("Idempotency-Key was reused with a different request body."));

            case IdempotencyBeginOutcome.Conflict:
                return Result.Failure<OrderResponse>(
                    Error.Conflict("A request with this Idempotency-Key is already in progress."));
        }

        try
        {
            var created = await CreateNewOrderAsync(idempotencyKey, request, ct);
            if (created.IsFailure)
            {
                await AbandonPendingIdempotencyAsync(idempotencyKey, ct);
            }

            return created;
        }
        catch
        {
            await AbandonPendingIdempotencyAsync(idempotencyKey, ct);
            throw;
        }
    }

    private async Task<Result<OrderResponse>> CreateNewOrderAsync(
        string idempotencyKey,
        CreateOrderRequest request,
        CancellationToken ct)
    {
        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var missing = productIds.FirstOrDefault(id => !products.ContainsKey(id));
        if (missing != Guid.Empty)
        {
            return Result.Failure<OrderResponse>(Error.NotFound($"Product '{missing}' was not found."));
        }

        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        try
        {
            foreach (var item in request.Items)
            {
                var deducted = await stock.TryDeductAsync(item.ProductId, item.Quantity, ct);
                if (!deducted)
                {
                    if (tx is not null)
                    {
                        await tx.RollbackAsync(ct);
                    }

                    return Result.Failure<OrderResponse>(
                        Error.Conflict("Insufficient stock for one or more products."));
                }
            }

            var now = DateTimeOffset.UtcNow;
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerId = request.CustomerId,
                ShippingAddress = request.ShippingAddress.Trim(),
                Status = OrderStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now,
                Items = request.Items.Select(item => new OrderItem
                {
                    Id = Guid.NewGuid(),
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = products[item.ProductId].Price
                }).ToList()
            };

            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);

            var response = ToResponse(order);
            var body = System.Text.Json.JsonSerializer.Serialize(response, OrderJson.SerializerOptions);
            await idempotency.CompleteAsync(idempotencyKey, StatusCodes.Status201Created, body, ct);

            if (tx is not null)
            {
                await tx.CommitAsync(ct);
            }

            return Result.Success(response);
        }
        catch
        {
            if (tx is not null)
            {
                await tx.RollbackAsync(ct);
            }

            throw;
        }
    }

    private async Task AbandonPendingIdempotencyAsync(string key, CancellationToken ct)
    {
        var record = await db.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Key == key && r.ResponseStatus == 0, ct);

        if (record is null)
        {
            return;
        }

        db.IdempotencyRecords.Remove(record);
        await db.SaveChangesAsync(ct);
    }

    private static Result<OrderResponse> DeserializeReplay(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return Result.Failure<OrderResponse>(
                Error.Conflict("A request with this Idempotency-Key is already in progress."));
        }

        var cached = System.Text.Json.JsonSerializer.Deserialize<OrderResponse>(responseBody, OrderJson.SerializerOptions);
        return cached is null
            ? Result.Failure<OrderResponse>(Error.Conflict("Stored idempotent response could not be replayed."))
            : Result.Success(cached);
    }

    private static OrderResponse ToResponse(Order order) => new()
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
