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
}
