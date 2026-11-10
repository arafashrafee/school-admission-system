using SchoolAdmissionSystem.Api.DTOs;
using SchoolAdmissionSystem.Api.Models;

namespace SchoolAdmissionSystem.Api.Services;

public interface IAdmissionService
{
    Task<CreateClassResult> CreateClassAsync(string? name, int totalSeats, CancellationToken cancellationToken = default);
    Task<List<ClassSeatCountsDto>> GetClassesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically attempts to reserve a seat in the specified class for a student.
    /// Serializes with SELECT FOR UPDATE on the parent class, sweeps expired holds, checks duplicate student, and enforces capacity.
    /// </summary>
    Task<ReserveSeatResult> ReserveSeatAsync(
        Guid classId,
        string studentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms an existing seat hold for the owning student.
    /// Idempotent for repeated confirmations by the valid owner.
    /// Rejects expired or cancelled holds.
    /// </summary>
    Task<ConfirmHoldResult> ConfirmHoldAsync(
        Guid holdId,
        string studentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a valid unexpired HELD reservation for the owning student, immediately releasing capacity.
    /// Confirmed admissions cannot be cancelled.
    /// </summary>
    Task<CancelHoldResult> CancelHoldAsync(
        Guid holdId,
        string studentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current lifecycle status and clock expiration status for a hold.
    /// </summary>
    Task<HoldStatusDto?> GetHoldStatusAsync(
        Guid holdId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns total, held, confirmed, and available seat counts for a class.
    /// </summary>
    Task<ClassSeatCountsDto?> GetClassSeatCountsAsync(
        Guid classId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all holds for a student across all classes.
    /// </summary>
    Task<List<AdmissionHold>> GetStudentHoldsAsync(
        string studentId,
        CancellationToken cancellationToken = default);
}
