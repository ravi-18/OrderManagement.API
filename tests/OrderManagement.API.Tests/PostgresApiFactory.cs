using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OrderManagement.API.Data;

namespace OrderManagement.API.Tests;

/// <summary>
/// Resolves a dedicated PostgreSQL test database and skips concurrent tests when it is unreachable.
/// Docker/Testcontainers is not used: Docker CLI is not installed in this environment.
/// </summary>
internal static class PostgresTestConnection
{
    public const string TestDatabaseName = "order_management_tests";

    public static string? ConnectionString { get; }
    public static string? SkipReason { get; }

    static PostgresTestConnection()
    {
        var raw = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("TEST_DATABASE_URL")
            ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=CHANGE_ME";

        try
        {
            var probe = new NpgsqlConnectionStringBuilder(raw)
            {
                Timeout = 5,
                Database = "postgres"
            };

            using var conn = new NpgsqlConnection(probe.ConnectionString);
            conn.Open();

            var test = new NpgsqlConnectionStringBuilder(raw)
            {
                Timeout = 5,
                Database = TestDatabaseName
            };
            ConnectionString = test.ConnectionString;
        }
        catch (Exception ex)
        {
            SkipReason =
                $"PostgreSQL unavailable ({ex.GetType().Name}): {ex.Message} " +
                $"Set ConnectionStrings__DefaultConnection or TEST_DATABASE_URL to a reachable server. " +
                $"Tests use database '{TestDatabaseName}' and never drop other databases.";
        }
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (PostgresTestConnection.SkipReason is { } reason)
        {
            Skip = reason;
        }
    }
}

public sealed class PostgresApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public PostgresApiFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString
            });
        });
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }
}

public sealed class PostgresApiFixture : IAsyncLifetime
{
    public PostgresApiFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (PostgresTestConnection.SkipReason is not null
            || PostgresTestConnection.ConnectionString is null)
        {
            return;
        }

        await EnsureTestDatabaseAsync(PostgresTestConnection.ConnectionString);
        Factory = new PostgresApiFactory(PostgresTestConnection.ConnectionString);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
    }

    private static async Task EnsureTestDatabaseAsync(string testConnectionString)
    {
        var admin = new NpgsqlConnectionStringBuilder(testConnectionString)
        {
            Database = "postgres"
        };

        await using var conn = new NpgsqlConnection(admin.ConnectionString);
        await conn.OpenAsync();

        await using var exists = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @name",
            conn);
        exists.Parameters.AddWithValue("name", PostgresTestConnection.TestDatabaseName);

        if (await exists.ExecuteScalarAsync() is not null)
        {
            return;
        }

        await using var create = new NpgsqlCommand(
            $"""CREATE DATABASE "{PostgresTestConnection.TestDatabaseName}" """,
            conn);
        await create.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresApiFixture>
{
    public const string Name = "Postgres";
}
