// Path: Core/Host/Program.cs
// File: Program.cs
// Purpose: DI aufbauen, Config laden, InstanceRegistry initialisieren, Testausgabe.

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
        var reg = provider.GetRequiredService<IInstanceRegistry>();

        bus.Subscribe<DiscordNotifyEvent>(e =>
            log.Info($"DiscordNotify: {e.Title} - {e.Message} ({e.Level})"));

        // Config initialisieren (./config/manager.json)
        var managerJson = Path.Combine(AppContext.BaseDirectory, "config", "manager.json");
        cfg.Initialize(managerJson);

        // Registry initialisieren und testen
        reg.Initialize();
        var all = reg.GetAll();
        log.Info($"[Host] Registry enthält {all.Count} Instanz(en).");
        foreach (var i in all)
            log.Info($"[Host]   -> {i.Name}");

        bus.Publish(new DiscordNotifyEvent("Manager", "InstanceRegistry initialisiert.", "info"));
        Console.WriteLine("DayZ Server Manager Host läuft. [Enter] beendet.");
        Console.ReadLine();
    }
}
