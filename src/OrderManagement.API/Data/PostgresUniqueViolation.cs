using Npgsql;

namespace OrderManagement.API.Data;

/// <summary>
/// Detects PostgreSQL unique_violation (<c>23505</c>) from EF/Npgsql exception chains.
/// </summary>
public static class PostgresUniqueViolation
{
    public const string SqlState = PostgresErrorCodes.UniqueViolation;

    public static bool Matches(string? sqlState) =>
        string.Equals(sqlState, SqlState, StringComparison.Ordinal);

    public static bool IsUniqueViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres && Matches(postgres.SqlState))
            {
                return true;
            }
        }

        return false;
    }
}
