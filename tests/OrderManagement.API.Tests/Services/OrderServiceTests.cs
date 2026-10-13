using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderManagement.API.Abstractions;
using OrderManagement.API.Contracts.Requests;
using OrderManagement.API.Contracts.Responses;
using OrderManagement.API.Data;
using OrderManagement.API.Domain;
using OrderManagement.API.Services;
using OrderManagement.API.Validators;

namespace OrderManagement.API.Tests.Services;

public sealed class OrderServiceTests
{
    [Fact]
    public async Task CreateAsync_InvalidRequest_DoesNotBeginIdempotency()
    {
        await using var db = CreateDb();
        var idempotency = new FakeIdempotencyService();
        var stock = new FakeStockService();
        var sut = CreateSut(db, idempotency, stock);

        var result = await sut.CreateAsync("key-1", ValidRequest() with { Items = [] }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Code);
        Assert.Equal(0, idempotency.BeginCalls);
        Assert.Empty(stock.Deducts);
    }

    [Fact]
    public async Task CreateAsync_HashMismatch_IsUnprocessableAndSkipsStock()
    {
        await using var db = CreateDb();
        var idempotency = new FakeIdempotencyService
        {
            BeginResult = IdempotencyBeginResult.Conflict(IdempotencyConflictReason.HashMismatch)
        };
        var stock = new FakeStockService();
        var sut = CreateSut(db, idempotency, stock);

        var result = await sut.CreateAsync("key-1", ValidRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("business_rule", result.Error!.Code);
        Assert.Equal(1, idempotency.BeginCalls);
        Assert.Equal(0, idempotency.CompleteCalls);
        Assert.Empty(stock.Deducts);
    }

    [Fact]
    public async Task CreateAsync_InFlight_IsRetryableConflict()
    {
        await using var db = CreateDb();
        var idempotency = new FakeIdempotencyService
        {
            BeginResult = IdempotencyBeginResult.Conflict(IdempotencyConflictReason.InFlight)
        };
        var stock = new FakeStockService();
        var sut = CreateSut(db, idempotency, stock);

        var result = await sut.CreateAsync("key-1", ValidRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error!.Code);
        Assert.Contains("in progress", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(stock.Deducts);
    }

    [Fact]
    public async Task CreateAsync_Replay_ReturnsCachedOrderWithoutDeduct()
    {
        await using var db = CreateDb();
        var cached = new OrderResponse
        {
            Id = Guid.Parse("cccccccc-0001-4000-8000-000000000001"),
            CustomerId = Guid.Parse("bbbbbbbb-0001-4000-8000-000000000001"),
            ShippingAddress = "Jl. Sudirman 1",
            Status = nameof(OrderStatus.Pending),
            CreatedAt = DateTimeOffset.Parse("2026-10-13T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-10-13T00:00:00Z"),
            Items = [new OrderItemResponse { ProductId = ProductSeed.ScenarioAProductId, Quantity = 2, UnitPrice = 10.00m }]
        };
        var idempotency = new FakeIdempotencyService
        {
            BeginResult = IdempotencyBeginResult.Replay(201, JsonSerializer.Serialize(cached))
        };
        var stock = new FakeStockService();
        var sut = CreateSut(db, idempotency, stock);

        var result = await sut.CreateAsync("key-1", ValidRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(cached.Id, result.Value.Id);
        Assert.Equal(0, idempotency.CompleteCalls);
        Assert.Empty(stock.Deducts);
        Assert.Empty(db.Orders);
    }

    [Fact]
    public async Task CreateAsync_InsufficientStock_IsConflictAndDoesNotComplete()
    {
        await using var db = CreateDb();
        var idempotency = new FakeIdempotencyService();
        var stock = new FakeStockService { DeductSucceeds = false };
        var sut = CreateSut(db, idempotency, stock);

        var result = await sut.CreateAsync("key-1", ValidRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error!.Code);
        Assert.Contains("stock", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, idempotency.CompleteCalls);
        Assert.Empty(db.Orders);
    }

    [Fact]
    public async Task CreateAsync_UnknownProduct_IsNotFound()
    {
        await using var db = CreateDb();
        var idempotency = new FakeIdempotencyService();
        var stock = new FakeStockService();
        var sut = CreateSut(db, idempotency, stock);
        var request = ValidRequest() with
        {
            Items = [new CreateOrderItemRequest { ProductId = Guid.NewGuid(), Quantity = 1 }]
        };

        var result = await sut.CreateAsync("key-1", request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error!.Code);
        Assert.Empty(stock.Deducts);
        Assert.Equal(0, idempotency.CompleteCalls);
    }

    [Fact]
    public async Task CreateAsync_Success_PersistsPendingOrderCapturesPriceAndCompletes()
    {
        await using var db = CreateDb();
        var idempotency = new FakeIdempotencyService();
        var stock = new FakeStockService();
        var sut = CreateSut(db, idempotency, stock);

        var result = await sut.CreateAsync("key-1", ValidRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(OrderStatus.Pending), result.Value.Status);
        Assert.Equal(10.00m, Assert.Single(result.Value.Items).UnitPrice);
        Assert.Equal(ProductSeed.ScenarioAProductId, result.Value.Items[0].ProductId);
        Assert.Equal(2, result.Value.Items[0].Quantity);

        var stored = Assert.Single(db.Orders.Include(o => o.Items));
        Assert.Equal(OrderStatus.Pending, stored.Status);
        Assert.Equal(10.00m, Assert.Single(stored.Items).UnitPrice);
        Assert.Equal([(ProductSeed.ScenarioAProductId, 2)], stock.Deducts);
        Assert.Equal(1, idempotency.CompleteCalls);
        Assert.Equal(201, idempotency.LastCompletedStatus);
        Assert.Contains(result.Value.Id.ToString(), idempotency.LastCompletedBody, StringComparison.Ordinal);
    }

    private static OrderService CreateSut(AppDbContext db, IIdempotencyService idempotency, IStockService stock) =>
        new(db, idempotency, stock, new CreateOrderRequestValidator());

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        db.Products.Add(new Product
        {
            Id = ProductSeed.ScenarioAProductId,
            Name = "Scenario A Widget",
            StockQuantity = 15,
            Price = 10.00m
        });
        db.SaveChanges();
        return db;
    }

    private static CreateOrderRequest ValidRequest() => new()
    {
        CustomerId = Guid.Parse("bbbbbbbb-0001-4000-8000-000000000001"),
        ShippingAddress = "Jl. Sudirman 1",
        Items = [new CreateOrderItemRequest { ProductId = ProductSeed.ScenarioAProductId, Quantity = 2 }]
    };

    private sealed class FakeIdempotencyService : IIdempotencyService
    {
        public IdempotencyBeginResult BeginResult { get; init; } = IdempotencyBeginResult.Created();
        public int BeginCalls { get; private set; }
        public int CompleteCalls { get; private set; }
        public int LastCompletedStatus { get; private set; }
        public string LastCompletedBody { get; private set; } = string.Empty;

        public Task<IdempotencyBeginResult> BeginAsync(string key, string requestHash, CancellationToken ct)
        {
            BeginCalls++;
            return Task.FromResult(BeginResult);
        }

        public Task CompleteAsync(string key, int responseStatus, string responseBody, CancellationToken ct)
        {
            CompleteCalls++;
            LastCompletedStatus = responseStatus;
            LastCompletedBody = responseBody;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStockService : IStockService
    {
        public bool DeductSucceeds { get; init; } = true;
        public List<(Guid ProductId, int Quantity)> Deducts { get; } = [];

        public Task<bool> TryDeductAsync(Guid productId, int quantity, CancellationToken ct)
        {
            Deducts.Add((productId, quantity));
            return Task.FromResult(DeductSucceeds);
        }

        public Task RestoreAsync(Guid productId, int quantity, CancellationToken ct) => Task.CompletedTask;
    }
}
