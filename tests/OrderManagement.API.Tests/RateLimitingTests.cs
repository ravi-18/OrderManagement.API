using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace OrderManagement.API.Tests;

public sealed class RateLimitingTests : IClassFixture<OrderManagementApiFactory>
{
    private readonly HttpClient _client;

    public RateLimitingTests(OrderManagementApiFactory factory) =>
        _client = factory
            .WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:PermitLimit", "2"))
            .CreateClient();

    [Fact]
    public async Task OverTheLimit_Returns429UniformProblemWithRetryAfter()
    {
        await _client.GetAsync("/api/orders?page=abc");
        await _client.GetAsync("/api/orders?page=abc");

        var response = await _client.GetAsync("/api/orders?page=abc");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("rate_limited", json.GetProperty("code").GetString());
        Assert.NotNull(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task Health_IsNotRateLimited()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _client.GetAsync("/health")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}
