using Microsoft.EntityFrameworkCore;
using OrderManagement.API.Abstractions;
using OrderManagement.API.Data;

namespace OrderManagement.API.Services;

public sealed class StockService(AppDbContext db) : IStockService
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

        return rows > 0;
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
    }
}
