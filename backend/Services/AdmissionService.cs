using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SchoolAdmissionSystem.Api.Configuration;
using SchoolAdmissionSystem.Api.Data;
using SchoolAdmissionSystem.Api.DTOs;
using SchoolAdmissionSystem.Api.Models;

namespace SchoolAdmissionSystem.Api.Services;

public partial class AdmissionService : IAdmissionService
{
    private readonly IAdmissionRepository _repository;
    private readonly IOptions<AdmissionOptions> _options;
    private readonly ILogger<AdmissionService> _logger;

    [GeneratedRegex("^[0-9]{17}$")]
    private static partial Regex StudentIdRegex();

    public AdmissionService(
        IAdmissionRepository repository,
        IOptions<AdmissionOptions> options,
        ILogger<AdmissionService> logger)
    {
        _repository = repository;
        _options = options;
        _logger = logger;
    }

    private static bool IsValidStudentId(string? studentId)
    {
        return !string.IsNullOrWhiteSpace(studentId) &&
               studentId.Length == 17 &&
               StudentIdRegex().IsMatch(studentId);
    }

    public async Task<ReserveSeatResult> ReserveSeatAsync(
        Guid classId,
        string studentId,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidStudentId(studentId))
        {
            return new ReserveSeatResult(
                false,
                "INVALID_STUDENT_ID",
                "Student ID must be exactly 17 decimal digits preserving leading zeroes.",
                null);
        }

        await using var transaction = await _repository.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            // Step 1: Acquire exclusive row-level lock on the target class row.
            // Serializes concurrent allocations for the same class at the database tier.
            var admissionClass = await _repository.GetClassWithLockAsync(classId, cancellationToken);
            if (admissionClass == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ReserveSeatResult(false, "CLASS_NOT_FOUND", $"Class '{classId}' does not exist.", null);
            }

            var utcNow = DateTime.UtcNow;

            // Step 2: Eager transactional sweep of overdue holds for this class.
            // Releases expired holds so capacity becomes immediately available in this transaction.
            await _repository.ExpireOverdueHoldsForClassAsync(classId, utcNow, cancellationToken);

