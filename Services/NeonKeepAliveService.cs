using Microsoft.EntityFrameworkCore;
using MonthExpenses.Api.Data;

namespace MonthExpenses.Api.Services;

/// <summary>
/// Hosted background service designed to keep Neon PostgreSQL serverless compute endpoints warm,
/// while respecting active operational windows to prevent exhausting free-tier compute hours.
/// </summary>
public class NeonKeepAliveService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NeonKeepAliveService> _logger;

    public NeonKeepAliveService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<NeonKeepAliveService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NeonKeepAliveService is starting background monitoring.");

        // Read keep-alive settings from configuration with sensible defaults
        var isEnabled = _configuration.GetValue("Neon:KeepAlive:Enabled", true);
        var intervalMinutes = Math.Max(1, _configuration.GetValue("Neon:KeepAlive:IntervalMinutes", 4));
        var activeWindowOnly = _configuration.GetValue("Neon:KeepAlive:ActiveWindowOnly", true);
        var startHourUtc = _configuration.GetValue("Neon:KeepAlive:StartHourUtc", 0);  // ~05:30 AM IST
        var endHourUtc = _configuration.GetValue("Neon:KeepAlive:EndHourUtc", 18);     // ~11:30 PM IST

        if (!isEnabled)
        {
            _logger.LogInformation("NeonKeepAliveService is disabled in configuration.");
            return;
        }

        // Initial delay on startup to allow application initialization & schema migrations to complete
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var nowUtc = DateTime.UtcNow;

                // Window check to protect free-tier compute hours quota
                if (activeWindowOnly && !IsWithinActiveWindow(nowUtc, startHourUtc, endHourUtc))
                {
                    _logger.LogDebug(
                        "Current time {Time} UTC is outside active keep-alive window ({Start} to {End} UTC). Skipping Neon ping to conserve compute quota.",
                        nowUtc, startHourUtc, endHourUtc);
                    continue;
                }

                await PingDatabaseAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error in NeonKeepAliveService cycle: {Message}", ex.Message);
            }
        }

        _logger.LogInformation("NeonKeepAliveService has stopped.");
    }

    private async Task PingDatabaseAsync(CancellationToken stoppingToken)
    {
        var startTime = DateTime.UtcNow;

        // Dedicated timeout of 25 seconds to account for cold instance spin-up
        using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        pingCts.CancelAfter(TimeSpan.FromSeconds(25));

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ExpenseDbContext>();

            // Executes lightweight heartbeat query against PostgreSQL
            await dbContext.Database.ExecuteSqlRawAsync("SELECT 1;", pingCts.Token);

            var elapsed = DateTime.UtcNow - startTime;
            if (elapsed.TotalSeconds > 2.0)
            {
                _logger.LogInformation(
                    "Neon keep-alive ping succeeded after cold-start wake-up delay of {ElapsedMs:N0} ms",
                    elapsed.TotalMilliseconds);
            }
            else
            {
                _logger.LogDebug("Neon keep-alive heartbeat successful ({ElapsedMs:N0} ms)", elapsed.TotalMilliseconds);
            }
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning("Neon keep-alive heartbeat timed out (server was likely cold or sleeping).");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Neon keep-alive ping encountered non-fatal connection error: {Message}", ex.Message);
        }
    }

    private static bool IsWithinActiveWindow(DateTime utcNow, int startHourUtc, int endHourUtc)
    {
        var hour = utcNow.Hour;
        if (startHourUtc <= endHourUtc)
        {
            return hour >= startHourUtc && hour <= endHourUtc;
        }

        // Handles wrap-around midnight
        return hour >= startHourUtc || hour <= endHourUtc;
    }
}

