using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventory.Application;
using Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure;

public sealed class ReplacementRepository(InventoryDbContext db) : IReplacementRepository
{
    public async Task<ReplacementResult> Status(Guid operationId, CancellationToken ct)
    {
        if (operationId == Guid.Empty) throw InventoryException.Invalid("operationId is required.");
        var receipt = await db.ReplacementOperations.AsNoTracking().SingleOrDefaultAsync(o => o.OperationId == operationId, ct);
        return receipt == null ? new(false, "", default, default, default, default, default, default, "")
            : JsonSerializer.Deserialize<ReplacementResult>(receipt.ResultJson)!;
    }
    public async Task<ReplacementResult> Replace(ReplaceAssetCommand c, CancellationToken ct)
    {
        if (new[] { c.OperationId, c.IncidentId, c.OriginalLoanId, c.OriginalLoanItemId, c.OriginalAssetUnitId,
            c.EquipmentTypeId, c.ReplacementLoanId, c.ReplacementLoanItemId, c.ActorId }.Contains(Guid.Empty)
            || c.OriginalLoanItemId == c.ReplacementLoanItemId || !c.AlreadyReturned && c.ReplacementLoanId != c.OriginalLoanId)
            throw InventoryException.Invalid("Invalid replacement identity or loan linkage.");
        var hash = Hash(c);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        async Task<ReplacementResult> Reject(string code)
        {
            // Definitive rejections are receipts too. A lost OUT_OF_STOCK reply must never turn
            // into an unapproved allocation when stock becomes available during a later recovery.
            var rejected = new ReplacementResult(true, hash, c.OriginalLoanId, c.OriginalLoanItemId, c.OriginalAssetUnitId,
                c.ReplacementLoanId, c.ReplacementLoanItemId, Guid.Empty, "", code);
            db.ReplacementOperations.Add(new() { OperationId = c.OperationId, RequestHash = hash, ResultJson = JsonSerializer.Serialize(rejected), CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return rejected;
        }
        var scope = "replacement:" + c.OperationId;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({scope}, 0))", ct);
        var previous = await Status(c.OperationId, ct);
        if (previous.Found)
        {
            if (previous.RequestHash != hash) throw InventoryException.OperationIdReused(c.OperationId);
            return previous;
        }
        if (c.DueAt <= DateTimeOffset.UtcNow) return await Reject("DUE_DATE_PASSED");
        var original = await db.Reservations.FromSqlInterpolated($"SELECT * FROM \"Reservations\" WHERE \"LoanId\"={c.OriginalLoanId} AND \"LoanItemId\"={c.OriginalLoanItemId} AND \"AssetUnitId\"={c.OriginalAssetUnitId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (original == null) return await Reject("ORIGINAL_ALLOCATION_NOT_FOUND");
        // xmin is a PostgreSQL system column and is not included in SELECT * inside EF's derived table.
        var oldAsset = await db.AssetUnits.FromSqlInterpolated($"SELECT *, xmin FROM \"AssetUnits\" WHERE \"Id\"={c.OriginalAssetUnitId} FOR UPDATE").SingleAsync(ct);
        if (oldAsset.EquipmentTypeId != c.EquipmentTypeId || (c.AlreadyReturned
            ? original.Status != ReservationStatus.Returned || original.ReturnCondition != ReturnCondition.Damaged
            : original.Status != ReservationStatus.HandedOver || oldAsset.Status != AssetStatus.OnLoan))
            return await Reject("ORIGINAL_ALLOCATION_INELIGIBLE");
        if (await db.Reservations.AnyAsync(r => r.LoanItemId == c.ReplacementLoanItemId, ct))
            return await Reject("REPLACEMENT_ITEM_ALREADY_ALLOCATED");
        if (c.DueAt <= DateTimeOffset.UtcNow) return await Reject("DUE_DATE_PASSED");
        var selected = await db.Database.SqlQuery<Guid>($"""
            UPDATE "AssetUnits" SET "Status"='RESERVED', "UpdatedAt"=now()
            WHERE "Id"=(SELECT "Id" FROM "AssetUnits"
                WHERE "EquipmentTypeId"={c.EquipmentTypeId} AND "Status"='AVAILABLE' AND "Id"<>{c.OriginalAssetUnitId}
                ORDER BY "SerialNumber" LIMIT 1 FOR UPDATE SKIP LOCKED)
            RETURNING "Id" AS "Value"
            """).ToListAsync(ct);
        if (selected.Count == 0) return await Reject("OUT_OF_STOCK");
        var newAsset = await db.AssetUnits.SingleAsync(a => a.Id == selected[0], ct);
        if (!c.AlreadyReturned)
        {
            original.Return(ReturnCondition.Damaged, c.ActorId);
            db.MaintenanceRecords.Add(new(oldAsset.Id, $"Verified defective return; incident {c.IncidentId}; loan {c.OriginalLoanId}.", c.ActorId));
        }
        db.StockMovements.Add(new(newAsset.Id, AssetStatus.Available, AssetStatus.Reserved, c.ReplacementLoanId, c.ActorId));
        var replacement = new Reservation(c.OperationId, c.ReplacementLoanId, c.ReplacementLoanItemId, newAsset.Id);
        db.Reservations.Add(replacement);
        replacement.HandOver(c.ActorId);
        var result = new ReplacementResult(true, hash, c.OriginalLoanId, c.OriginalLoanItemId, oldAsset.Id,
            c.ReplacementLoanId, c.ReplacementLoanItemId, newAsset.Id, newAsset.SerialNumber);
        db.ReplacementOperations.Add(new() { OperationId = c.OperationId, RequestHash = hash, ResultJson = JsonSerializer.Serialize(result), CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveMappedAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }
    public static string Hash(ReplaceAssetCommand c) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{c.IncidentId}|{c.OriginalLoanId}|{c.OriginalLoanItemId}|{c.OriginalAssetUnitId}|{c.EquipmentTypeId}|{c.ReplacementLoanId}|{c.ReplacementLoanItemId}|{c.ActorId}|{c.AlreadyReturned}|{c.DueAt.ToUniversalTime():O}")));
}
