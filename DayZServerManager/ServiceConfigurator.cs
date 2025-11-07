// Path: Core/Host/ServiceConfigurator.cs
// File: ServiceConfigurator.cs
// Purpose: DI – inkl. DiscordNotifier registrieren und verwendete Services bündeln.

using Core.Domain;
using Core.Domain.Services;
using Core.Logging;
using Microsoft.Extensions.DependencyInjection;
using Modules.ConfigService;
using Modules.InstanceRegistry;
using Modules.ProcessControl;
using Modules.Monitoring;
using Modules.RconService;
using Modules.RestartOrchestrator;
using Modules.ModUpdateService;
using Modules.DiscordNotifier;

namespace Core.Host;

public static class ServiceConfigurator
{
    public static ServiceProvider Configure()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IEventBus, EventBus>();

        services.AddSingleton<ILogService>(sp =>
            new CompositeLogService(
                new ConsoleLogService(),
                new FileLogService(Path.Combine(AppContext.BaseDirectory, "logs", "manager.log"))
            ));

        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<IInstanceRegistry, InstanceRegistry>();
        services.AddSingleton<IProcessController, ProcessController>();
        services.AddSingleton<IMonitoringService, MonitoringService>();
        services.AddSingleton<IRconService>(sp =>
            new RconService(
                sp.GetRequiredService<IEventBus>(),
                sp.GetRequiredService<ILogService>(),
                sp.GetRequiredService<IConfigService>(),
                dryRun: true));
        services.AddSingleton<IRestartOrchestrator, RestartOrchestrator>();
        services.AddSingleton<IModUpdateService, ModUpdateService>();

        // Discord Notifier
        services.AddSingleton<IDiscordNotifier, DiscordNotifier>();

        return services.BuildServiceProvider();
    }
}
