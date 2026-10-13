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

    private sealed class ConfigurableOrderService : IOrderService
    {
        public Result<OrderResponse>? Next { get; set; }
        public string? LastKey { get; private set; }

        public Task<Result<OrderResponse>> CreateAsync(string idempotencyKey, CreateOrderRequest request, CancellationToken ct)
        {
            LastKey = idempotencyKey;
            return Task.FromResult(Next ?? throw new InvalidOperationException("Next result was not configured."));
        }
    }
}
