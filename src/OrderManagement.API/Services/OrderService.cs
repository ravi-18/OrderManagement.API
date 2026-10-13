using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
                return DeserializeReplay(begin.ResponseStatus, begin.ResponseBody);

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
                await AbandonAfterFailureAsync(idempotencyKey);
            }

            return created;
        }
        catch
        {
            await AbandonAfterFailureAsync(idempotencyKey);
            throw;
        }
    }

    public async Task<Result<OrderResponse>> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

        return order is null
            ? Result.Failure<OrderResponse>(Error.NotFound($"Order '{id}' was not found."))
            : Result.Success(ToResponse(order));
    }

    public async Task<Result<PagedResponse<OrderResponse>>> ListAsync(ListOrdersQuery query, CancellationToken ct)
    {
        if (query.Page < 1)
        {
            return Result.Failure<PagedResponse<OrderResponse>>(Error.Validation("page must be at least 1."));
        }

        if (query.PageSize < 1)
        {
            return Result.Failure<PagedResponse<OrderResponse>>(Error.Validation("pageSize must be at least 1."));
        }

        if (query.PageSize > 100)
        {
            return Result.Failure<PagedResponse<OrderResponse>>(Error.Validation("pageSize must be 100 or fewer."));
        }

        var filtered = db.Orders.AsNoTracking();
        if (query.Status is { } status)
        {
            filtered = filtered.Where(o => o.Status == status);
        }

        if (query.CustomerId is { } customerId)
        {
            filtered = filtered.Where(o => o.CustomerId == customerId);
        }

        if (query.From is { } from)
        {
            filtered = filtered.Where(o => o.CreatedAt >= from);
        }

        if (query.To is { } to)
        {
            filtered = filtered.Where(o => o.CreatedAt <= to);
        }

        var totalCount = await filtered.CountAsync(ct);
        var orders = await filtered
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return Result.Success(new PagedResponse<OrderResponse>
        {
            Items = orders.Select(ToResponse).ToList(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<OrderResponse>> UpdateStatusAsync(
        Guid id,
        UpdateOrderStatusRequest request,
        CancellationToken ct)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

        if (order is null)
        {
            return Result.Failure<OrderResponse>(Error.NotFound($"Order '{id}' was not found."));
        }

        if (request.Status == OrderStatus.Cancelled
            && OrderStatusTransitions.CanTransition(order.Status, OrderStatus.Cancelled))
        {
            return Result.Failure<OrderResponse>(
                Error.BusinessRule("Cancel orders via POST /api/orders/{id}/cancel."));
        }

        if (!OrderStatusTransitions.CanTransition(order.Status, request.Status))
        {
            return Result.Failure<OrderResponse>(
                Error.BusinessRule($"Cannot transition order from {order.Status} to {request.Status}."));
        }

        order.Status = request.Status;
        order.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<OrderResponse>(
                Error.Conflict("The order was modified by another request."));
        }

        return Result.Success(ToResponse(order));
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
                    await RollbackAndDetachAsync(tx);
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
            await RollbackAndDetachAsync(tx);
            throw;
        }
    }

    private async Task AbandonAfterFailureAsync(string key)
    {
        db.ChangeTracker.Clear();
        await idempotency.AbandonAsync(key, CancellationToken.None);
    }

    private async Task RollbackAndDetachAsync(IDbContextTransaction? tx)
    {
        try
        {
            if (tx is not null)
            {
                await tx.RollbackAsync(CancellationToken.None);
            }
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private static Result<OrderResponse> DeserializeReplay(int? responseStatus, string? responseBody)
    {
        if (responseStatus is not StatusCodes.Status201Created)
        {
            return Result.Failure<OrderResponse>(MapStoredReplayStatus(responseStatus));
        }

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

    private static Error MapStoredReplayStatus(int? status) => status switch
    {
        StatusCodes.Status400BadRequest =>
            Error.Validation("Stored idempotent response indicates a validation failure."),
        StatusCodes.Status404NotFound =>
            Error.NotFound("Stored idempotent response indicates the resource was not found."),
        StatusCodes.Status409Conflict =>
            Error.Conflict("Stored idempotent response indicates a conflict."),
        StatusCodes.Status422UnprocessableEntity =>
            Error.BusinessRule("Stored idempotent response indicates a business rule violation."),
        _ => Error.Conflict("Stored idempotent response could not be replayed with the original status.")
    };

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
