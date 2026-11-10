using Microsoft.EntityFrameworkCore;
using SchoolAdmissionSystem.Api.Configuration;
using SchoolAdmissionSystem.Api.Data;
using SchoolAdmissionSystem.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Configure Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=postgres;Port=5432;Database=school_admission;Username=postgres;Password=postgres";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Register Repository and Business Service
builder.Services.AddScoped<IAdmissionRepository, AdmissionRepository>();
builder.Services.AddScoped<IAdmissionService, AdmissionService>();

// Configure Admission & Expiration Options
var holdDurationSeconds = builder.Configuration.GetValue<int>("HOLD_DURATION_SECONDS", 600);
builder.Services.Configure<AdmissionOptions>(options =>
{
    options.HoldDurationSeconds = holdDurationSeconds;
    options.ExpirationSweepIntervalSeconds = 15;
});

// Register Background Expiration Worker
builder.Services.AddHostedService<HoldExpirationWorker>();

var app = builder.Build();

app.UseCors("AllowFrontend");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Automatically apply pending EF Core migrations at startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("Applying EF Core database migrations...");
        await db.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully.");

        // Idempotent initial seed if database has no admission classes
        if (!await db.AdmissionClasses.AnyAsync())
        {
            logger.LogInformation("Seeding default AdmissionClass: Class 1 (50 seats)");
            db.AdmissionClasses.Add(new SchoolAdmissionSystem.Api.Models.AdmissionClass
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "Class 1",
                TotalSeats = 50,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Default seed completed.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while applying database migrations.");
        throw;
    }
}

// Health check endpoint verifying database readiness
app.MapGet("/health", async (ApplicationDbContext db, ILogger<Program> logger) =>
{
    try
    {
        var canConnect = await db.Database.CanConnectAsync();
        if (!canConnect)
        {
            logger.LogWarning("Health check failed: unable to connect to database");
            return Results.Json(new
            {
                status = "Unhealthy",
                database = "Disconnected",
                holdDurationSeconds,
                timestamp = DateTime.UtcNow
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var classCount = await db.AdmissionClasses.CountAsync();

        return Results.Ok(new
        {
            status = "Healthy",
            database = "Connected",
            holdDurationSeconds,
            classCount,
            timestamp = DateTime.UtcNow
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Health check failed with exception");
        return Results.Json(new
        {
            status = "Unhealthy",
            database = "Error",
            error = ex.Message,
            holdDurationSeconds,
            timestamp = DateTime.UtcNow
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

// Informational root endpoint
app.MapGet("/", () => Results.Ok(new
{
    service = "School Admission System API",
    version = "Phase 1A Database Foundation",
    status = "Running",
    healthEndpoint = "/health",
    holdDurationSeconds
}));

app.Run();
