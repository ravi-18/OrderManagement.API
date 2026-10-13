using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrderManagement.API.Contracts.Requests;

namespace OrderManagement.API.Services;

public static class RequestHash
{
    public static string Compute(CreateOrderRequest request)
    {
        var json = JsonSerializer.Serialize(request, OrderJson.SerializerOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
