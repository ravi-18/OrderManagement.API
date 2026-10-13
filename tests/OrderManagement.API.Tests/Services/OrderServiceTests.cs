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
    public async Task CreateAsync_ReplayNonSuccessStatus_ReturnsMappedFailureWithoutDeduct()
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
            BeginResult = IdempotencyBeginResult.Replay(409, JsonSerializer.Serialize(cached))
        };
        var stock = new FakeStockService();
        var sut = CreateSut(db, idempotency, stock);

        var result = await sut.CreateAsync("key-1", ValidRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error!.Code);
        Assert.Equal(0, idempotency.CompleteCalls);
        Assert.Equal(0, idempotency.AbandonCalls);
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
        Assert.Equal(1, idempotency.AbandonCalls);
        Assert.Equal("key-1", idempotency.LastAbandonedKey);
        Assert.Equal(CancellationToken.None, idempotency.LastAbandonToken);
        Assert.Empty(db.Orders);
    }

    [Fact]
    public async Task CreateAsync_InsufficientStockAfterCancel_AbandonsWithNoneToken()
    {
        await using var db = CreateDb();
        using var cts = new CancellationTokenSource();
        var idempotency = new FakeIdempotencyService();
        var stock = new FakeStockService { DeductSucceeds = false, CancelAfterDeduct = cts };
        var sut = CreateSut(db, idempotency, stock);

        var result = await sut.CreateAsync("key-1", ValidRequest(), cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(1, idempotency.AbandonCalls);
        Assert.Equal(CancellationToken.None, idempotency.LastAbandonToken);
        Assert.False(idempotency.LastAbandonToken.CanBeCanceled);
    }

    [Fact]
    public async Task CreateAsync_WhenOrderSaveFails_ClearsChangeTrackerAndAbandons()
    {
        await using var db = CreateThrowingOnAddedOrderDb();
        var idempotency = new FakeIdempotencyService();
        var stock = new FakeStockService();
        var sut = CreateSut(db, idempotency, stock);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.CreateAsync("key-1", ValidRequest(), CancellationToken.None));

        Assert.DoesNotContain(
            db.ChangeTracker.Entries<Order>(),
            e => e.State is EntityState.Added or EntityState.Modified);
        Assert.Empty(db.Orders);
        Assert.Equal(1, idempotency.AbandonCalls);
        Assert.Equal(CancellationToken.None, idempotency.LastAbandonToken);
        Assert.Equal(0, idempotency.CompleteCalls);
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
        Assert.Equal(1, idempotency.AbandonCalls);
        Assert.Equal(CancellationToken.None, idempotency.LastAbandonToken);
    }

    [Fact]
    public async Task GetByIdAsync_Missing_IsNotFound()
    {
        await using var db = CreateDb();
        var sut = CreateSut(db);
        var id = Guid.Parse("cccccccc-0001-4000-8000-000000000099");

        var result = await sut.GetByIdAsync(id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error!.Code);
        Assert.Contains(id.ToString(), result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetByIdAsync_Existing_ReturnsDtoWithItemsAndDoesNotTrack()
    {
        await using var db = CreateDb();
        var id = Guid.Parse("cccccccc-0001-4000-8000-000000000001");
        var customerId = Guid.Parse("bbbbbbbb-0001-4000-8000-000000000001");
        SeedOrder(db, id, customerId, OrderStatus.Pending, DateTimeOffset.Parse("2026-03-01T00:00:00Z"), quantity: 2);
        db.ChangeTracker.Clear();
        var sut = CreateSut(db);

        var result = await sut.GetByIdAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(id, result.Value.Id);
        Assert.Equal(customerId, result.Value.CustomerId);
        Assert.Equal(nameof(OrderStatus.Pending), result.Value.Status);
        Assert.Equal("Jl. Test", result.Value.ShippingAddress);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(ProductSeed.ScenarioAProductId, item.ProductId);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(10.00m, item.UnitPrice);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ListAsync_FiltersStatusCustomerAndCreatedAtRange()
    {
        await using var db = CreateDb();
        var customerA = Guid.Parse("bbbbbbbb-0001-4000-8000-000000000001");
        var customerB = Guid.Parse("bbbbbbbb-0001-4000-8000-000000000002");
        var matchId = Guid.Parse("cccccccc-0001-4000-8000-000000000010");
        SeedOrder(db, matchId, customerA, OrderStatus.Pending, DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        SeedOrder(db, Guid.Parse("cccccccc-0001-4000-8000-000000000011"), customerA, OrderStatus.Confirmed, DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        SeedOrder(db, Guid.Parse("cccccccc-0001-4000-8000-000000000012"), customerB, OrderStatus.Pending, DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        SeedOrder(db, Guid.Parse("cccccccc-0001-4000-8000-000000000013"), customerA, OrderStatus.Pending, DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        SeedOrder(db, Guid.Parse("cccccccc-0001-4000-8000-000000000014"), customerA, OrderStatus.Pending, DateTimeOffset.Parse("2026-12-01T00:00:00Z"));
        db.ChangeTracker.Clear();
        var sut = CreateSut(db);

        var result = await sut.ListAsync(new ListOrdersQuery
        {
            Status = OrderStatus.Pending,
            CustomerId = customerA,
            From = DateTimeOffset.Parse("2026-03-01T00:00:00Z"),
            To = DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
            Page = 1,
            PageSize = 20
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(matchId, Assert.Single(result.Value.Items).Id);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal(1, result.Value.Page);
        Assert.Equal(20, result.Value.PageSize);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ListAsync_PaginatesNewestFirst()
    {
        await using var db = CreateDb();
        var customerId = Guid.Parse("bbbbbbbb-0001-4000-8000-000000000001");
        SeedOrder(db, Guid.Parse("cccccccc-0001-4000-8000-000000000021"), customerId, OrderStatus.Pending, DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        SeedOrder(db, Guid.Parse("cccccccc-0001-4000-8000-000000000022"), customerId, OrderStatus.Pending, DateTimeOffset.Parse("2026-02-01T00:00:00Z"));
        SeedOrder(db, Guid.Parse("cccccccc-0001-4000-8000-000000000023"), customerId, OrderStatus.Pending, DateTimeOffset.Parse("2026-03-01T00:00:00Z"));
        SeedOrder(db, Guid.Parse("cccccccc-0001-4000-8000-000000000024"), customerId, OrderStatus.Pending, DateTimeOffset.Parse("2026-04-01T00:00:00Z"));
        SeedOrder(db, Guid.Parse("cccccccc-0001-4000-8000-000000000025"), customerId, OrderStatus.Pending, DateTimeOffset.Parse("2026-05-01T00:00:00Z"));
        var sut = CreateSut(db);

        var result = await sut.ListAsync(new ListOrdersQuery { Page = 2, PageSize = 2 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(2, result.Value.Page);
        Assert.Equal(2, result.Value.PageSize);
        Assert.Equal(
            [Guid.Parse("cccccccc-0001-4000-8000-000000000023"), Guid.Parse("cccccccc-0001-4000-8000-000000000022")],
            result.Value.Items.Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task ListAsync_InvalidPageOrPageSize_IsValidation()
    {
        await using var db = CreateDb();
        var sut = CreateSut(db);

        var page = await sut.ListAsync(new ListOrdersQuery { Page = 0, PageSize = 20 }, CancellationToken.None);
        var size = await sut.ListAsync(new ListOrdersQuery { Page = 1, PageSize = 0 }, CancellationToken.None);
        var max = await sut.ListAsync(new ListOrdersQuery { Page = 1, PageSize = 101 }, CancellationToken.None);

        Assert.True(page.IsFailure);
        Assert.Equal("validation", page.Error!.Code);
        Assert.True(size.IsFailure);
        Assert.Equal("validation", size.Error!.Code);
        Assert.True(max.IsFailure);
        Assert.Equal("validation", max.Error!.Code);
        Assert.Empty(db.Orders);
    }

    [Fact]
    public async Task UpdateStatusAsync_Missing_IsNotFound()
    {
        await using var db = CreateDb();
        var sut = CreateSut(db);
        var id = Guid.Parse("cccccccc-0001-4000-8000-000000000099");

        var result = await sut.UpdateStatusAsync(
            id,
            new UpdateOrderStatusRequest { Status = OrderStatus.Confirmed },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error!.Code);
        Assert.Contains(id.ToString(), result.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Pending, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Pending, OrderStatus.Pending)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Pending)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Pending)]
    public async Task UpdateStatusAsync_IllegalTransition_IsBusinessRule(OrderStatus from, OrderStatus to)
    {
        await using var db = CreateDb();
        var id = Guid.Parse("cccccccc-0001-4000-8000-000000000001");
        SeedOrder(db, id, Guid.Parse("bbbbbbbb-0001-4000-8000-000000000001"), from, DateTimeOffset.Parse("2026-03-01T00:00:00Z"));
        db.ChangeTracker.Clear();
        var sut = CreateSut(db);

        var result = await sut.UpdateStatusAsync(
            id,
            new UpdateOrderStatusRequest { Status = to },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("business_rule", result.Error!.Code);
        Assert.Contains(from.ToString(), result.Error.Message, StringComparison.Ordinal);
        Assert.Contains(to.ToString(), result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(from, Assert.Single(db.Orders).Status);
    }

    [Fact]
    public async Task UpdateStatusAsync_ValidTransition_UpdatesStatusAndUpdatedAt()
    {
        await using var db = CreateDb();
        var id = Guid.Parse("cccccccc-0001-4000-8000-000000000001");
        var createdAt = DateTimeOffset.Parse("2026-03-01T00:00:00Z");
        SeedOrder(db, id, Guid.Parse("bbbbbbbb-0001-4000-8000-000000000001"), OrderStatus.Pending, createdAt, quantity: 2);
        db.ChangeTracker.Clear();
        var sut = CreateSut(db);

        var result = await sut.UpdateStatusAsync(
            id,
            new UpdateOrderStatusRequest { Status = OrderStatus.Confirmed },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(OrderStatus.Confirmed), result.Value.Status);
        Assert.True(result.Value.UpdatedAt > createdAt);
        Assert.Equal(2, Assert.Single(result.Value.Items).Quantity);

        var stored = Assert.Single(db.Orders.Include(o => o.Items));
        Assert.Equal(OrderStatus.Confirmed, stored.Status);
        Assert.Equal(result.Value.UpdatedAt, stored.UpdatedAt);
        Assert.Equal(createdAt, stored.CreatedAt);
        Assert.NotEmpty(db.ChangeTracker.Entries<Order>());
    }

    [Fact]
    public async Task UpdateStatusAsync_ConcurrencyConflict_IsConflict()
    {
        await using var db = CreateThrowingConcurrencyDb();
        var id = Guid.Parse("cccccccc-0001-4000-8000-000000000001");
        SeedOrder(db, id, Guid.Parse("bbbbbbbb-0001-4000-8000-000000000001"), OrderStatus.Pending, DateTimeOffset.Parse("2026-03-01T00:00:00Z"));
        db.ChangeTracker.Clear();
        var sut = CreateSut(db);

        var result = await sut.UpdateStatusAsync(
            id,
            new UpdateOrderStatusRequest { Status = OrderStatus.Confirmed },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error!.Code);
        Assert.Contains("modified", result.Error.Message, StringComparison.OrdinalIgnoreCase);
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

    private static OrderService CreateSut(AppDbContext db) =>
        CreateSut(db, new FakeIdempotencyService(), new FakeStockService());

    private static OrderService CreateSut(AppDbContext db, IIdempotencyService idempotency, IStockService stock) =>
        new(db, idempotency, stock, new CreateOrderRequestValidator());

    private static void SeedOrder(
        AppDbContext db,
        Guid id,
        Guid customerId,
        OrderStatus status,
        DateTimeOffset createdAt,
        int quantity = 1)
    {
        db.Orders.Add(new Order
        {
            Id = id,
            CustomerId = customerId,
            ShippingAddress = "Jl. Test",
            Status = status,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            Items =
            [
                new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = id,
                    ProductId = ProductSeed.ScenarioAProductId,
                    Quantity = quantity,
                    UnitPrice = 10.00m
                }
            ]
        });
        db.SaveChanges();
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        SeedProduct(db);
        return db;
    }

    private static ThrowWhenSavingAddedOrdersDbContext CreateThrowingOnAddedOrderDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ThrowWhenSavingAddedOrdersDbContext(options);
        SeedProduct(db);
        return db;
    }

    private static ThrowConcurrencyOnModifiedOrderDbContext CreateThrowingConcurrencyDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ThrowConcurrencyOnModifiedOrderDbContext(options);
        SeedProduct(db);
        return db;
    }

    private static void SeedProduct(AppDbContext db)
    {
        db.Products.Add(new Product
        {
            Id = ProductSeed.ScenarioAProductId,
            Name = "Scenario A Widget",
            StockQuantity = 15,
            Price = 10.00m
        });
        db.SaveChanges();
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
        public int AbandonCalls { get; private set; }
        public int LastCompletedStatus { get; private set; }
        public string LastCompletedBody { get; private set; } = string.Empty;
        public string LastAbandonedKey { get; private set; } = string.Empty;
        public CancellationToken LastAbandonToken { get; private set; }

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

        public Task AbandonAsync(string key, CancellationToken ct)
        {
            AbandonCalls++;
            LastAbandonedKey = key;
            LastAbandonToken = ct;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowWhenSavingAddedOrdersDbContext(DbContextOptions<AppDbContext> options)
        : AppDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ChangeTracker.Entries<Order>().Any(e => e.State == EntityState.Added))
            {
                throw new InvalidOperationException("simulated order persist failure");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class ThrowConcurrencyOnModifiedOrderDbContext(DbContextOptions<AppDbContext> options)
        : AppDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ChangeTracker.Entries<Order>().Any(e => e.State == EntityState.Modified))
            {
                throw new DbUpdateConcurrencyException("simulated status concurrency conflict");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class FakeStockService : IStockService
    {
        public bool DeductSucceeds { get; init; } = true;
        public CancellationTokenSource? CancelAfterDeduct { get; init; }
        public List<(Guid ProductId, int Quantity)> Deducts { get; } = [];

        public Task<bool> TryDeductAsync(Guid productId, int quantity, CancellationToken ct)
        {
            Deducts.Add((productId, quantity));
            CancelAfterDeduct?.Cancel();
            return Task.FromResult(DeductSucceeds);
        }

        public Task RestoreAsync(Guid productId, int quantity, CancellationToken ct) => Task.CompletedTask;
    }
}
