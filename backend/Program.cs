using Microsoft.EntityFrameworkCore;
using SchoolAdmissionSystem.Api.Data;

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

// Configure HOLD_DURATION_SECONDS (default 600)
var holdDurationSeconds = builder.Configuration.GetValue<int>("HOLD_DURATION_SECONDS", 600);

var app = builder.Build();

app.UseCors("AllowFrontend");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
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

        return Results.Ok(new
        {
            status = "Healthy",
            database = "Connected",
            holdDurationSeconds,
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
    version = "Phase 0 Scaffold",
    status = "Running",
    healthEndpoint = "/health",
    holdDurationSeconds
}));

app.Run();
