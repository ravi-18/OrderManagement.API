using OrderManagement.API.Abstractions;
using OrderManagement.API.Services;

namespace OrderManagement.API.Tests.Services;

public sealed class IdempotencyDecisionTests
{
    [Fact]
    public void ForExistingRecord_HashMismatch_IsConflictRegardlessOfCompletion()
    {
        var result = IdempotencyDecision.ForExistingRecord(
            incomingRequestHash: "hash-a",
            storedRequestHash: "hash-b",
            storedResponseStatus: 201,
            storedResponseBody: """{"id":1}""");

        Assert.Equal(IdempotencyBeginOutcome.Conflict, result.Outcome);
        Assert.Equal(IdempotencyConflictReason.HashMismatch, result.ConflictReason);
        Assert.Null(result.ResponseStatus);
        Assert.Null(result.ResponseBody);
    }

    [Fact]
    public void ForExistingRecord_HashMismatch_IsCaseSensitive()
    {
        var result = IdempotencyDecision.ForExistingRecord(
            incomingRequestHash: "abc",
            storedRequestHash: "ABC",
            storedResponseStatus: 0,
            storedResponseBody: string.Empty);

        Assert.Equal(IdempotencyBeginOutcome.Conflict, result.Outcome);
        Assert.Equal(IdempotencyConflictReason.HashMismatch, result.ConflictReason);
    }

    [Fact]
    public void ForExistingRecord_MatchingHashAndCompleted_IsReplay()
    {
        var result = IdempotencyDecision.ForExistingRecord(
            incomingRequestHash: "same",
            storedRequestHash: "same",
            storedResponseStatus: 201,
            storedResponseBody: """{"id":42}""");

        Assert.Equal(IdempotencyBeginOutcome.Replay, result.Outcome);
        Assert.Equal(201, result.ResponseStatus);
        Assert.Equal("""{"id":42}""", result.ResponseBody);
        Assert.Null(result.ConflictReason);
    }

    [Fact]
    public void ForExistingRecord_MatchingHashAndInFlight_IsRetryableConflict()
    {
        var result = IdempotencyDecision.ForExistingRecord(
            incomingRequestHash: "same",
            storedRequestHash: "same",
            storedResponseStatus: 0,
            storedResponseBody: string.Empty);

        Assert.Equal(IdempotencyBeginOutcome.Conflict, result.Outcome);
        Assert.Equal(IdempotencyConflictReason.InFlight, result.ConflictReason);
        Assert.Null(result.ResponseStatus);
        Assert.Null(result.ResponseBody);
    }
}
