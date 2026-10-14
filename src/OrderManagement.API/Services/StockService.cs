using Microsoft.EntityFrameworkCore;
using OrderManagement.API.Abstractions;
using OrderManagement.API.Data;

namespace OrderManagement.API.Services;

public sealed class StockService(AppDbContext db, ILogger<StockService> logger) : IStockService
{
    public async Task<bool> TryDeductAsync(Guid productId, int quantity, CancellationToken ct)
    {
        var rows = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "Products"
            SET "StockQuantity" = "StockQuantity" - {quantity}
            WHERE "Id" = {productId} AND "StockQuantity" >= {quantity}
            """,
            ct);

        var deducted = rows > 0;
        if (deducted)
        {
            logger.LogInformation(
                "Stock deducted for ProductId {ProductId} Quantity {Quantity}",
                productId,
                quantity);
        }
        else
        {
            logger.LogWarning(
                "Stock deduct failed for ProductId {ProductId} Quantity {Quantity}",
                productId,
                quantity);
        }

        return deducted;
    }

    public async Task RestoreAsync(Guid productId, int quantity, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "Products"
            SET "StockQuantity" = "StockQuantity" + {quantity}
            WHERE "Id" = {productId}
            """,
            ct);

        logger.LogInformation(
            "Stock restored for ProductId {ProductId} Quantity {Quantity}",
            productId,
            quantity);
    }
}
