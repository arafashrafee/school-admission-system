using SchoolAdmissionSystem.Api.Models;

namespace SchoolAdmissionSystem.Api.DTOs;

public record CreateClassRequest(string? Name, int TotalSeats);
// Assignment HTTP response shapes; service results below remain internal to the API layer.
public record CreatedClassResponse(Guid Id, string Name, int TotalSeats);
public record ClassResponse(Guid Id, string Name, int TotalSeats, int HeldSeats, int ConfirmedSeats, int AvailableSeats);
public record CreatedHoldResponse(Guid HoldId, Guid ClassId, string StudentId, string Status, DateTime ExpiresAt);
public record HoldResponse(Guid HoldId, Guid ClassId, string Status, DateTime ExpiresAt);
public record ConfirmHoldResponse(Guid HoldId, string Status);
public record StudentHoldResponse(Guid HoldId, Guid ClassId, string ClassName, string Status, DateTime ExpiresAt);
public record ApiError(string Code, string Message);
public record CreateClassResult(bool Success, string? ErrorCode, string? Message, ClassSeatCountsDto? Class);

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
