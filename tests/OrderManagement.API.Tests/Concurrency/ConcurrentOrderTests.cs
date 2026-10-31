using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OrderManagement.API.Abstractions;
using OrderManagement.API.Contracts.Requests;
using OrderManagement.API.Contracts.Responses;
using OrderManagement.API.Data;
using OrderManagement.API.Domain;
using OrderManagement.API.Services;

namespace OrderManagement.API.Tests.Concurrency;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ConcurrentOrderTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly PostgresApiFactory _factory;

    public ConcurrentOrderTests(PostgresApiFixture fixture)
    {
        _factory = fixture.Factory;
    }

    [PostgresFact]
    public async Task ScenarioA_ConcurrentStock_AtMostOneSuccessAndNeverNegative()
    {
        await ResetStockAsync(ProductSeed.ScenarioAProductId, 15);

        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var bodyA = CreateBody(customerA, ProductSeed.ScenarioAProductId, quantity: 10);
        var bodyB = CreateBody(customerB, ProductSeed.ScenarioAProductId, quantity: 10);

        using var clientA = _factory.CreateClient();
        using var clientB = _factory.CreateClient();

        var responses = await Task.WhenAll(
            PostCreateAsync(clientA, $"scenario-a-{Guid.NewGuid():N}", bodyA),
            PostCreateAsync(clientB, $"scenario-a-{Guid.NewGuid():N}", bodyB));

        var statuses = responses.Select(r => r.StatusCode).ToArray();
        var created = responses.Where(r => r.StatusCode == HttpStatusCode.Created).ToList();
        var conflicts = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();

        Assert.All(statuses, status =>
            Assert.True(
                status is HttpStatusCode.Created or HttpStatusCode.Conflict,
                $"Unexpected status {status}."));
        Assert.True(created.Count <= 1, "Both creates succeeded; combined quantity 20 exceeds stock 15.");
        Assert.Equal(2 - created.Count, conflicts.Count);

        foreach (var conflict in conflicts)
        {
            var problem = await conflict.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("conflict", problem.GetProperty("code").GetString());
        }

        var remaining = await GetStockAsync(ProductSeed.ScenarioAProductId);
        Assert.True(remaining >= 0, $"Stock went negative ({remaining}).");
        Assert.Equal(15 - (10 * created.Count), remaining);

        var orderCount = await CountOrdersAsync(customerA) + await CountOrdersAsync(customerB);
        Assert.Equal(created.Count, orderCount);
    }

    [PostgresFact]
    public async Task ScenarioB_ConcurrentStatus_OneWinnerAndConflict()
    {
        await ResetStockAsync(ProductSeed.StandardProductId, 50);

        using var client = _factory.CreateClient();
        var customerId = Guid.NewGuid();
        var created = await PostCreateAsync(
            client,
            $"scenario-b-create-{Guid.NewGuid():N}",
            CreateBody(customerId, ProductSeed.StandardProductId, quantity: 1));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = await created.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(order);

        using var confirm = await PatchStatusAsync(client, order.Id, OrderStatus.Confirmed);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        using var clientA = _factory.CreateClient();
        using var clientB = _factory.CreateClient();
        var responses = await Task.WhenAll(
            PatchStatusAsync(clientA, order.Id, OrderStatus.Shipped),
            PatchStatusAsync(clientB, order.Id, OrderStatus.Shipped));

        var winners = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        var losers = responses.Where(r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity).ToList();

        Assert.True(winners.Count == 1, $"Expected exactly one PATCH winner, got {winners.Count} ({Describe(responses)}).");
        Assert.True(losers.Count == 1, $"Expected the other PATCH to lose with 409/422, got {Describe(responses)}.");

        if (losers[0].StatusCode == HttpStatusCode.Conflict)
        {
            var problem = await losers[0].Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("conflict", problem.GetProperty("code").GetString());
        }

        var current = await GetOrderAsync(client, order.Id);
        Assert.Equal(nameof(OrderStatus.Shipped), current.Status);
    }

    [PostgresFact]
    public async Task ScenarioC_IdempotentCreateRace_ExactlyOneOrder()
    {
        await ResetStockAsync(ProductSeed.StandardProductId, 50);

        var customerId = Guid.NewGuid();
        var key = $"scenario-c-{Guid.NewGuid():N}";
        var body = CreateBody(customerId, ProductSeed.StandardProductId, quantity: 2);

        using var clientA = _factory.CreateClient();
        using var clientB = _factory.CreateClient();

        var firstWave = await Task.WhenAll(
            PostCreateAsync(clientA, key, body),
            PostCreateAsync(clientB, key, body));

        Assert.All(firstWave, r =>
            Assert.True(
                r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
                $"Unexpected status {r.StatusCode}."));
        Assert.Contains(firstWave, r => r.StatusCode == HttpStatusCode.Created);

        var settled = await Task.WhenAll(
            SettleCreateAsync(clientA, key, body, firstWave[0]),
            SettleCreateAsync(clientB, key, body, firstWave[1]));

        Assert.All(settled, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        var orderA = await settled[0].Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        var orderB = await settled[1].Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(orderA);
        Assert.NotNull(orderB);
        Assert.Equal(orderA.Id, orderB.Id);
        Assert.Equal(orderA.CustomerId, orderB.CustomerId);
        Assert.Equal(orderA.ShippingAddress, orderB.ShippingAddress);
        Assert.Equal(orderA.Items[0].Quantity, orderB.Items[0].Quantity);

        Assert.Equal(1, await CountOrdersAsync(customerId));
        Assert.Equal(1, await CountIdempotencyRecordsAsync(key));
        Assert.Equal(48, await GetStockAsync(ProductSeed.StandardProductId));
    }

    [PostgresFact]
    public async Task Create_OppositeItemOrders_DoNotDeadlock()
    {
        await ResetStockAsync(ProductSeed.ScenarioAProductId, 1000);
        await ResetStockAsync(ProductSeed.StandardProductId, 1000);

        using var client = _factory.CreateClient();
        var responses = await Task.WhenAll(Enumerable.Range(0, 24).Select(i =>
        {
            Guid[] productIds = i % 2 == 0
                ? [ProductSeed.ScenarioAProductId, ProductSeed.StandardProductId]
                : [ProductSeed.StandardProductId, ProductSeed.ScenarioAProductId];
            return PostCreateAsync(client, $"deadlock-{Guid.NewGuid():N}", CreateBody(Guid.NewGuid(), productIds));
        }));

        Assert.True(responses.All(r => r.StatusCode == HttpStatusCode.Created), Describe(responses));
        Assert.Equal(976, await GetStockAsync(ProductSeed.ScenarioAProductId));
        Assert.Equal(976, await GetStockAsync(ProductSeed.StandardProductId));
    }

    [PostgresFact]
    public async Task Create_LaterItemOutOfStock_RollsBackEarlierDeducts()
    {
        await ResetStockAsync(ProductSeed.StandardProductId, 50);
        await ResetStockAsync(ProductSeed.LowStockProductId, 0);

        var customerId = Guid.NewGuid();
        var key = $"rollback-{Guid.NewGuid():N}";
        using var client = _factory.CreateClient();

        using var response = await PostCreateAsync(
            client,
            key,
            CreateBody(customerId, ProductSeed.StandardProductId, ProductSeed.LowStockProductId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(50, await GetStockAsync(ProductSeed.StandardProductId));
        Assert.Equal(0, await CountOrdersAsync(customerId));
        Assert.Equal(0, await CountIdempotencyRecordsAsync(key));
    }

    [PostgresFact]
    public async Task Create_WhileInFlight_NothingIsCommittedForACrashToStrand()
    {
        await ResetStockAsync(ProductSeed.StandardProductId, 50);

        var key = $"crash-{Guid.NewGuid():N}";
        long? visibleMidFlight = null;
        await using var factory = FactoryWithStockHook(async () =>
            visibleMidFlight = await CountIdempotencyRowsOutsideTransactionAsync(key));
        using var client = factory.CreateClient();

        using var response = await PostCreateAsync(
            client,
            key,
            CreateBody(Guid.NewGuid(), ProductSeed.StandardProductId, quantity: 1));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(0, visibleMidFlight);
    }

    [PostgresFact]
    public async Task Create_ConcurrentSameKey_AllReceiveTheSameOrderWithoutRetrying()
    {
        await ResetStockAsync(ProductSeed.StandardProductId, 50);

        var key = $"same-key-{Guid.NewGuid():N}";
        var body = CreateBody(Guid.NewGuid(), ProductSeed.StandardProductId, quantity: 1);
        using var client = _factory.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PostCreateAsync(client, key, body)));

        Assert.True(responses.All(r => r.StatusCode == HttpStatusCode.Created), Describe(responses));
        var orders = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions)));
        Assert.Single(orders.Select(o => o!.Id).Distinct());
        Assert.Equal(49, await GetStockAsync(ProductSeed.StandardProductId));
    }

    [PostgresFact]
    public async Task Cancel_LosingTheRace_RollsBackRestoredStock()
    {
        await ResetStockAsync(ProductSeed.StandardProductId, 50);

        using var plainClient = _factory.CreateClient();
        using var created = await PostCreateAsync(
            plainClient,
            $"cancel-race-{Guid.NewGuid():N}",
            CreateBody(Guid.NewGuid(), ProductSeed.StandardProductId, quantity: 4));
        var order = await created.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);

        // A rival write lands after stock is restored and before the cancel saves, so xmin rejects the cancel.
        await using var factory = FactoryWithStockHook(() => TouchOrderOutsideTransactionAsync(order!.Id));
        using var client = factory.CreateClient();

        using var response = await client.PostAsync($"/api/orders/{order!.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(46, await GetStockAsync(ProductSeed.StandardProductId));
        Assert.Equal(nameof(OrderStatus.Pending), (await GetOrderAsync(plainClient, order.Id)).Status);
    }

    [PostgresFact]
    public async Task Create_SameKeyFromDifferentCustomers_AreIndependent()
    {
        await ResetStockAsync(ProductSeed.StandardProductId, 50);

        var key = $"shared-key-{Guid.NewGuid():N}";
        using var client = _factory.CreateClient();

        using var first = await PostCreateAsync(client, key, CreateBody(Guid.NewGuid(), ProductSeed.StandardProductId, quantity: 1));
        using var second = await PostCreateAsync(client, key, CreateBody(Guid.NewGuid(), ProductSeed.StandardProductId, quantity: 1));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [PostgresFact]
    public async Task Create_SameCustomerReusingKeyWithDifferentBody_IsRejected()
    {
        await ResetStockAsync(ProductSeed.StandardProductId, 50);

        var key = $"reused-key-{Guid.NewGuid():N}";
        var customerId = Guid.NewGuid();
        using var client = _factory.CreateClient();

        using var first = await PostCreateAsync(client, key, CreateBody(customerId, ProductSeed.StandardProductId, quantity: 1));
        using var second = await PostCreateAsync(client, key, CreateBody(customerId, ProductSeed.StandardProductId, quantity: 2));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
    }

    private async Task ResetStockAsync(Guid productId, int quantity)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = await db.Products.SingleAsync(p => p.Id == productId);
        product.StockQuantity = quantity;
        await db.SaveChangesAsync();
    }

    private async Task<int> GetStockAsync(Guid productId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => p.StockQuantity)
            .SingleAsync();
    }

    private async Task<int> CountOrdersAsync(Guid customerId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Orders.AsNoTracking().CountAsync(o => o.CustomerId == customerId);
    }

    private async Task<int> CountIdempotencyRecordsAsync(string key)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.IdempotencyRecords.AsNoTracking().CountAsync(r => r.Key == key);
    }

    private WebApplicationFactory<Program> FactoryWithStockHook(Func<Task> afterStockWrite) =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddScoped<StockService>();
            services.AddScoped<IStockService>(provider =>
                new HookedStockService(provider.GetRequiredService<StockService>(), afterStockWrite));
        }));

    private static async Task<long> CountIdempotencyRowsOutsideTransactionAsync(string key)
    {
        await using var connection = new NpgsqlConnection(PostgresTestConnection.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""SELECT count(*) FROM "IdempotencyRecords" WHERE "Key" = @key""", connection);
        command.Parameters.AddWithValue("key", key);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task TouchOrderOutsideTransactionAsync(Guid orderId)
    {
        await using var connection = new NpgsqlConnection(PostgresTestConnection.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""UPDATE "Orders" SET "UpdatedAt" = now() WHERE "Id" = @id""", connection);
        command.Parameters.AddWithValue("id", orderId);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class HookedStockService(IStockService inner, Func<Task> afterStockWrite) : IStockService
    {
        public async Task<bool> TryDeductAsync(Guid productId, int quantity, CancellationToken ct)
        {
            var deducted = await inner.TryDeductAsync(productId, quantity, ct);
            await afterStockWrite();
            return deducted;
        }

        public async Task RestoreAsync(Guid productId, int quantity, CancellationToken ct)
        {
            await inner.RestoreAsync(productId, quantity, ct);
            await afterStockWrite();
        }
    }

    private static CreateOrderRequest CreateBody(Guid customerId, Guid productId, int quantity) => new()
    {
        CustomerId = customerId,
        ShippingAddress = "Jl. Concurrent 1",
        Items = [new CreateOrderItemRequest { ProductId = productId, Quantity = quantity }]
    };

    private static CreateOrderRequest CreateBody(Guid customerId, params Guid[] productIds) => new()
    {
        CustomerId = customerId,
        ShippingAddress = "Jl. Concurrent 1",
        Items = productIds.Select(id => new CreateOrderItemRequest { ProductId = id, Quantity = 1 }).ToList()
    };

    private static async Task<HttpResponseMessage> PostCreateAsync(
        HttpClient client,
        string key,
        CreateOrderRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SettleCreateAsync(
        HttpClient client,
        string key,
        CreateOrderRequest body,
        HttpResponseMessage first)
    {
        if (first.StatusCode == HttpStatusCode.Created)
        {
            return first;
        }

        first.Dispose();
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(50);
            var retry = await PostCreateAsync(client, key, body);
            if (retry.StatusCode != HttpStatusCode.Conflict)
            {
                return retry;
            }

            retry.Dispose();
        }

        return await PostCreateAsync(client, key, body);
    }

    private static Task<HttpResponseMessage> PatchStatusAsync(HttpClient client, Guid orderId, OrderStatus status)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/orders/{orderId}/status")
        {
            Content = new StringContent(
                $$"""{"status":"{{status}}"}""",
                Encoding.UTF8,
                "application/json")
        };
        return client.SendAsync(request);
    }

    private static async Task<OrderResponse> GetOrderAsync(HttpClient client, Guid orderId)
    {
        var response = await client.GetAsync($"/api/orders/{orderId}");
        response.EnsureSuccessStatusCode();
        var order = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        return order ?? throw new InvalidOperationException("Order body was empty.");
    }

    private static string Describe(IEnumerable<HttpResponseMessage> responses) =>
        string.Join(", ", responses.Select(r => (int)r.StatusCode));
}
