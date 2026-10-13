using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderManagement.API.Abstractions;
using OrderManagement.API.Common;
using OrderManagement.API.Contracts.Requests;
using OrderManagement.API.Contracts.Responses;
using OrderManagement.API.Controllers;
using OrderManagement.API.Data;
using OrderManagement.API.Domain;
using OrderManagement.API.Middleware;

namespace OrderManagement.API.Tests.Controllers;

public sealed class OrdersControllerTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly ConfigurableOrderService _orders = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(OrdersController).Assembly);
        builder.Services.AddSingleton<IOrderService>(_orders);
        builder.Services.AddExceptionHandler<DomainExceptionHandler>();
        builder.Services.AddProblemDetails();

        _app = builder.Build();
        _app.UseMiddleware<CorrelationIdMiddleware>();
        _app.UseExceptionHandler();
        _app.MapControllers();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Post_MissingIdempotencyKey_Returns400ProblemDetails()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", ValidBody());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(400, json.GetProperty("status").GetInt32());
        Assert.Equal("validation", json.GetProperty("code").GetString());
        Assert.Contains("Idempotency-Key", json.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Null(_orders.LastKey);
    }

    [Fact]
    public async Task Post_IdempotencyKeyOver256_Returns400ProblemDetails()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", new string('k', 257));
        request.Content = JsonContent.Create(ValidBody());

        var response = await _client.SendAsync(request);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation", json.GetProperty("code").GetString());
        Assert.Contains("256", json.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Null(_orders.LastKey);
    }

    [Fact]
    public async Task Post_IdempotencyKeyAt256_IsAccepted()
    {
        var order = new OrderResponse
        {
            Id = Guid.Parse("cccccccc-0001-4000-8000-000000000001"),
            CustomerId = Guid.Parse("bbbbbbbb-0001-4000-8000-000000000001"),
            ShippingAddress = "Jl. Sudirman 1",
            Status = nameof(OrderStatus.Pending),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Items = [new OrderItemResponse { ProductId = ProductSeed.ScenarioAProductId, Quantity = 2, UnitPrice = 10.00m }]
        };
        _orders.Next = Result.Success(order);
        var key = new string('k', 256);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        request.Content = JsonContent.Create(ValidBody());
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(key, _orders.LastKey);
    }

    [Fact]
    public async Task Post_HashMismatch_Returns422()
    {
        _orders.Next = Result.Failure<OrderResponse>(
            Error.BusinessRule("Idempotency-Key was reused with a different request body."));

        var response = await SendCreateAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("business_rule", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Post_InFlight_Returns409()
    {
        _orders.Next = Result.Failure<OrderResponse>(
            Error.Conflict("A request with this Idempotency-Key is already in progress."));

        var response = await SendCreateAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_InsufficientStock_Returns409()
    {
        _orders.Next = Result.Failure<OrderResponse>(Error.Conflict("Insufficient stock for one or more products."));

        var response = await SendCreateAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("conflict", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Get_ExistingOrder_Returns200WithItems()
    {
        var order = SampleOrder();
        _orders.GetNext = Result.Success(order);

        var response = await _client.GetAsync($"/api/orders/{order.Id}");
        var body = await response.Content.ReadFromJsonAsync<OrderResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(order.Id, _orders.LastGetId);
        Assert.Equal(order.Id, body!.Id);
        Assert.Equal(order.Items[0].ProductId, Assert.Single(body.Items).ProductId);
        Assert.Equal(order.Items[0].Quantity, body.Items[0].Quantity);
        Assert.Equal(order.Items[0].UnitPrice, body.Items[0].UnitPrice);
    }

    [Fact]
    public async Task Get_MissingOrder_Returns404ProblemDetails()
    {
        var id = Guid.Parse("cccccccc-0001-4000-8000-000000000099");
        _orders.GetNext = Result.Failure<OrderResponse>(Error.NotFound($"Order '{id}' was not found."));

        var response = await _client.GetAsync($"/api/orders/{id}");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(404, json.GetProperty("status").GetInt32());
        Assert.Equal("not_found", json.GetProperty("code").GetString());
        Assert.Equal(id, _orders.LastGetId);
    }

    [Fact]
    public async Task List_ForwardsFiltersAndReturnsPagedBody()
    {
        var customerId = Guid.Parse("bbbbbbbb-0001-4000-8000-000000000001");
        var from = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-12-31T23:59:59Z");
        var order = SampleOrder();
        _orders.ListNext = Result.Success(new PagedResponse<OrderResponse>
        {
            Items = [order],
            Page = 2,
            PageSize = 10,
            TotalCount = 21
        });

        var response = await _client.GetAsync(
            $"/api/orders?status=Pending&customerId={customerId}&from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}&page=2&pageSize=10");
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<OrderResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(_orders.LastListQuery);
        Assert.Equal(OrderStatus.Pending, _orders.LastListQuery!.Status);
        Assert.Equal(customerId, _orders.LastListQuery.CustomerId);
        Assert.Equal(from, _orders.LastListQuery.From);
        Assert.Equal(to, _orders.LastListQuery.To);
        Assert.Equal(2, _orders.LastListQuery.Page);
        Assert.Equal(10, _orders.LastListQuery.PageSize);
        Assert.Equal(order.Id, Assert.Single(body!.Items).Id);
        Assert.Equal(2, body.Page);
        Assert.Equal(10, body.PageSize);
        Assert.Equal(21, body.TotalCount);
    }

    [Fact]
    public async Task List_OmitsQuery_UsesDefaultPageAndPageSize()
    {
        _orders.ListNext = Result.Success(new PagedResponse<OrderResponse>
        {
            Items = [],
            Page = 1,
            PageSize = 20,
            TotalCount = 0
        });

        var response = await _client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _orders.LastListQuery!.Page);
        Assert.Equal(20, _orders.LastListQuery.PageSize);
        Assert.Null(_orders.LastListQuery.Status);
        Assert.Null(_orders.LastListQuery.CustomerId);
        Assert.Null(_orders.LastListQuery.From);
        Assert.Null(_orders.LastListQuery.To);
    }

    [Fact]
    public async Task List_InvalidPage_Returns400ProblemDetails()
    {
        _orders.ListNext = Result.Failure<PagedResponse<OrderResponse>>(
            Error.Validation("page must be at least 1."));

        var response = await _client.GetAsync("/api/orders?page=0");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation", json.GetProperty("code").GetString());
        Assert.Equal(0, _orders.LastListQuery!.Page);
    }

    [Fact]
    public async Task Post_Success_Returns201WithOrderBody()
    {
        var order = new OrderResponse
        {
            Id = Guid.Parse("cccccccc-0001-4000-8000-000000000001"),
            CustomerId = Guid.Parse("bbbbbbbb-0001-4000-8000-000000000001"),
            ShippingAddress = "Jl. Sudirman 1",
            Status = nameof(OrderStatus.Pending),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Items = [new OrderItemResponse { ProductId = ProductSeed.ScenarioAProductId, Quantity = 2, UnitPrice = 10.00m }]
        };
        _orders.Next = Result.Success(order);

        var response = await SendCreateAsync();
        var body = await response.Content.ReadFromJsonAsync<OrderResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/orders/{order.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(order.Id, body!.Id);
        Assert.Equal("key-1", _orders.LastKey);
    }

    private async Task<HttpResponseMessage> SendCreateAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "key-1");
        request.Content = JsonContent.Create(ValidBody());
        return await _client.SendAsync(request);
    }

    private static CreateOrderRequest ValidBody() => new()
    {
        CustomerId = Guid.Parse("bbbbbbbb-0001-4000-8000-000000000001"),
        ShippingAddress = "Jl. Sudirman 1",
        Items = [new CreateOrderItemRequest { ProductId = ProductSeed.ScenarioAProductId, Quantity = 2 }]
    };

    private static OrderResponse SampleOrder() => new()
    {
        Id = Guid.Parse("cccccccc-0001-4000-8000-000000000001"),
        CustomerId = Guid.Parse("bbbbbbbb-0001-4000-8000-000000000001"),
        ShippingAddress = "Jl. Sudirman 1",
        Status = nameof(OrderStatus.Pending),
        CreatedAt = DateTimeOffset.Parse("2026-10-13T00:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2026-10-13T00:00:00Z"),
        Items = [new OrderItemResponse { ProductId = ProductSeed.ScenarioAProductId, Quantity = 2, UnitPrice = 10.00m }]
    };

    private sealed class ConfigurableOrderService : IOrderService
    {
        public Result<OrderResponse>? Next { get; set; }
        public Result<OrderResponse>? GetNext { get; set; }
        public Result<PagedResponse<OrderResponse>>? ListNext { get; set; }
        public string? LastKey { get; private set; }
        public Guid? LastGetId { get; private set; }
        public ListOrdersQuery? LastListQuery { get; private set; }

        public Task<Result<OrderResponse>> CreateAsync(string idempotencyKey, CreateOrderRequest request, CancellationToken ct)
        {
            LastKey = idempotencyKey;
            return Task.FromResult(Next ?? throw new InvalidOperationException("Next result was not configured."));
        }

        public Task<Result<OrderResponse>> GetByIdAsync(Guid id, CancellationToken ct)
        {
            LastGetId = id;
            return Task.FromResult(GetNext ?? throw new InvalidOperationException("GetNext result was not configured."));
        }

        public Task<Result<PagedResponse<OrderResponse>>> ListAsync(ListOrdersQuery query, CancellationToken ct)
        {
            LastListQuery = query;
            return Task.FromResult(ListNext ?? throw new InvalidOperationException("ListNext result was not configured."));
        }
    }
}
