using SchoolAdmissionSystem.Api.DTOs;
using SchoolAdmissionSystem.Api.Models;
using SchoolAdmissionSystem.Api.Services;

namespace SchoolAdmissionSystem.Api;

public static class AdmissionEndpoints
{
    public static void MapAdmissionEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/classes", async (CreateClassRequest request, IAdmissionService service, CancellationToken ct) =>
        {
            var result = await service.CreateClassAsync(request.Name, request.TotalSeats, ct);
            return result.Success
                ? Results.Created($"/api/classes/{result.Class!.ClassId}/seats", result.Class)
                : Error(result.ErrorCode, result.Message);
        }).WithName("CreateClass").WithTags("Classes");

        api.MapGet("/classes", async (IAdmissionService service, CancellationToken ct) =>
            Results.Ok(await service.GetClassesAsync(ct)))
            .WithName("ListClasses").WithTags("Classes");

        api.MapGet("/classes/{classId:guid}/seats", async (Guid classId, IAdmissionService service, CancellationToken ct) =>
        {
            var counts = await service.GetClassSeatCountsAsync(classId, ct);
            return counts == null ? Error("CLASS_NOT_FOUND", "Class not found.") : Results.Ok(counts);
        }).WithName("GetClassSeats").WithTags("Classes");

        api.MapPost("/holds", async (ReserveSeatRequest request, IAdmissionService service, CancellationToken ct) =>
        {
            if (request.ClassId == Guid.Empty)
                return Error("INVALID_CLASS", "A nonempty classId is required.");
            var result = await service.ReserveSeatAsync(request.ClassId, request.StudentId!, ct);
            return result.Success
                ? Results.Created($"/api/holds/{result.Hold!.Id}", ToDto(result.Hold))
                : Error(result.ErrorCode, result.Message);
        }).WithName("ReserveSeat").WithTags("Holds");

        api.MapPost("/holds/{holdId:guid}/confirm", async (Guid holdId, StudentRequest request, IAdmissionService service, CancellationToken ct) =>
        {
            var result = await service.ConfirmHoldAsync(holdId, request.StudentId!, ct);
            return result.Success ? Results.Ok(ToDto(result.Hold!)) : Error(result.ErrorCode, result.Message);
        }).WithName("ConfirmHold").WithTags("Holds");

        api.MapPost("/holds/{holdId:guid}/cancel", async (Guid holdId, StudentRequest request, IAdmissionService service, CancellationToken ct) =>
        {
            var result = await service.CancelHoldAsync(holdId, request.StudentId!, ct);
            return result.Success ? Results.Ok(ToDto(result.Hold!)) : Error(result.ErrorCode, result.Message);
        }).WithName("CancelHold").WithTags("Holds");

        api.MapGet("/holds/{holdId:guid}", async (Guid holdId, IAdmissionService service, CancellationToken ct) =>
        {
            var hold = await service.GetHoldStatusAsync(holdId, ct);
            return hold == null ? Error("HOLD_NOT_FOUND", "Hold not found.") : Results.Ok(hold);
        }).WithName("GetHoldStatus").WithTags("Holds");

        api.MapGet("/students/{studentId}/holds", async (string studentId, IAdmissionService service, CancellationToken ct) =>
        {
            if (!AdmissionService.IsValidStudentId(studentId))
                return Error("INVALID_STUDENT_ID", "Student ID must be exactly 17 decimal digits preserving leading zeroes.");
            var now = DateTime.UtcNow;
            var holds = await service.GetStudentHoldsAsync(studentId, ct);
            return Results.Ok(holds.Select(h => ToDto(h, now)).ToList());
        }).WithName("GetStudentHolds").WithTags("Students");
    }

    private static HoldStatusDto ToDto(AdmissionHold hold, DateTime? now = null)
    {
        var expired = hold.Status == HoldStatus.HELD && hold.ExpiresAt <= (now ?? DateTime.UtcNow);
        return new(hold.Id, hold.ClassId, hold.StudentId, expired ? HoldStatus.EXPIRED : hold.Status,
            hold.CreatedAt, hold.ExpiresAt, hold.ConfirmedAt, expired);
    }

    private static IResult Error(string? code, string? message)
    {
        var status = code switch
        {
            "INVALID_CLASS" or "INVALID_STUDENT_ID" => StatusCodes.Status400BadRequest,
            "CLASS_NOT_FOUND" or "HOLD_NOT_FOUND" => StatusCodes.Status404NotFound,
            "UNAUTHORIZED" => StatusCodes.Status403Forbidden,
            "DUPLICATE_CLASS" or "DUPLICATE_APPLICATION" or "CAPACITY_EXHAUSTED" or
                "HOLD_EXPIRED" or "HOLD_CANCELLED" or "CANNOT_CANCEL_CONFIRMED" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
        return Results.Json(new ApiError(code ?? "INTERNAL_ERROR", status == 500
            ? "An unexpected error occurred." : message ?? "Request failed."), statusCode: status);
    }
}
