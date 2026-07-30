using GoveeController.Application.Schedules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GoveeController.Infrastructure.Scheduling;

/// <summary>
/// Background loop that fires due <see cref="Domain.Schedules.Schedule"/> rows. Deliberately a thin
/// shell: it only ticks and delegates to <see cref="IScheduleService.RunDueSchedulesAsync"/>, which
/// holds all the real logic and is what's unit-tested (see SCHEDULED-SHORTCUTS-PLAN.md §3.4).
/// </summary>
public sealed class ScheduleRunnerService : BackgroundService
{
    // Frequent enough that a schedule fires within a minute of its due time (matched against the
    // 5-minute grace window with plenty of margin), infrequent enough not to matter for load.
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScheduleRunnerService> _logger;

    /// <summary>Creates the service.</summary>
    public ScheduleRunnerService(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<ScheduleRunnerService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Schedule runner started, ticking every {TickInterval}.", TickInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // A new scope per tick because AppDbContext/IScheduleRepository/IShortcutService are
                // scoped, while this BackgroundService itself is a singleton - resolving them
                // directly into the constructor above would fail at startup. See
                // SCHEDULED-SHORTCUTS-PLAN.md §3.4.
                using var scope = _scopeFactory.CreateScope();
                var scheduleService = scope.ServiceProvider.GetRequiredService<IScheduleService>();
                await scheduleService.RunDueSchedulesAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected on shutdown - let the loop exit below rather than logging this as a failure.
                break;
            }
            catch (Exception ex)
            {
                // A BackgroundService that lets an exception escape ExecuteAsync stops the entire
                // host by default (BackgroundServiceExceptionBehavior.StopHost) - one bad tick must
                // not take down light control for the rest of the app, so this is caught, logged,
                // and the loop continues. See SCHEDULED-SHORTCUTS-PLAN.md §3.4.
                _logger.LogError(ex, "Schedule runner tick failed unexpectedly; will retry on the next tick.");
            }

            await Task.Delay(TickInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
        }
    }
}
