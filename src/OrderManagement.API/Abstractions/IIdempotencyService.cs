namespace OrderManagement.API.Abstractions;

public interface IIdempotencyService
{
    Task<IdempotencyBeginResult> BeginAsync(string key, string requestHash, CancellationToken ct);
    Task CompleteAsync(string key, int responseStatus, string responseBody, CancellationToken ct);
}
