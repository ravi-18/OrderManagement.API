using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderManagement.API.Domain;

namespace OrderManagement.API.Data.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Price)
            .HasPrecision(18, 2);

        builder.Property(p => p.StockQuantity)
            .IsRequired();

        builder.HasData(
            new Product
            {
                Id = ProductSeed.ScenarioAProductId,
                Name = "Scenario A Widget",
                StockQuantity = 15,
                Price = 10.00m
            },
            new Product
            {
                Id = ProductSeed.StandardProductId,
                Name = "Standard Gadget",
                StockQuantity = 50,
                Price = 25.50m
            },
            new Product
            {
                Id = ProductSeed.LowStockProductId,
                Name = "Low Stock Accessory",
                StockQuantity = 5,
                Price = 7.99m
            });
    }
}