            // Step 3: Prevent duplicate active (HELD) or confirmed admission for this student.
            var hasActive = await _repository.HasActiveAdmissionAsync(classId, studentId, cancellationToken);
            if (hasActive)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ReserveSeatResult(
                    false,
                    "DUPLICATE_APPLICATION",
                    $"Student '{studentId}' already has an active hold or confirmed admission in class '{admissionClass.Name}'.",
                    null);
            }

            // Step 4: Authoritative database count of occupied seats.
            var occupied = await _repository.GetOccupiedSeatCountAsync(classId, cancellationToken);
            if (occupied >= admissionClass.TotalSeats)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ReserveSeatResult(
                    false,
                    "CAPACITY_EXHAUSTED",
                    $"No seats available in class '{admissionClass.Name}'. Total: {admissionClass.TotalSeats}, Occupied: {occupied}.",
                    null);
            }

            // Step 5: Insert new HELD record.
            var holdDuration = Math.Max(1, _options.Value.HoldDurationSeconds);
            var hold = new AdmissionHold
            {
                Id = Guid.NewGuid(),
                ClassId = classId,
                StudentId = studentId,
                Status = HoldStatus.HELD,
                CreatedAt = utcNow,
                ExpiresAt = utcNow.AddSeconds(holdDuration),
                ConfirmedAt = null
            };

            await _repository.AddHoldAsync(hold, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Seat successfully reserved. HoldId: {HoldId}, ClassId: {ClassId}, StudentId: {StudentId}, ExpiresAt: {ExpiresAt:O}",
                hold.Id,
                classId,
                studentId,
                hold.ExpiresAt);

            return new ReserveSeatResult(true, null, null, hold);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while reserving seat for student {StudentId} in class {ClassId}", studentId, classId);
            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch
            {
                // Ignore rollback secondary errors
            }

            return new ReserveSeatResult(false, "DATABASE_ERROR", ex.Message, null);
        }
    }

    public async Task<ConfirmHoldResult> ConfirmHoldAsync(
        Guid holdId,
        string studentId,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidStudentId(studentId))
        {
            return new ConfirmHoldResult(
                false,
                "INVALID_STUDENT_ID",
                "Student ID must be exactly 17 decimal digits preserving leading zeroes.",
                null);
        }

        await using var transaction = await _repository.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            var classId = await _repository.GetHoldClassIdAsync(holdId, cancellationToken);
            if (!classId.HasValue)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ConfirmHoldResult(false, "HOLD_NOT_FOUND", "Admission hold not found.", null);
            }

            // Lock parent class row first to adhere to consistent lock ordering across operations.
            await _repository.GetClassWithLockAsync(classId.Value, cancellationToken);

            // Fetch hold under active class lock so state reflects any concurrent commits.
            var hold = await _repository.GetHoldByIdAsync(holdId, cancellationToken);
            if (hold == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ConfirmHoldResult(false, "HOLD_NOT_FOUND", "Admission hold not found.", null);
            }

            // Ownership check
            if (hold.StudentId != studentId)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ConfirmHoldResult(
                    false,
                    "UNAUTHORIZED",
                    "Student ID does not match the owner of this hold.",
                    null);
            }

            // Idempotent confirmation
            if (hold.Status == HoldStatus.CONFIRMED)
            {
                await transaction.CommitAsync(cancellationToken);
                return new ConfirmHoldResult(
                    true,
                    null,
                    "Hold is already confirmed.",
                    hold,
                    WasAlreadyConfirmed: true);
            }

            if (hold.Status == HoldStatus.CANCELLED)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ConfirmHoldResult(
                    false,
                    "HOLD_CANCELLED",
                    "Cannot confirm a cancelled hold.",
                    hold);
            }

            var utcNow = DateTime.UtcNow;

            // Check if hold has expired (either in status or by clock)
            if (hold.Status == HoldStatus.EXPIRED || hold.ExpiresAt <= utcNow)
            {
                if (hold.Status == HoldStatus.HELD)
                {
                    hold.Status = HoldStatus.EXPIRED;
                    await _repository.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                else
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                return new ConfirmHoldResult(
                    false,
                    "HOLD_EXPIRED",
                    "Hold has expired and can no longer be confirmed.",
                    hold);
            }

            // Transition from HELD to CONFIRMED
            hold.Status = HoldStatus.CONFIRMED;
            hold.ConfirmedAt = utcNow;

            await _repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Hold {HoldId} confirmed successfully for student {StudentId}",
                holdId,
                studentId);

            return new ConfirmHoldResult(
                true,
                null,
                "Admission confirmed successfully.",
                hold,
                WasAlreadyConfirmed: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while confirming hold {HoldId} for student {StudentId}", holdId, studentId);
            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch
            {
                // Ignore rollback secondary errors
            }

            return new ConfirmHoldResult(false, "DATABASE_ERROR", ex.Message, null);
        }
    }

    public async Task<CancelHoldResult> CancelHoldAsync(
        Guid holdId,
        string studentId,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidStudentId(studentId))
        {
            return new CancelHoldResult(
                false,
                "INVALID_STUDENT_ID",
                "Student ID must be exactly 17 decimal digits preserving leading zeroes.",
                null);
        }

        await using var transaction = await _repository.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            var classId = await _repository.GetHoldClassIdAsync(holdId, cancellationToken);
            if (!classId.HasValue)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CancelHoldResult(false, "HOLD_NOT_FOUND", "Admission hold not found.", null);
            }

            // Lock parent class row first
            await _repository.GetClassWithLockAsync(classId.Value, cancellationToken);

            var hold = await _repository.GetHoldByIdAsync(holdId, cancellationToken);
            if (hold == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CancelHoldResult(false, "HOLD_NOT_FOUND", "Admission hold not found.", null);
            }

            // Ownership check
            if (hold.StudentId != studentId)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CancelHoldResult(
                    false,
                    "UNAUTHORIZED",
                    "Student ID does not match the owner of this hold.",
                    null);
            }

            if (hold.Status == HoldStatus.CONFIRMED)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CancelHoldResult(
                    false,
                    "CANNOT_CANCEL_CONFIRMED",
                    "Confirmed admission seats cannot be cancelled.",
                    hold);
            }

            if (hold.Status == HoldStatus.CANCELLED)
            {
                await transaction.CommitAsync(cancellationToken);
                return new CancelHoldResult(true, null, "Hold was already cancelled.", hold);
            }

            var utcNow = DateTime.UtcNow;

            if (hold.Status == HoldStatus.EXPIRED || hold.ExpiresAt <= utcNow)
            {
                if (hold.Status == HoldStatus.HELD)
                {
                    hold.Status = HoldStatus.EXPIRED;
                    await _repository.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return new CancelHoldResult(
                    false,
                    "HOLD_EXPIRED",
                    "Hold has already expired.",
                    hold);
            }

            // Transition HELD to CANCELLED
            hold.Status = HoldStatus.CANCELLED;
            await _repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Hold {HoldId} cancelled successfully by student {StudentId}",
                holdId,
                studentId);

            return new CancelHoldResult(true, null, "Hold cancelled successfully.", hold);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while cancelling hold {HoldId} for student {StudentId}", holdId, studentId);
            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch
            {
                // Ignore rollback secondary errors
            }

            return new CancelHoldResult(false, "DATABASE_ERROR", ex.Message, null);
        }
    }

    public async Task<HoldStatusDto?> GetHoldStatusAsync(
        Guid holdId,
        CancellationToken cancellationToken = default)
    {
        var hold = await _repository.GetHoldByIdAsync(holdId, cancellationToken);
        if (hold == null)
        {
            return null;
        }

        var isExpiredAccordingToClock = hold.Status == HoldStatus.HELD && hold.ExpiresAt <= DateTime.UtcNow;

        return new HoldStatusDto(
            hold.Id,
            hold.ClassId,
            hold.StudentId,
            hold.Status,
            hold.CreatedAt,
            hold.ExpiresAt,
            hold.ConfirmedAt,
            isExpiredAccordingToClock);
    }

    public async Task<ClassSeatCountsDto?> GetClassSeatCountsAsync(
        Guid classId,
        CancellationToken cancellationToken = default)
    {
        var admissionClass = await _repository.GetClassByIdAsync(classId, cancellationToken);
        if (admissionClass == null)
        {
            return null;
        }

        var utcNow = DateTime.UtcNow;
        var breakdown = await _repository.GetClassSeatBreakdownAsync(classId, utcNow, cancellationToken);

        return new ClassSeatCountsDto(
            classId,
            admissionClass.Name,
            breakdown.TotalSeats,
            breakdown.HeldSeats,
            breakdown.ConfirmedSeats,
            breakdown.AvailableSeats);
    }

    public async Task<List<AdmissionHold>> GetStudentHoldsAsync(
        string studentId,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidStudentId(studentId))
        {
            return new List<AdmissionHold>();
        }

        return await _repository.GetHoldsByStudentIdAsync(studentId, cancellationToken);
    }
}
