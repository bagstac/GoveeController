using GoveeController.Application.Devices;
using GoveeController.Application.Shortcuts;
using GoveeController.Infrastructure.Govee;
using GoveeController.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoveeController.Infrastructure;

/// <summary>
/// Composition helper that wires every Infrastructure-layer implementation to its Application-layer
/// interface. Called once from the Web layer's Program.cs, which is the only place in the solution
/// that is allowed to know about both layers at once.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Govee HTTP client (with standard retry/backoff resilience for 429/5xx), the
    /// SQLite-backed shortcut repository, in-memory caching, and the Application-layer use-case
    /// services that depend on them.
    /// </summary>
    /// <param name="services">The DI container to register into.</param>
    /// <param name="configuration">
    /// App configuration. Expects a "Govee:ApiKey" value (populated via the GOVEE_API_KEY environment
    /// variable) and a "ConnectionStrings:ShortcutsDb" value (the SQLite file path/connection string).
    /// </param>
    public static IServiceCollection AddGoveeInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();

        services.Configure<GoveeApiOptions>(configuration.GetSection(GoveeApiOptions.SectionName));
        services.AddHttpClient<IGoveeApiClient, GoveeApiClient>()
            .AddStandardResilienceHandler();

        var connectionString = configuration.GetConnectionString("ShortcutsDb") ?? "Data Source=shortcuts.db";
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

        services.AddScoped<IShortcutRepository, ShortcutRepository>();
        services.AddScoped<IDeviceControlService, DeviceControlService>();
        services.AddScoped<IShortcutService, ShortcutService>();

        return services;
    }
}
