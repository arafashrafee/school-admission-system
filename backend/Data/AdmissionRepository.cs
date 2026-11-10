using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SchoolAdmissionSystem.Api.Models;

namespace SchoolAdmissionSystem.Api.Data;

public class AdmissionRepository : IAdmissionRepository
{
    private readonly ApplicationDbContext _context;

    public AdmissionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IDbContextTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        return await _context.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
    }

    public async Task<AdmissionClass?> GetClassWithLockAsync(
        Guid classId,
        CancellationToken cancellationToken = default)
    {
        // Executes SELECT FOR UPDATE to acquire exclusive row-level lock on the AdmissionClass.
        // Serializes concurrent hold and confirmation requests for this class at the database tier.
        return await _context.AdmissionClasses
            .FromSqlInterpolated($"SELECT * FROM \"AdmissionClasses\" WHERE \"Id\" = {classId} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<AdmissionClass?> GetClassByIdAsync(
        Guid classId,
        CancellationToken cancellationToken = default)
    {
        return await _context.AdmissionClasses
            .FirstOrDefaultAsync(c => c.Id == classId, cancellationToken);
    }

    public async Task<int> ExpireOverdueHoldsForClassAsync(
        Guid classId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        // Atomically transitions any overdue HELD records to EXPIRED for this class,
        // instantly releasing capacity before remaining calculations.
        return await _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"AdmissionHolds\" SET \"Status\" = 'EXPIRED' WHERE \"ClassId\" = {classId} AND \"Status\" = 'HELD' AND \"ExpiresAt\" <= {utcNow}",
            cancellationToken);
    }

    public async Task<int> ExpireAllOverdueHoldsAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        // Periodic background sweeper query across all classes.
        return await _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"AdmissionHolds\" SET \"Status\" = 'EXPIRED' WHERE \"Status\" = 'HELD' AND \"ExpiresAt\" <= {utcNow}",
            cancellationToken);
    }

    public async Task<int> GetOccupiedSeatCountAsync(
        Guid classId,
        CancellationToken cancellationToken = default)
    {
        // Authoritative database-level seat occupancy counter: active holds + confirmed admissions.
        return await _context.AdmissionHolds
            .Where(h => h.ClassId == classId && (h.Status == HoldStatus.HELD || h.Status == HoldStatus.CONFIRMED))
            .CountAsync(cancellationToken);
    }

    public async Task<bool> HasActiveAdmissionAsync(
        Guid classId,
        string studentId,
        CancellationToken cancellationToken = default)
    {
        return await _context.AdmissionHolds
            .AnyAsync(
                h => h.ClassId == classId &&
                     h.StudentId == studentId &&
                     (h.Status == HoldStatus.HELD || h.Status == HoldStatus.CONFIRMED),
                cancellationToken);
    }

    public async Task<AdmissionHold?> GetHoldByIdAsync(
        Guid holdId,
        CancellationToken cancellationToken = default)
    {
        return await _context.AdmissionHolds
            .Include(h => h.Class)
            .FirstOrDefaultAsync(h => h.Id == holdId, cancellationToken);
    }

    public async Task<Guid?> GetHoldClassIdAsync(
        Guid holdId,
        CancellationToken cancellationToken = default)
    {
        return await _context.AdmissionHolds
            .AsNoTracking()
            .Where(h => h.Id == holdId)
            .Select(h => (Guid?)h.ClassId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<AdmissionHold>> GetHoldsByStudentIdAsync(
        string studentId,
        CancellationToken cancellationToken = default)
    {
        return await _context.AdmissionHolds
            .Include(h => h.Class)
            .Where(h => h.StudentId == studentId)
            .OrderByDescending(h => h.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<(int TotalSeats, int HeldSeats, int ConfirmedSeats, int AvailableSeats)> GetClassSeatBreakdownAsync(
        Guid classId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var admissionClass = await _context.AdmissionClasses
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == classId, cancellationToken);

        if (admissionClass == null)
        {
            return (0, 0, 0, 0);
        }

        // Active holds are HELD and not yet passed expiresAt (or clock-expired)
        var counts = await _context.AdmissionHolds
            .AsNoTracking()
            .Where(h => h.ClassId == classId)
            .GroupBy(h => h.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count(),
                ValidHeldCount = g.Count(h => h.Status == HoldStatus.HELD && h.ExpiresAt > utcNow)
            })
            .ToListAsync(cancellationToken);

        var held = counts.FirstOrDefault(c => c.Status == HoldStatus.HELD)?.ValidHeldCount ?? 0;
        var confirmed = counts.FirstOrDefault(c => c.Status == HoldStatus.CONFIRMED)?.Count ?? 0;
        var available = Math.Max(0, admissionClass.TotalSeats - (held + confirmed));

        return (admissionClass.TotalSeats, held, confirmed, available);
    }

    public async Task AddHoldAsync(
        AdmissionHold hold,
        CancellationToken cancellationToken = default)
    {
        await _context.AdmissionHolds.AddAsync(hold, cancellationToken);
    }

    public async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
