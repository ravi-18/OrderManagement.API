using Microsoft.EntityFrameworkCore;

namespace OrderManagement.API.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
