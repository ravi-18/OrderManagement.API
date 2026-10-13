namespace OrderManagement.API.Abstractions;

public interface IStockService
{
    Task<bool> TryDeductAsync(Guid productId, int quantity, CancellationToken ct);
    Task RestoreAsync(Guid productId, int quantity, CancellationToken ct);
}
