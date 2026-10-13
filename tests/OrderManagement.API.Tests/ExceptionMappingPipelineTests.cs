using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderManagement.API.Common;
using OrderManagement.API.Middleware;
using DomainValidationException = OrderManagement.API.Common.ValidationException;

namespace OrderManagement.API.Tests;

public sealed class ExceptionMappingPipelineTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddExceptionHandler<DomainExceptionHandler>();
        builder.Services.AddProblemDetails();

        _app = builder.Build();
        _app.UseMiddleware<CorrelationIdMiddleware>();
        _app.UseExceptionHandler();
        _app.MapGet("/throw/not-found", () =>
        {
            throw new NotFoundException("Order not found");
        });
        _app.MapGet("/throw/conflict", () =>
        {
            throw new ConflictException("Insufficient stock");
        });
        _app.MapGet("/throw/validation", () =>
        {
            throw new DomainValidationException("CustomerId is required");
        });
        _app.MapGet("/throw/business", () =>
        {
            throw new BusinessRuleException("Invalid status transition");
        });
        _app.MapGet("/throw/unhandled", () =>
        {
            throw new InvalidOperationException("password=super-secret;Host=db.internal");
        });

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task NotFoundException_Returns404ProblemDetails()
    {
        var (response, json) = await GetProblem("/throw/not-found");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertProblem(json, 404, "Not Found", "Order not found", "not_found");
    }

    [Fact]
    public async Task ConflictException_Returns409ProblemDetails()
    {
        var (response, json) = await GetProblem("/throw/conflict");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        AssertProblem(json, 409, "Conflict", "Insufficient stock", "conflict");
    }

    [Fact]
    public async Task DomainValidationException_Returns400ProblemDetails()
    {
        var (response, json) = await GetProblem("/throw/validation");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertProblem(json, 400, "Bad Request", "CustomerId is required", "validation");
    }

    [Fact]
    public async Task BusinessRuleException_Returns422ProblemDetails()
    {
        var (response, json) = await GetProblem("/throw/business");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        AssertProblem(json, 422, "Unprocessable Entity", "Invalid status transition", "business_rule");
    }

    [Fact]
    public async Task UnhandledException_Returns500WithoutSecretsOrStacks()
    {
        var (response, json) = await GetProblem("/throw/unhandled");
        var body = json.GetRawText();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(500, json.GetProperty("status").GetInt32());
        Assert.Equal("Internal Server Error", json.GetProperty("title").GetString());
        Assert.Equal("An unexpected error occurred.", json.GetProperty("detail").GetString());
        Assert.Equal("internal_error", json.GetProperty("code").GetString());
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("super-secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("db.internal", body, StringComparison.OrdinalIgnoreCase);
        Assert.False(json.TryGetProperty("exception", out _));
        Assert.False(json.TryGetProperty("stackTrace", out _));
    }

    [Fact]
    public async Task MappedError_IncludesCorrelationIdHeader()
    {
        const string suppliedId = "error-correlation-123";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/throw/not-found");
        request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, suppliedId);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values));
        Assert.Equal(suppliedId, Assert.Single(values));
    }

    private async Task<(HttpResponseMessage Response, JsonElement Json)> GetProblem(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (response, json);
    }

    private static void AssertProblem(JsonElement json, int status, string title, string detail, string code)
    {
        Assert.Equal(status, json.GetProperty("status").GetInt32());
        Assert.Equal(title, json.GetProperty("title").GetString());
        Assert.Equal(detail, json.GetProperty("detail").GetString());
        Assert.Equal(code, json.GetProperty("code").GetString());
        Assert.Contains(status.ToString(), json.GetProperty("type").GetString(), StringComparison.Ordinal);
    }
}
