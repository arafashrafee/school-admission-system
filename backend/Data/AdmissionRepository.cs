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
