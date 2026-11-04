using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace OrderManagement.API.Common;

/// <summary>
/// Fixed-window limit per client IP on the API routes. Behind a reverse proxy every client shares the
/// proxy's address unless forwarded headers are configured.
/// </summary>
public static class ApiRateLimiting
{
    public const string PolicyName = "api";

    private const int DefaultPermitLimit = 120;
    private const int DefaultWindowSeconds = 60;

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.OnRejected = WriteProblemAsync;
            options.AddPolicy(PolicyName, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => WindowOptions(context.RequestServices.GetRequiredService<IConfiguration>())));
        });

    private static FixedWindowRateLimiterOptions WindowOptions(IConfiguration configuration) => new()
    {
        PermitLimit = configuration.GetValue("RateLimiting:PermitLimit", DefaultPermitLimit),
        Window = TimeSpan.FromSeconds(configuration.GetValue("RateLimiting:WindowSeconds", DefaultWindowSeconds))
    };

    private static async ValueTask WriteProblemAsync(OnRejectedContext context, CancellationToken ct)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }

        await response.WriteAsJsonAsync(ProblemDetailsMapper.RateLimited(), options: null, "application/problem+json", ct);
    }
}
