namespace OrderManagement.API.Abstractions;

public interface IIdempotencyService
{
    Task<IdempotencyBeginResult> BeginAsync(Guid customerId, string key, string requestHash, CancellationToken ct);
    Task CompleteAsync(Guid customerId, string key, int responseStatus, string responseBody, CancellationToken ct);
    Task AbandonAsync(Guid customerId, string key, CancellationToken ct);
}
