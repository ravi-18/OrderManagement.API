using OrderManagement.API.Domain;

namespace OrderManagement.API.Contracts.Requests;

public sealed record ListOrdersQuery
{
    public OrderStatus? Status { get; init; }
    public Guid? CustomerId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
