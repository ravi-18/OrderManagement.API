using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OrderManagement.API.Data;
using OrderManagement.API.Domain;
using OrderManagement.API.Services;

namespace OrderManagement.API.Tests.Services;

public sealed class IdempotencyServiceTests
{
    [Fact]
    public async Task AbandonAsync_RemovesPendingRecordOnly()
    {
        await using var db = CreateDb();
        db.IdempotencyRecords.AddRange(
            new IdempotencyRecord
            {
                Key = "pending-key",
                RequestHash = "hash-pending",
                ResponseStatus = 0,
                ResponseBody = string.Empty,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new IdempotencyRecord
            {
                Key = "completed-key",
                RequestHash = "hash-done",
                ResponseStatus = 201,
                ResponseBody = """{"ok":true}""",
                CreatedAt = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();

        var sut = new IdempotencyService(db, NullLogger<IdempotencyService>.Instance);
        await sut.AbandonAsync("pending-key", CancellationToken.None);

        Assert.Null(await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Key == "pending-key"));
        Assert.NotNull(await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Key == "completed-key"));
    }

    [Fact]
    public async Task AbandonAsync_MissingOrCompleted_IsNoOp()
    {
        await using var db = CreateDb();
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Key = "completed-key",
            RequestHash = "hash-done",
            ResponseStatus = 201,
            ResponseBody = """{"ok":true}""",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var sut = new IdempotencyService(db, NullLogger<IdempotencyService>.Instance);
        await sut.AbandonAsync("completed-key", CancellationToken.None);
        await sut.AbandonAsync("missing-key", CancellationToken.None);

        Assert.NotNull(await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Key == "completed-key"));
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}
