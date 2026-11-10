using Microsoft.Extensions.Options;
using SchoolAdmissionSystem.Api.Configuration;
using SchoolAdmissionSystem.Api.Data;

namespace SchoolAdmissionSystem.Api.Services;

/// <summary>
/// Background service that periodically sweeps and transitions overdue HELD records to EXPIRED.
/// Ensures seat capacity is actively recovered even if a class experiences zero incoming requests.
/// </summary>
public class HoldExpirationWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<AdmissionOptions> _options;
    private readonly ILogger<HoldExpirationWorker> _logger;

    public HoldExpirationWorker(
        IServiceProvider serviceProvider,
        IOptions<AdmissionOptions> options,
        ILogger<HoldExpirationWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = Math.Max(5, _options.Value.ExpirationSweepIntervalSeconds);
        var interval = TimeSpan.FromSeconds(intervalSeconds);

        _logger.LogInformation(
            "HoldExpirationWorker started. Interval: {IntervalSeconds}s, HoldDuration: {HoldDurationSeconds}s",
            intervalSeconds,
            _options.Value.HoldDurationSeconds);

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IAdmissionRepository>();

                var utcNow = DateTime.UtcNow;
                var expiredCount = await repository.ExpireAllOverdueHoldsAsync(utcNow, stoppingToken);

                if (expiredCount > 0)
                {
                    _logger.LogInformation(
                        "HoldExpirationWorker transitioned {Count} overdue holds to EXPIRED at {Timestamp:O}",
                        expiredCount,
                        utcNow);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred during background hold expiration sweep");
            }
        }

        _logger.LogInformation("HoldExpirationWorker stopped");
    }
}
