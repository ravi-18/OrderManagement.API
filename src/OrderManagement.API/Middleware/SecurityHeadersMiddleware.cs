namespace OrderManagement.API.Middleware;

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // OnStarting so the headers survive ExceptionHandlerMiddleware.Clear().
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                // Order responses carry customer and address data; keep them out of shared caches.
                headers.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        });

        return _next(context);
    }
}
