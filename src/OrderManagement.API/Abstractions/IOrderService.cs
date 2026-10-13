using OrderManagement.API.Common;
using OrderManagement.API.Contracts.Requests;
using OrderManagement.API.Contracts.Responses;

namespace OrderManagement.API.Abstractions;

public interface IOrderService
{
    Task<Result<OrderResponse>> CreateAsync(string idempotencyKey, CreateOrderRequest request, CancellationToken ct);

    Task<Result<OrderResponse>> GetByIdAsync(Guid id, CancellationToken ct);

    Task<Result<PagedResponse<OrderResponse>>> ListAsync(ListOrdersQuery query, CancellationToken ct);

    Task<Result<OrderResponse>> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request, CancellationToken ct);
}
