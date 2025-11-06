// Path: Core/Host/Program.cs
// File: Program.cs
// Purpose: Einstiegspunkt. DI aufbauen, ConfigService initialisieren, Testevent.

using Core.Domain;
using Core.Domain.Events;
using Core.Domain.Services;
using Core.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Core.Host;

public static class Program
{
    public static void Main(string[] args)
    {
        var provider = ServiceConfigurator.Configure();
        var bus = provider.GetRequiredService<IEventBus>();
        var log = provider.GetRequiredService<ILogService>();
        var cfg = provider.GetRequiredService<IConfigService>();

        bus.Subscribe<DiscordNotifyEvent>(e =>
            log.Info($"DiscordNotify: {e.Title} - {e.Message} ({e.Level})"));

        var managerJson = Path.Combine(AppContext.BaseDirectory, "config", "manager.json");
        cfg.Initialize(managerJson);

        var instances = cfg.GetInstances();
        log.Info($"Instanzen geladen: {instances.Count}");
        foreach (var i in instances)
            log.Info($" - {i.Name} @ {i.ServerRoot}");

        bus.Publish(new DiscordNotifyEvent("Manager", "Config geladen & Bootstrap abgeschlossen.", "info"));
        Console.WriteLine("DayZ Server Manager Host läuft. [Enter] beendet.");
        Console.ReadLine();
    }
}
