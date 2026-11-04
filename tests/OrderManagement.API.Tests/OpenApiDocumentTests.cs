using System.Net.Http.Json;
using System.Text.Json;

namespace OrderManagement.API.Tests;

public sealed class OpenApiDocumentTests : IClassFixture<OrderManagementApiFactory>
{
    private readonly HttpClient _client;

    public OpenApiDocumentTests(OrderManagementApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task CreateOrder_DeclaresIdempotencyKeyAsRequiredHeader()
    {
        var doc = await _client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var post = doc.GetProperty("paths").GetProperty("/api/orders").GetProperty("post");

        var header = post.GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "Idempotency-Key");

        Assert.Equal("header", header.GetProperty("in").GetString());
        Assert.True(header.GetProperty("required").GetBoolean());
    }

    [Fact]
    public async Task CreateOrder_DeclaresEveryDocumentedStatus()
    {
        var doc = await _client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var post = doc.GetProperty("paths").GetProperty("/api/orders").GetProperty("post");
        var codes = post.GetProperty("responses").EnumerateObject().Select(p => p.Name).ToHashSet();

        Assert.Superset(new HashSet<string> { "201", "400", "404", "409", "422", "500" }, codes);
    }
}
