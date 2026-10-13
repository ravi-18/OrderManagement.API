namespace OrderManagement.API.Domain;

public class IdempotencyRecord
{
    public required string Key { get; set; }
    public required string RequestHash { get; set; }
    public int ResponseStatus { get; set; }
    public required string ResponseBody { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
