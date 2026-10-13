namespace OrderManagement.API.Domain;

public class Product
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public int StockQuantity { get; set; }
    public decimal Price { get; set; }
}
