using System.Data;
using Microsoft.EntityFrameworkCore.Storage;
using SchoolAdmissionSystem.Api.Models;

namespace SchoolAdmissionSystem.Api.Data;

/// <summary>
/// Persistence operations for admission classes and atomic seat holds with pessimistic row-level locking.
/// </summary>
public interface IAdmissionRepository
{
    /// <summary>
    /// Begins a database transaction with the specified isolation level.
    /// </summary>
    Task<IDbContextTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks the target AdmissionClass row using SELECT FOR UPDATE to serialize concurrent hold attempts.
    /// Returns null if the class does not exist.
    /// </summary>
    Task<AdmissionClass?> GetClassWithLockAsync(
        Guid classId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an AdmissionClass without locking.
    /// </summary>
    Task<AdmissionClass?> GetClassByIdAsync(
        Guid classId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transition overdue HELD records for a specific class to EXPIRED status, immediately releasing capacity.
    /// </summary>
    Task<int> ExpireOverdueHoldsForClassAsync(
        Guid classId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transition all overdue HELD records across all classes to EXPIRED status (for background sweeper).
    /// </summary>
    Task<int> ExpireAllOverdueHoldsAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates current occupied seats (HELD + CONFIRMED) directly from PostgreSQL.
    /// Invariant: Occupied <= TotalSeats.
    /// </summary>
    Task<int> GetOccupiedSeatCountAsync(
        Guid classId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a student already possesses an active (HELD) or confirmed admission in the given class.
    /// </summary>
    Task<bool> HasActiveAdmissionAsync(
        Guid classId,
        string studentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves an AdmissionHold by identifier.
    /// </summary>
    Task<AdmissionHold?> GetHoldByIdAsync(
        Guid holdId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up the parent ClassId for a hold without attaching the entity to change tracking.
    /// </summary>
    Task<Guid?> GetHoldClassIdAsync(
        Guid holdId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all holds associated with a student across classes.
    /// </summary>
    Task<List<AdmissionHold>> GetHoldsByStudentIdAsync(
        string studentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns breakdown of total, held, confirmed, and available seats for a class.
    /// </summary>
    Task<(int TotalSeats, int HeldSeats, int ConfirmedSeats, int AvailableSeats)> GetClassSeatBreakdownAsync(
        Guid classId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new AdmissionHold record to the context.
    /// </summary>
    Task AddHoldAsync(
        AdmissionHold hold,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists pending changes.
    /// </summary>
    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
