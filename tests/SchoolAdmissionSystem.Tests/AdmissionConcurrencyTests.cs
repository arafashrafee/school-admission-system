using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SchoolAdmissionSystem.Api.Configuration;
using SchoolAdmissionSystem.Api.Data;
using SchoolAdmissionSystem.Api.DTOs;
using SchoolAdmissionSystem.Api.Models;
using SchoolAdmissionSystem.Api.Services;
using Xunit;

namespace SchoolAdmissionSystem.Tests;

public class AdmissionConcurrencyTests
{
    private readonly string _connectionString;

    public AdmissionConcurrencyTests()
    {
        var rawConnection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=postgres;Port=5432;Database=school_admission;Username=postgres;Password=postgres";

        // Ensure Npgsql connection pool accommodates parallel test execution
        _connectionString = rawConnection.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase)
            ? rawConnection
            : $"{rawConnection.TrimEnd(';')};Maximum Pool Size=150;Connection Idle Lifetime=10;";
    }

    private ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    private IAdmissionService CreateAdmissionService(ApplicationDbContext context, int holdDurationSeconds = 600)
    {
        var repository = new AdmissionRepository(context);
        var options = Options.Create(new AdmissionOptions
        {
            HoldDurationSeconds = holdDurationSeconds,
            ExpirationSweepIntervalSeconds = 15
        });

        return new AdmissionService(
            repository,
            options,
            NullLogger<AdmissionService>.Instance);
    }

    [Fact]
    public async Task Concurrency_100ParallelRequests_On10SeatClass_NeverOversells_AndExactly10Succeed()
    {
        // A. Create a class with 10 seats
        await using var db = CreateDbContext();
        var testClass = new AdmissionClass
        {
            Id = Guid.NewGuid(),
            Name = $"Concurrency-Test-{Guid.NewGuid():N}",
            TotalSeats = 10,
            CreatedAt = DateTime.UtcNow
        };
        db.AdmissionClasses.Add(testClass);
        await db.SaveChangesAsync();

        try
        {
            // B. Submit at least 100 parallel reservation attempts from distinct 17-digit student IDs
            const int totalAttempts = 100;
            var tasks = new Task<ReserveSeatResult>[totalAttempts];

            for (var i = 0; i < totalAttempts; i++)
            {
                var studentId = i.ToString("D17");
                tasks[i] = Task.Run(async () =>
                {
                    await using var taskDb = CreateDbContext();
                    var service = CreateAdmissionService(taskDb);
                    return await service.ReserveSeatAsync(testClass.Id, studentId);
                });
            }

            var results = await Task.WhenAll(tasks);

            // C. Exactly 10 reservations succeed; the rest fail without overselling
            var successCount = results.Count(r => r.Success);
            var failureCount = results.Count(r => !r.Success);

            successCount.Should().Be(10, "exactly 10 seats exist in this class");
            failureCount.Should().Be(90, "all 90 remaining parallel requests should be rejected");

            // Database count must be exactly 10
            await using var verifyDb = CreateDbContext();
            var occupied = await verifyDb.AdmissionHolds
                .Where(h => h.ClassId == testClass.Id && (h.Status == HoldStatus.HELD || h.Status == HoldStatus.CONFIRMED))
                .CountAsync();

            occupied.Should().Be(10, "database occupied count must never exceed TotalSeats");
        }
        finally
        {
            // Cleanup isolated test data
            await using var cleanupDb = CreateDbContext();
            var holds = await cleanupDb.AdmissionHolds.Where(h => h.ClassId == testClass.Id).ToListAsync();
            cleanupDb.AdmissionHolds.RemoveRange(holds);
            var cls = await cleanupDb.AdmissionClasses.FindAsync(testClass.Id);
            if (cls != null) cleanupDb.AdmissionClasses.Remove(cls);
            await cleanupDb.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task DuplicateApplication_ForSameStudentInSameClass_IsRejected()
    {
        await using var db = CreateDbContext();
        var testClass = new AdmissionClass
        {
            Id = Guid.NewGuid(),
            Name = $"Duplicate-Test-{Guid.NewGuid():N}",
            TotalSeats = 5,
            CreatedAt = DateTime.UtcNow
        };
        db.AdmissionClasses.Add(testClass);
        await db.SaveChangesAsync();

        try
        {
            var service = CreateAdmissionService(db);
            const string studentId = "12345678901234567";

            // First reservation succeeds
            var first = await service.ReserveSeatAsync(testClass.Id, studentId);
            first.Success.Should().BeTrue();

            // Second reservation for same student must be rejected
            var second = await service.ReserveSeatAsync(testClass.Id, studentId);
            second.Success.Should().BeFalse();
            second.ErrorCode.Should().Be("DUPLICATE_APPLICATION");
        }
        finally
        {
            await using var cleanupDb = CreateDbContext();
            var holds = await cleanupDb.AdmissionHolds.Where(h => h.ClassId == testClass.Id).ToListAsync();
            cleanupDb.AdmissionHolds.RemoveRange(holds);
            var cls = await cleanupDb.AdmissionClasses.FindAsync(testClass.Id);
            if (cls != null) cleanupDb.AdmissionClasses.Remove(cls);
            await cleanupDb.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ExpiredHold_ReleasesCapacity_AndAllowsStudentToReapply()
    {
        await using var db = CreateDbContext();
        var testClass = new AdmissionClass
        {
            Id = Guid.NewGuid(),
            Name = $"Expire-Test-{Guid.NewGuid():N}",
            TotalSeats = 1, // Only 1 seat!
            CreatedAt = DateTime.UtcNow
        };
        db.AdmissionClasses.Add(testClass);
        await db.SaveChangesAsync();

        try
        {
            var service = CreateAdmissionService(db, holdDurationSeconds: 1);
            const string studentId = "99999999999999999";

            // First reservation takes the only seat
            var first = await service.ReserveSeatAsync(testClass.Id, studentId);
            first.Success.Should().BeTrue();

            // Artificially age the hold in the past
            var hold = await db.AdmissionHolds.FindAsync(first.Hold!.Id);
            hold!.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();

            // Student should now be able to reapply and reclaim the seat!
            var second = await service.ReserveSeatAsync(testClass.Id, studentId);
            second.Success.Should().BeTrue("expired hold should release capacity and not block reapplication");
            second.Hold!.Id.Should().NotBe(first.Hold.Id);

            // Verify previous hold was marked EXPIRED in database
            await using var verifyDb = CreateDbContext();
            var refreshedFirst = await verifyDb.AdmissionHolds.FindAsync(first.Hold.Id);
            refreshedFirst!.Status.Should().Be(HoldStatus.EXPIRED);
        }
        finally
        {
            await using var cleanupDb = CreateDbContext();
            var holds = await cleanupDb.AdmissionHolds.Where(h => h.ClassId == testClass.Id).ToListAsync();
            cleanupDb.AdmissionHolds.RemoveRange(holds);
            var cls = await cleanupDb.AdmissionClasses.FindAsync(testClass.Id);
            if (cls != null) cleanupDb.AdmissionClasses.Remove(cls);
            await cleanupDb.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Confirmation_ValidHold_Succeeds_AndRepeatedConfirmationIsIdempotent()
    {
        await using var db = CreateDbContext();
        var testClass = new AdmissionClass
        {
            Id = Guid.NewGuid(),
            Name = $"Confirm-Test-{Guid.NewGuid():N}",
            TotalSeats = 5,
            CreatedAt = DateTime.UtcNow
        };
        db.AdmissionClasses.Add(testClass);
        await db.SaveChangesAsync();

        try
        {
            var service = CreateAdmissionService(db);
            const string studentId = "88888888888888888";

            var reservation = await service.ReserveSeatAsync(testClass.Id, studentId);
            reservation.Success.Should().BeTrue();

            // First confirmation succeeds
            var confirm1 = await service.ConfirmHoldAsync(reservation.Hold!.Id, studentId);
            confirm1.Success.Should().BeTrue();
            confirm1.WasAlreadyConfirmed.Should().BeFalse();
            confirm1.Hold!.Status.Should().Be(HoldStatus.CONFIRMED);
            confirm1.Hold.ConfirmedAt.Should().NotBeNull();

            // G. Repeated confirmation is idempotent
            var confirm2 = await service.ConfirmHoldAsync(reservation.Hold.Id, studentId);
            confirm2.Success.Should().BeTrue();
            confirm2.WasAlreadyConfirmed.Should().BeTrue();

            // Verify seat count only counted once
            var counts = await service.GetClassSeatCountsAsync(testClass.Id);
            counts!.ConfirmedSeats.Should().Be(1);
            counts.HeldSeats.Should().Be(0);
        }
        finally
        {
            await using var cleanupDb = CreateDbContext();
            var holds = await cleanupDb.AdmissionHolds.Where(h => h.ClassId == testClass.Id).ToListAsync();
            cleanupDb.AdmissionHolds.RemoveRange(holds);
            var cls = await cleanupDb.AdmissionClasses.FindAsync(testClass.Id);
            if (cls != null) cleanupDb.AdmissionClasses.Remove(cls);
            await cleanupDb.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Confirmation_AfterExpiration_Fails()
    {
        await using var db = CreateDbContext();
        var testClass = new AdmissionClass
        {
            Id = Guid.NewGuid(),
            Name = $"Confirm-Expire-Test-{Guid.NewGuid():N}",
            TotalSeats = 5,
            CreatedAt = DateTime.UtcNow
        };
        db.AdmissionClasses.Add(testClass);
        await db.SaveChangesAsync();

        try
        {
            var service = CreateAdmissionService(db);
            const string studentId = "77777777777777777";

            var reservation = await service.ReserveSeatAsync(testClass.Id, studentId);
            reservation.Success.Should().BeTrue();

            // Age the hold
            var hold = await db.AdmissionHolds.FindAsync(reservation.Hold!.Id);
            hold!.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();

            // Confirmation should fail
            var confirm = await service.ConfirmHoldAsync(reservation.Hold.Id, studentId);
            confirm.Success.Should().BeFalse();
            confirm.ErrorCode.Should().Be("HOLD_EXPIRED");
        }
        finally
        {
            await using var cleanupDb = CreateDbContext();
            var holds = await cleanupDb.AdmissionHolds.Where(h => h.ClassId == testClass.Id).ToListAsync();
            cleanupDb.AdmissionHolds.RemoveRange(holds);
            var cls = await cleanupDb.AdmissionClasses.FindAsync(testClass.Id);
            if (cls != null) cleanupDb.AdmissionClasses.Remove(cls);
            await cleanupDb.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task UnauthorizedStudent_CannotConfirmOrCancelHold()
    {
        await using var db = CreateDbContext();
        var testClass = new AdmissionClass
        {
            Id = Guid.NewGuid(),
            Name = $"Auth-Test-{Guid.NewGuid():N}",
            TotalSeats = 5,
            CreatedAt = DateTime.UtcNow
        };
        db.AdmissionClasses.Add(testClass);
        await db.SaveChangesAsync();

        try
        {
            var service = CreateAdmissionService(db);
            const string ownerId = "11111111111111111";
            const string attackerId = "22222222222222222";

            var reservation = await service.ReserveSeatAsync(testClass.Id, ownerId);
            reservation.Success.Should().BeTrue();

            // Attacker cannot confirm
            var confirmAttacker = await service.ConfirmHoldAsync(reservation.Hold!.Id, attackerId);
            confirmAttacker.Success.Should().BeFalse();
            confirmAttacker.ErrorCode.Should().Be("UNAUTHORIZED");

            // Attacker cannot cancel
            var cancelAttacker = await service.CancelHoldAsync(reservation.Hold.Id, attackerId);
            cancelAttacker.Success.Should().BeFalse();
            cancelAttacker.ErrorCode.Should().Be("UNAUTHORIZED");

            // Legitimate owner can cancel
            var cancelOwner = await service.CancelHoldAsync(reservation.Hold.Id, ownerId);
            cancelOwner.Success.Should().BeTrue();
        }
        finally
        {
            await using var cleanupDb = CreateDbContext();
            var holds = await cleanupDb.AdmissionHolds.Where(h => h.ClassId == testClass.Id).ToListAsync();
            cleanupDb.AdmissionHolds.RemoveRange(holds);
            var cls = await cleanupDb.AdmissionClasses.FindAsync(testClass.Id);
            if (cls != null) cleanupDb.AdmissionClasses.Remove(cls);
            await cleanupDb.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Cancellation_ReleasesCapacity_Immediately()
    {
        await using var db = CreateDbContext();
        var testClass = new AdmissionClass
        {
            Id = Guid.NewGuid(),
            Name = $"Cancel-Capacity-Test-{Guid.NewGuid():N}",
            TotalSeats = 1,
            CreatedAt = DateTime.UtcNow
        };
        db.AdmissionClasses.Add(testClass);
        await db.SaveChangesAsync();

        try
        {
            var service = CreateAdmissionService(db);
            const string student1 = "33333333333333333";
            const string student2 = "44444444444444444";

            var res1 = await service.ReserveSeatAsync(testClass.Id, student1);
            res1.Success.Should().BeTrue();

            // Class is full now
            var res2BeforeCancel = await service.ReserveSeatAsync(testClass.Id, student2);
            res2BeforeCancel.Success.Should().BeFalse();
            res2BeforeCancel.ErrorCode.Should().Be("CAPACITY_EXHAUSTED");

            // Student 1 cancels
            var cancel = await service.CancelHoldAsync(res1.Hold!.Id, student1);
            cancel.Success.Should().BeTrue();

            // Student 2 can now reserve!
            var res2AfterCancel = await service.ReserveSeatAsync(testClass.Id, student2);
            res2AfterCancel.Success.Should().BeTrue();
        }
        finally
        {
            await using var cleanupDb = CreateDbContext();
            var holds = await cleanupDb.AdmissionHolds.Where(h => h.ClassId == testClass.Id).ToListAsync();
            cleanupDb.AdmissionHolds.RemoveRange(holds);
            var cls = await cleanupDb.AdmissionClasses.FindAsync(testClass.Id);
            if (cls != null) cleanupDb.AdmissionClasses.Remove(cls);
            await cleanupDb.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ConfirmCancelExpireRaces_PreserveInvariants()
    {
        await using var db = CreateDbContext();
        var testClass = new AdmissionClass
        {
            Id = Guid.NewGuid(),
            Name = $"Race-Test-{Guid.NewGuid():N}",
            TotalSeats = 2,
            CreatedAt = DateTime.UtcNow
        };
        db.AdmissionClasses.Add(testClass);
        await db.SaveChangesAsync();

        try
        {
            var service = CreateAdmissionService(db);
            const string student = "55555555555555555";

            var res = await service.ReserveSeatAsync(testClass.Id, student);
            res.Success.Should().BeTrue();
            var holdId = res.Hold!.Id;

            // Race: Parallel confirm and cancel on same hold
            var confirmTask = Task.Run(async () =>
            {
                await using var taskDb = CreateDbContext();
                var taskService = CreateAdmissionService(taskDb);
                return await taskService.ConfirmHoldAsync(holdId, student);
            });

            var cancelTask = Task.Run(async () =>
            {
                await using var taskDb = CreateDbContext();
                var taskService = CreateAdmissionService(taskDb);
                return await taskService.CancelHoldAsync(holdId, student);
            });

            await Task.WhenAll(confirmTask, cancelTask);
            var confirmRes = await confirmTask;
            var cancelRes = await cancelTask;

            // Invariant: One operation must win, state must be unambiguous (either CONFIRMED or CANCELLED)
            await using var verifyDb = CreateDbContext();
            var finalHold = await verifyDb.AdmissionHolds.FindAsync(holdId);

            (finalHold!.Status == HoldStatus.CONFIRMED || finalHold.Status == HoldStatus.CANCELLED)
                .Should().BeTrue("status must resolve cleanly to either CONFIRMED or CANCELLED");

            if (finalHold.Status == HoldStatus.CONFIRMED)
            {
                confirmRes.Success.Should().BeTrue();
                cancelRes.Success.Should().BeFalse();
            }
            else
            {
                cancelRes.Success.Should().BeTrue();
                confirmRes.Success.Should().BeFalse();
            }
        }
        finally
        {
            await using var cleanupDb = CreateDbContext();
            var holds = await cleanupDb.AdmissionHolds.Where(h => h.ClassId == testClass.Id).ToListAsync();
            cleanupDb.AdmissionHolds.RemoveRange(holds);
            var cls = await cleanupDb.AdmissionClasses.FindAsync(testClass.Id);
            if (cls != null) cleanupDb.AdmissionClasses.Remove(cls);
            await cleanupDb.SaveChangesAsync();
        }
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(25)]
    [InlineData(60000)]
    public async Task ConfirmationCancellationAndExpirationSweep_RaceToOneTerminalState(int expiresInMilliseconds)
    {
        await using var db = CreateDbContext();
        var offering = new AdmissionClass { Name = $"Three-Way-Race-{Guid.NewGuid():N}", TotalSeats = 1 };
        var hold = new AdmissionHold
        {
            ClassId = offering.Id, StudentId = "00000000000000404", Status = HoldStatus.HELD,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1), ExpiresAt = DateTime.UtcNow.AddMilliseconds(expiresInMilliseconds)
        };
        db.AdmissionClasses.Add(offering);
        db.AdmissionHolds.Add(hold);
        await db.SaveChangesAsync();
        try
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var confirm = Task.Run(async () =>
            {
                await using var context = CreateDbContext();
                await start.Task;
                return await CreateAdmissionService(context).ConfirmHoldAsync(hold.Id, hold.StudentId);
            });
            var cancel = Task.Run(async () =>
            {
                await using var context = CreateDbContext();
                await start.Task;
                return await CreateAdmissionService(context).CancelHoldAsync(hold.Id, hold.StudentId);
            });
            var sweep = Task.Run(async () =>
            {
                await using var context = CreateDbContext();
                await start.Task;
                return await new AdmissionRepository(context).ExpireAllOverdueHoldsAsync(DateTime.UtcNow);
            });
            start.SetResult();
            await Task.WhenAll(confirm, cancel, sweep);
            await using var verify = CreateDbContext();
            var final = await verify.AdmissionHolds.AsNoTracking().SingleAsync(h => h.Id == hold.Id);
            final.Status.Should().BeOneOf(HoldStatus.CONFIRMED, HoldStatus.CANCELLED, HoldStatus.EXPIRED);
            (await confirm).ErrorCode.Should().NotBe("DATABASE_ERROR");
            (await cancel).ErrorCode.Should().NotBe("DATABASE_ERROR");
            (await confirm).Success.Should().Be(final.Status == HoldStatus.CONFIRMED);
            (await cancel).Success.Should().Be(final.Status == HoldStatus.CANCELLED);
            if (expiresInMilliseconds < 0) final.Status.Should().Be(HoldStatus.EXPIRED);
            if (expiresInMilliseconds == 60000) final.Status.Should().NotBe(HoldStatus.EXPIRED);
            var counts = await CreateAdmissionService(verify).GetClassSeatCountsAsync(offering.Id);
            counts!.HeldSeats.Should().Be(0);
            counts.ConfirmedSeats.Should().Be(final.Status == HoldStatus.CONFIRMED ? 1 : 0);
            counts.AvailableSeats.Should().Be(final.Status == HoldStatus.CONFIRMED ? 0 : 1);
            var next = await CreateAdmissionService(verify).ReserveSeatAsync(offering.Id, "00000000000000405");
            next.Success.Should().Be(final.Status != HoldStatus.CONFIRMED);
        }
        finally
        {
            await using var cleanup = CreateDbContext();
            await cleanup.AdmissionHolds.Where(h => h.ClassId == offering.Id).ExecuteDeleteAsync();
            await cleanup.AdmissionClasses.Where(c => c.Id == offering.Id).ExecuteDeleteAsync();
        }
    }

}
