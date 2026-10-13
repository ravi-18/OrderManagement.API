namespace OrderManagement.API.Middleware;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    public const string LogScopeKey = "CorrelationId";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context.Request);
        context.TraceIdentifier = correlationId;
        context.Items[HeaderName] = correlationId;

        // Set on start so the header survives ExceptionHandlerMiddleware.Clear().
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (_logger.BeginScope(new Dictionary<string, object> { [LogScopeKey] = correlationId }))
        {
            await _next(context);
        }
    }

    private static string ResolveCorrelationId(HttpRequest request)
    {
        if (request.Headers.TryGetValue(HeaderName, out var values))
        {
            var incoming = values.ToString();
            if (!string.IsNullOrWhiteSpace(incoming))
            {
                return incoming.Trim();
            }
        }

        return Guid.NewGuid().ToString();
    }
}
