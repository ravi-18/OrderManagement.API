using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderManagement.API.Data;

namespace OrderManagement.API.Tests.Data;

public sealed class PostgresUniqueViolationTests
{
    [Fact]
    public void Matches_23505_IsUniqueViolation()
    {
        Assert.Equal("23505", PostgresUniqueViolation.SqlState);
        Assert.True(PostgresUniqueViolation.Matches("23505"));
    }

    [Fact]
    public void Matches_OtherSqlState_IsNotUniqueViolation()
    {
        Assert.False(PostgresUniqueViolation.Matches("23503"));
        Assert.False(PostgresUniqueViolation.Matches(null));
        Assert.False(PostgresUniqueViolation.Matches(string.Empty));
    }

    [Fact]
    public void IsUniqueViolation_GenericException_IsFalse()
    {
        Assert.False(PostgresUniqueViolation.IsUniqueViolation(new InvalidOperationException("no postgres")));
        Assert.False(PostgresUniqueViolation.IsUniqueViolation(new Exception("outer", new Exception("inner"))));
    }

    [Fact]
    public void IsUniqueViolation_WrappedPostgres23505_IsTrue()
    {
        var postgres = new PostgresException("duplicate key value violates unique constraint", "ERROR", "ERROR", "23505");
        var wrapped = new DbUpdateException("could not save", postgres);

        Assert.True(PostgresUniqueViolation.IsUniqueViolation(wrapped));
    }

    [Fact]
    public void IsUniqueViolation_OtherPostgresSqlState_IsFalse()
    {
        var postgres = new PostgresException("foreign key violation", "ERROR", "ERROR", "23503");

        Assert.False(PostgresUniqueViolation.IsUniqueViolation(postgres));
    }
}
