using OrderManagement.API.Common;
using OrderManagement.API.Contracts.Requests;
using OrderManagement.API.Contracts.Responses;

namespace OrderManagement.API.Abstractions;

public interface IOrderService
{
    Task<Result<OrderResponse>> CreateAsync(string idempotencyKey, CreateOrderRequest request, CancellationToken ct);
}
