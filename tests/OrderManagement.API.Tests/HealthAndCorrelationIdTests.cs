using System.Net;

namespace OrderManagement.API.Tests;

public sealed class HealthAndCorrelationIdTests : IClassFixture<OrderManagementApiFactory>
{
    private const string CorrelationIdHeader = "X-Correlation-Id";

    private readonly HttpClient _client;

    public HealthAndCorrelationIdTests(OrderManagementApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealth_ReturnsSuccess()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetHealth_IncludesGeneratedCorrelationId()
    {
        var response = await _client.GetAsync("/health");

        Assert.True(response.Headers.TryGetValues(CorrelationIdHeader, out var values));
        var correlationId = Assert.Single(values);
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
    }

    [Fact]
    public async Task GetHealth_PreservesClientSuppliedCorrelationId()
    {
        const string suppliedId = "client-correlation-123";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation(CorrelationIdHeader, suppliedId);

        var response = await _client.SendAsync(request);

        Assert.True(response.Headers.TryGetValues(CorrelationIdHeader, out var values));
        Assert.Equal(suppliedId, Assert.Single(values));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    [InlineData("quote\"")]
    [InlineData("caf\u00e9")]
    public async Task GetHealth_ReplacesCorrelationIdWithUnsafeCharacters(string suppliedId)
    {
        var returned = await SendWithCorrelationIdAsync(suppliedId);

        Assert.NotEqual(suppliedId, returned);
        Assert.True(Guid.TryParse(returned, out _));
    }

    [Fact]
    public async Task GetHealth_ReplacesOverlongCorrelationId()
    {
        var suppliedId = new string('a', Middleware.CorrelationIdMiddleware.MaxLength + 1);

        var returned = await SendWithCorrelationIdAsync(suppliedId);

        Assert.True(Guid.TryParse(returned, out _));
    }

    [Fact]
    public async Task GetHealth_AcceptsCorrelationIdAtMaxLength()
    {
        var suppliedId = new string('a', Middleware.CorrelationIdMiddleware.MaxLength);

        Assert.Equal(suppliedId, await SendWithCorrelationIdAsync(suppliedId));
    }

    [Fact]
    public async Task GetHealth_SendsNosniffHeader()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    [Fact]
    public async Task GetApiRoute_IsNotCacheable()
    {
        var response = await _client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    private async Task<string> SendWithCorrelationIdAsync(string suppliedId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation(CorrelationIdHeader, suppliedId);

        var response = await _client.SendAsync(request);

        return Assert.Single(response.Headers.GetValues(CorrelationIdHeader));
    }
}
