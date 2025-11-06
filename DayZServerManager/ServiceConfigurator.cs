// Path: Core/Host/ServiceConfigurator.cs
// File: ServiceConfigurator.cs
// Purpose: DI-Registrierung inkl. ConfigService + InstanceRegistry.

using Core.Domain;
using Core.Domain.Services;
using Core.Logging;
using Microsoft.Extensions.DependencyInjection;
using Modules.ConfigService;
using Modules.InstanceRegistry;

namespace Core.Host;

public static class ServiceConfigurator
{
    public static ServiceProvider Configure()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<ILogService, ConsoleLogService>();

        // Vollqualifiziert registrieren, falls VS noch zweifelt:
        services.AddSingleton<Core.Domain.Services.IConfigService, Modules.ConfigService.ConfigService>();
        services.AddSingleton<Core.Domain.Services.IInstanceRegistry, Modules.InstanceRegistry.InstanceRegistry>();

        return services.BuildServiceProvider();
    }
}
