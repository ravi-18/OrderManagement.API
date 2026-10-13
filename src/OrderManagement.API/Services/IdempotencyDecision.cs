using OrderManagement.API.Abstractions;

namespace OrderManagement.API.Services;

/// <summary>
/// Pure begin-path decision for an existing idempotency row.
/// In-flight (matching hash, no stored response yet) is a retryable conflict;
/// hash mismatch is a non-retryable conflict (HTTP 422 at the Create Order boundary).
/// Concurrent unique-key races are covered against PostgreSQL in Task 17.
/// </summary>
public static class IdempotencyDecision
{
    public static bool IsComplete(int storedResponseStatus) => storedResponseStatus != 0;

    public static IdempotencyBeginResult ForExistingRecord(
        string incomingRequestHash,
        string storedRequestHash,
        int storedResponseStatus,
        string storedResponseBody)
    {
        if (!string.Equals(incomingRequestHash, storedRequestHash, StringComparison.Ordinal))
        {
            return IdempotencyBeginResult.Conflict(IdempotencyConflictReason.HashMismatch);
        }

        if (IsComplete(storedResponseStatus))
        {
            return IdempotencyBeginResult.Replay(storedResponseStatus, storedResponseBody);
        }

        return IdempotencyBeginResult.Conflict(IdempotencyConflictReason.InFlight);
    }
}
