using Microsoft.AspNetCore.Mvc;
using SchoolAdmissionSystem.Api.DTOs;
using SchoolAdmissionSystem.Api.Models;
using SchoolAdmissionSystem.Api.Services;

namespace SchoolAdmissionSystem.Api;

public static class AdmissionEndpoints
{
    public static void MapAdmissionEndpoints(this WebApplication app)
    {
        app.MapPost("/classes", async (CreateClassRequest request, IAdmissionService service, CancellationToken ct) =>
        {
            var result = await service.CreateClassAsync(request.Name, request.TotalSeats, ct);
            if (!result.Success) return Error(result.ErrorCode, result.Message);
            var c = result.Class!;
            return Results.Created($"/classes/{c.ClassId}", new CreatedClassResponse(c.ClassId, c.ClassName, c.TotalSeats));
        }).WithName("CreateClass").WithTags("Classes").Produces<CreatedClassResponse>(201);

        app.MapGet("/classes", async (IAdmissionService service, CancellationToken ct) =>
            Results.Ok((await service.GetClassesAsync(ct)).Select(ToClassResponse).ToList()))
            .WithName("ListClasses").WithTags("Classes").Produces<List<ClassResponse>>();

        app.MapGet("/classes/{classId:guid}", async (Guid classId, IAdmissionService service, CancellationToken ct) =>
        {
            var counts = await service.GetClassSeatCountsAsync(classId, ct);
            return counts == null ? Error("CLASS_NOT_FOUND", "Class not found.") : Results.Ok(ToClassResponse(counts));
        }).WithName("GetClassSeats").WithTags("Classes").Produces<ClassResponse>();

        app.MapPost("/classes/{classId:guid}/holds", async (Guid classId,
            [FromHeader(Name = "X-Student-Id")] string? studentId, IAdmissionService service, CancellationToken ct) =>
        {
            var result = await service.ReserveSeatAsync(classId, studentId!, ct);
            if (!result.Success) return Error(result.ErrorCode, result.Message);
            var h = result.Hold!;
            return Results.Created($"/holds/{h.Id}", new CreatedHoldResponse(h.Id, h.ClassId, h.StudentId,
                h.Status.ToString(), h.ExpiresAt));
        }).WithName("ReserveSeat").WithTags("Holds").Produces<CreatedHoldResponse>(201).RequireStudentHeader();

        app.MapGet("/holds/{holdId:guid}", async (Guid holdId, IAdmissionService service, CancellationToken ct) =>
        {
            var hold = await service.GetHoldStatusAsync(holdId, ct);
            return hold == null ? Error("HOLD_NOT_FOUND", "Hold not found.")
                : Results.Ok(new HoldResponse(hold.HoldId, hold.ClassId, hold.Status.ToString(), hold.ExpiresAt));
        }).WithName("GetHoldStatus").WithTags("Holds").Produces<HoldResponse>();

        app.MapPost("/holds/{holdId:guid}/confirm", async (Guid holdId,
            [FromHeader(Name = "X-Student-Id")] string? studentId, IAdmissionService service, CancellationToken ct) =>
        {
            var result = await service.ConfirmHoldAsync(holdId, studentId!, ct);
            return result.Success ? Results.Ok(new ConfirmHoldResponse(result.Hold!.Id, result.Hold.Status.ToString()))
                : Error(result.ErrorCode, result.Message);
        }).WithName("ConfirmHold").WithTags("Holds").Produces<ConfirmHoldResponse>().RequireStudentHeader();

        app.MapDelete("/holds/{holdId:guid}", async (Guid holdId,
            [FromHeader(Name = "X-Student-Id")] string? studentId, IAdmissionService service, CancellationToken ct) =>
        {
            var result = await service.CancelHoldAsync(holdId, studentId!, ct);
            return result.Success ? Results.NoContent() : Error(result.ErrorCode, result.Message);
        }).WithName("CancelHold").WithTags("Holds").Produces(204).RequireStudentHeader();

        app.MapGet("/me/holds", async ([FromHeader(Name = "X-Student-Id")] string? studentId,
            IAdmissionService service, CancellationToken ct) =>
        {
            if (!AdmissionService.IsValidStudentId(studentId))
                return Error("INVALID_STUDENT_ID", "Student ID must be exactly 17 decimal digits preserving leading zeroes.");
            var holds = await service.GetStudentHoldsAsync(studentId!, ct);
            var now = DateTime.UtcNow;
            return Results.Ok(holds.Select(h => new StudentHoldResponse(h.Id, h.ClassId, h.Class!.Name,
                (h.Status == HoldStatus.HELD && h.ExpiresAt <= now ? HoldStatus.EXPIRED : h.Status).ToString(),
                h.ExpiresAt)).ToList());
        }).WithName("GetStudentHolds").WithTags("Students").Produces<List<StudentHoldResponse>>().RequireStudentHeader();
    }

    private static ClassResponse ToClassResponse(ClassSeatCountsDto c)
        => new(c.ClassId, c.ClassName, c.TotalSeats, c.HeldSeats, c.ConfirmedSeats, c.AvailableSeats);

    private static RouteHandlerBuilder RequireStudentHeader(this RouteHandlerBuilder builder)
        => builder.Produces<ApiError>(400).Produces<ApiError>(403).WithOpenApi(operation =>
        {
            operation.Parameters.Single(p => p.Name == "X-Student-Id").Required = true;
            return operation;
        });

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
