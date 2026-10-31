using Microsoft.EntityFrameworkCore;
using OrderManagement.API.Abstractions;
using OrderManagement.API.Data;
using OrderManagement.API.Common;
using OrderManagement.API.Middleware;
using OrderManagement.API.OpenApi;
using OrderManagement.API.Services;
using OrderManagement.API.Validators;

var builder = WebApplication.CreateBuilder(args);

// Orders are small (at most CreateOrderRequestValidator.MaxItems lines); Kestrel's 30 MB default is far too generous.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<IIdempotencyService, IdempotencyService>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddSingleton<CreateOrderRequestValidator>();
builder.Services.AddControllers().AddUniformValidationProblems();
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Order Management API",
        Version = "v1",
        Description = "Order management prototype with idempotency and concurrency-safe stock handling."
    });
    options.OperationFilter<IdempotencyKeyHeaderFilter>();
});
builder.Services.AddHealthChecks();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddApiRateLimiting();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Order Management API v1");
        options.RoutePrefix = "swagger";
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();

app.MapHealthChecks("/health");
app.MapControllers().RequireRateLimiting(ApiRateLimiting.PolicyName);

app.Run();

public partial class Program;
