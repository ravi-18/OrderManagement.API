namespace OrderManagement.API.Abstractions;

public enum IdempotencyBeginOutcome
{
    Created,
    Replay,
    Conflict
}

public enum IdempotencyConflictReason
{
    HashMismatch,
    InFlight
}

public sealed record IdempotencyBeginResult(
    IdempotencyBeginOutcome Outcome,
    int? ResponseStatus = null,
    string? ResponseBody = null,
    IdempotencyConflictReason? ConflictReason = null)
{
    public static IdempotencyBeginResult Created() =>
        new(IdempotencyBeginOutcome.Created);

    public static IdempotencyBeginResult Replay(int responseStatus, string responseBody) =>
        new(IdempotencyBeginOutcome.Replay, responseStatus, responseBody);

    public static IdempotencyBeginResult Conflict(IdempotencyConflictReason reason) =>
        new(IdempotencyBeginOutcome.Conflict, ConflictReason: reason);
}
