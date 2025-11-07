// Path: Core/Host/Program.cs
// File: Program.cs
// Purpose: CLI + AutoStart + Services starten + DiscordNotifier initialisieren (vollständige Version).

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
        var sp = ServiceConfigurator.Configure();
        var bus = sp.GetRequiredService<IEventBus>();
        var log = sp.GetRequiredService<ILogService>();
        var cfg = sp.GetRequiredService<IConfigService>();
        var reg = sp.GetRequiredService<IInstanceRegistry>();
        var proc = sp.GetRequiredService<IProcessController>();
        var mon = sp.GetRequiredService<IMonitoringService>();
        var rcon = sp.GetRequiredService<IRconService>();
        var rst = sp.GetRequiredService<IRestartOrchestrator>();
        var mus = sp.GetRequiredService<IModUpdateService>();
        var dsn = sp.GetRequiredService<IDiscordNotifier>();

        bus.Subscribe<DiscordNotifyEvent>(e => log.Info($"DiscordNotify: {e.Title} - {e.Message} ({e.Level})"));

        var managerJson = Path.Combine(AppContext.BaseDirectory, "config", "manager.json");
        cfg.Initialize(managerJson);
        dsn.Initialize();   // Discord-Filter/Webhook aus manager.json lesen
        reg.Initialize();

        // === AutoStart ===
        var toAutoStart = reg.GetAll().Where(i => i.AutoStart).ToList();
        if (toAutoStart.Count > 0)
        {
            log.Info($"AutoStart: {toAutoStart.Count} Instanz(en) werden gestartet …");
            foreach (var i in toAutoStart)
            {
                log.Info($"AutoStart -> {i.Name}");
                var ok = proc.Start(i.Name);
                if (!ok) log.Warn($"AutoStart: Start von '{i.Name}' fehlgeschlagen.");
                Thread.Sleep(TimeSpan.FromSeconds(3));
            }
        }
        else
        {
            log.Info("AutoStart: Keine Instanzen mit AutoStart=true gefunden.");
        }
        // === /AutoStart ===

        mon.Start();
        mus.Start();

        log.Info("CLI: ls | start/stop/kill <name|all> | reload | lock/unlock <name> | bc <name> <msg> | kickall/shutdown <name> | rcon <name> <cmd> | restart <name> [sec] [reason] | cancel <name> | modcheck | modupdate <name|all> | exit");

        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Trim().Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToLowerInvariant();
            var a1 = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            var a2 = parts.Length > 2 ? parts[2].Trim() : string.Empty;
            var a3 = parts.Length > 3 ? parts[3].Trim() : string.Empty;

            switch (cmd)
            {
                case "ls":
                    foreach (var i in reg.GetAll())
                    {
                        var running = proc.IsRunning(i.Name) ? $"RUN (PID {proc.GetPid(i.Name)})" : "STOP";
                        var scheduled = (rst.IsScheduled(i.Name) ? " RESTART* " : "");
                        Console.WriteLine($"- {i.Name}  [{running}]{scheduled}");
                    }
                    break;

                case "start": ExecuteFor(a1, reg, name => proc.Start(name), log); break;
                case "stop": ExecuteFor(a1, reg, name => proc.Stop(name, kill: false), log); break;
                case "kill": ExecuteFor(a1, reg, name => proc.Stop(name, kill: true), log); break;

                case "reload":
                    log.Info("Reload...");
                    cfg.Reload();
                    reg.Initialize();
                    dsn.Initialize(); // Discord-Config neu einlesen
                    log.Info("Reload ok.");
                    break;

                case "lock": _ = rcon.LockAsync(a1); break;
                case "unlock": _ = rcon.UnlockAsync(a1); break;
                case "bc": _ = rcon.BroadcastAsync(a1, a3.Length > 0 ? $"{a2} {a3}" : a2); break;
                case "kickall": _ = rcon.KickAllAsync(a1); break;
                case "shutdown": _ = rcon.ShutdownGracefulAsync(a1); break;
                case "rcon": _ = rcon.SendRawAsync(a1, a3.Length > 0 ? $"{a2} {a3}" : a2); break;

                case "restart":
                    {
                        if (string.IsNullOrWhiteSpace(a1)) { log.Warn("restart <name> [seconds] [reason]"); break; }
                        var seconds = 60;
                        if (int.TryParse(a2, out var s) && s > 0) seconds = s;
                        var reason = string.IsNullOrWhiteSpace(a3) ? "Scheduled restart" : a3;
                        var ok = rst.ScheduleRestart(a1, visibleSeconds: seconds, totalSeconds: seconds, reason: reason, autoStartAfter: true);
                        if (!ok) log.Warn($"Restart konnte nicht geplant werden für '{a1}'.");
                        break;
                    }

                case "cancel":
                    if (!rst.CancelRestart(a1))
                        log.Warn($"Kein Restart für '{a1}' geplant.");
                    break;

                case "modcheck":
                    _ = mus.CheckNowAsync();
                    break;

                case "modupdate":
                    if (string.Equals(a1, "all", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var i in reg.GetAll())
                            _ = mus.UpdateInstanceModsAsync(i.Name);
                    }
                    else if (!string.IsNullOrWhiteSpace(a1))
                    {
                        _ = mus.UpdateInstanceModsAsync(a1);
                    }
                    else
                    {
                        log.Warn("Nutzung: modupdate <name|all>");
                    }
                    break;

                case "exit":
                    mon.Stop();
                    mus.Stop();
                    return;

                default:
                    log.Warn("Unbekannter Befehl.");
                    break;
            }
        }
    }

    private static void ExecuteFor(string arg, IInstanceRegistry reg, Func<string, bool> action, ILogService log)
    {
        if (string.Equals(arg, "all", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var i in reg.GetAll())
                action(i.Name);
            return;
        }
        if (string.IsNullOrWhiteSpace(arg)) { log.Warn("Bitte einen Instanznamen oder 'all' angeben."); return; }

        var inst = reg.GetByName(arg);
        if (inst is null) { log.Warn($"Unbekannte Instanz '{arg}'. Mit 'ls' anzeigen lassen."); return; }

        action(inst.Name);
    }
}
