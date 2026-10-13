namespace OrderManagement.API.Data;

/// <summary>
/// Stable product identifiers for migrations and concurrency tests (Scenario A stock=15).
/// </summary>
public static class ProductSeed
{
    public static readonly Guid ScenarioAProductId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");
    public static readonly Guid StandardProductId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000002");
    public static readonly Guid LowStockProductId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000003");
}
