using Microsoft.EntityFrameworkCore;
using OrderManagement.API.Abstractions;
using OrderManagement.API.Common;
using OrderManagement.API.Data;
using OrderManagement.API.Domain;

namespace OrderManagement.API.Services;

public sealed class IdempotencyService(AppDbContext db, ILogger<IdempotencyService> logger) : IIdempotencyService
{
    public async Task<IdempotencyBeginResult> BeginAsync(string key, string requestHash, CancellationToken ct)
    {
        var pending = new IdempotencyRecord
        {
            Key = key,
            RequestHash = requestHash,
            ResponseStatus = 0,
            ResponseBody = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.IdempotencyRecords.Add(pending);

        try
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Idempotency begin created for IdempotencyKey {IdempotencyKey}", key);
            return IdempotencyBeginResult.Created();
        }
        catch (DbUpdateException ex) when (PostgresUniqueViolation.IsUniqueViolation(ex))
        {
            db.Entry(pending).State = EntityState.Detached;

            var existing = await db.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Key == key, ct);

            if (existing is null)
            {
                throw;
            }

            var decision = IdempotencyDecision.ForExistingRecord(
                requestHash,
                existing.RequestHash,
                existing.ResponseStatus,
                existing.ResponseBody);

            logger.LogInformation(
                "Idempotency begin resolved existing key IdempotencyKey {IdempotencyKey} Outcome {Outcome} ConflictReason {ConflictReason}",
                key,
                decision.Outcome,
                decision.ConflictReason);
            return decision;
        }
    }

    public async Task CompleteAsync(string key, int responseStatus, string responseBody, CancellationToken ct)
    {
        var record = await db.IdempotencyRecords.FirstOrDefaultAsync(r => r.Key == key, ct)
            ?? throw new NotFoundException($"Idempotency record '{key}' was not found.");

        record.ResponseStatus = responseStatus;
        record.ResponseBody = responseBody;
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Idempotency completed for IdempotencyKey {IdempotencyKey} ResponseStatus {ResponseStatus}",
            key,
            responseStatus);
    }

    public async Task AbandonAsync(string key, CancellationToken ct)
    {
        var record = await db.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Key == key && r.ResponseStatus == 0, ct);

        if (record is null)
        {
            logger.LogDebug(
                "Idempotency abandon skipped; no pending record for IdempotencyKey {IdempotencyKey}",
                key);
            return;
        }

        db.IdempotencyRecords.Remove(record);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Idempotency abandoned pending key IdempotencyKey {IdempotencyKey}", key);
    }
}
