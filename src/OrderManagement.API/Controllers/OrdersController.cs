using Microsoft.AspNetCore.Mvc;
using OrderManagement.API.Abstractions;
using OrderManagement.API.Common;
using OrderManagement.API.Contracts.Requests;
using OrderManagement.API.Contracts.Responses;
using OrderManagement.API.Domain;

namespace OrderManagement.API.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(IOrderService orders) : ControllerBase
{
    public const int IdempotencyKeyMaxLength = 256;

    [HttpPost]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IResult> Create(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] CreateOrderRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Error.Validation("Idempotency-Key header is required.").ToProblem();
        }

        if (idempotencyKey.Length > IdempotencyKeyMaxLength)
        {
            return Error.Validation("Idempotency-Key must be 256 characters or fewer.").ToProblem();
        }

        var result = await orders.CreateAsync(idempotencyKey, request, ct);
        if (result.IsFailure)
        {
            return result.ToProblem();
        }

        var order = result.Value;
        return TypedResults.Created($"/api/orders/{order.Id}", order);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await orders.GetByIdAsync(id, ct);
        return result.IsFailure ? result.ToProblem() : TypedResults.Ok(result.Value);
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<OrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> List(
        [FromQuery] OrderStatus? status,
        [FromQuery] Guid? customerId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await orders.ListAsync(
            new ListOrdersQuery
            {
                Status = status,
                CustomerId = customerId,
                From = from,
                To = to,
                Page = page,
                PageSize = pageSize
            },
            ct);

        return result.IsFailure ? result.ToProblem() : TypedResults.Ok(result.Value);
    }
}
