// Path: Core/Host/ServiceConfigurator.cs
// File: ServiceConfigurator.cs
// Purpose: DI-Registrierung für Kern-Services (EventBus, Logging) und ConfigService.

using Core.Domain;
using Core.Domain.Services;
using Core.Logging;
using Microsoft.Extensions.DependencyInjection;
using Modules.ConfigService; // Implementierung

namespace Core.Host;

public static class ServiceConfigurator
{
    public static ServiceProvider Configure()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<ILogService, ConsoleLogService>();

        // Module
        services.AddSingleton<IConfigService, ConfigService>();

        return services.BuildServiceProvider();
    }
}
