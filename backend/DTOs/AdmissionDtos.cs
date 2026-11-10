using SchoolAdmissionSystem.Api.Models;

namespace SchoolAdmissionSystem.Api.DTOs;

public record ReserveSeatResult(
    bool Success,
    string? ErrorCode,
    string? Message,
    AdmissionHold? Hold);

public record ConfirmHoldResult(
    bool Success,
    string? ErrorCode,
    string? Message,
    AdmissionHold? Hold,
    bool WasAlreadyConfirmed = false);

public record CancelHoldResult(
    bool Success,
    string? ErrorCode,
    string? Message,
    AdmissionHold? Hold);

public record ClassSeatCountsDto(
    Guid ClassId,
    string ClassName,
    int TotalSeats,
    int HeldSeats,
    int ConfirmedSeats,
    int AvailableSeats);

public record HoldStatusDto(
    Guid HoldId,
    Guid ClassId,
    string StudentId,
    HoldStatus Status,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? ConfirmedAt,
    bool IsExpiredAccordingToClock);
