// Path: Core/Host/Program.cs
// File: Program.cs
// Purpose: CLI: start/stop/kill <name|all> | provision <name|all> | mods dl|install <name|all> | ls | reload | exit
// - Robuste Namensauflösung (case-insensitive Match gegen Registry)
// - Unterstützt Sync- und Async-Methoden im IProcessController
// - Klare Erfolg-/Fehlermeldungen, inkl. Hinweis wenn keine Instanzen gefunden

using System.Reflection;
using Core.Domain;
using Core.Domain.Services;
using Core.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Core.Host;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var sp = ServiceConfigurator.Configure();
        var log = sp.GetRequiredService<ILogService>();
        var proc = sp.GetRequiredService<IProcessController>();
        var reg = sp.GetRequiredService<IInstanceRegistry>();
        var cfg = sp.GetRequiredService<IConfigService>();
        var prov = sp.GetRequiredService<IProvisioningService>();

        // --- Bootstrap: Config laden, Registry füllen, Hintergrunddienste starten ---
        cfg.Initialize("config\\manager.json");          // Pfad ggf. anpassen (siehe Hinweis unten)
        reg.Initialize();

        var updateSvc = sp.GetRequiredService<IModUpdateService>();
        updateSvc.Start();

        var mon = sp.GetRequiredService<IMonitoringService>();
        mon.Start();

        var discord = sp.GetRequiredService<IDiscordNotifier>();
        discord.Initialize();

        var web = sp.GetRequiredService<IWebApiService>();
        web.Start(); // respektiert intern Enable/Port aus der Config
                     // --- Ende Bootstrap ---

        log.Info("DayZ Server Manager Host läuft. CLI bereit.");
        PrintHelp();

        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToLowerInvariant();

            try
            {
                switch (cmd)
                {
                    case "exit":
                    case "quit":
                        return;

                    case "help":
                        PrintHelp();
                        break;

                    case "reload":
                        cfg.Reload();
                        log.Info("Reload ok.");
                        break;

                    case "ls":
                        ListInstances(reg, log);
                        break;

                    case "start":
                        await HandleStartStopAsync(proc, reg, start: true, parts.Skip(1).ToArray(), log);
                        break;

                    case "stop":
                        await HandleStartStopAsync(proc, reg, start: false, parts.Skip(1).ToArray(), log);
                        break;

                    case "kill":
                        await HandleKillAsync(proc, reg, parts.Skip(1).ToArray(), log);
                        break;

                    case "provision":
                        await HandleProvisionAsync(prov, reg, parts.Skip(1).ToArray(), log);
                        break;

                    case "mods":
                        await HandleModsAsync(prov, reg, parts.Skip(1).ToArray(), log);
                        break;

                    default:
                        log.Warn("Unbekannter Befehl.");
                        PrintHelp();
                        break;
                }
            }
            catch (Exception ex)
            {
                log.Warn($"CLI-Fehler: {ex.Message}");
            }
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("CLI: start/stop/kill <name|all> | provision <name|all> | mods dl|install <name|all> | ls | reload | exit");
    }

    private static void ListInstances(IInstanceRegistry reg, ILogService log)
    {
        var all = reg.GetAll().ToList();
        if (all.Count == 0) { log.Warn("Keine Instanzen in der Registry."); return; }
        log.Info($"Instanzen geladen: {all.Count}");
        foreach (var i in all) log.Info($" - {i.Name} @ {i.ServerRoot}");
    }

    // -------- Commands --------

    private static async Task HandleStartStopAsync(IProcessController proc, IInstanceRegistry reg, bool start, string[] args, ILogService log)
    {
        if (!TryParseTarget(args, out var rawTarget))
        {
            log.Warn("Nutzung: start/stop <name|all>");
            return;
        }

        var targets = ResolveTargets(reg, rawTarget);
        if (targets.Count == 0)
        {
            log.Warn(rawTarget.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? "Keine Instanzen in der Registry."
                : $"Instanz nicht gefunden: '{rawTarget}'. Versuche 'ls'.");
            return;
        }

        foreach (var name in targets)
        {
            var ok = await InvokeControllerAsync(proc,
                start
                    ? new[] { "StartInstanceAsync", "StartInstance", "StartAsync", "Start" }
                    : new[] { "StopInstanceAsync", "StopInstance", "StopAsync", "Stop" },
                name);

            if (ok) log.Info($"{(start ? "Start" : "Stop")} {name} ok.");
            else log.Warn($"{(start ? "Start" : "Stop")} {name} fehlgeschlagen (Methode nicht gefunden/Fehler).");
        }
    }

    private static async Task HandleKillAsync(IProcessController proc, IInstanceRegistry reg, string[] args, ILogService log)
    {
        if (!TryParseTarget(args, out var rawTarget))
        {
            log.Warn("Nutzung: kill <name|all>");
            return;
        }

        var targets = ResolveTargets(reg, rawTarget);
        if (targets.Count == 0)
        {
            log.Warn(rawTarget.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? "Keine Instanzen in der Registry."
                : $"Instanz nicht gefunden: '{rawTarget}'. Versuche 'ls'.");
            return;
        }

        foreach (var name in targets)
        {
            var ok = await InvokeControllerAsync(proc,
                new[] { "KillInstanceAsync", "KillInstance", "KillAsync", "Kill" },
                name);

            if (ok) log.Info($"Kill {name} ok.");
            else log.Warn($"Kill {name} fehlgeschlagen (Methode nicht gefunden/Fehler).");
        }
    }

    private static async Task HandleProvisionAsync(IProvisioningService prov, IInstanceRegistry reg, string[] args, ILogService log)
    {
        if (!TryParseTarget(args, out var rawTarget))
        {
            log.Warn("Nutzung: provision <name|all>");
            return;
        }

        var targets = ResolveTargets(reg, rawTarget);
        if (targets.Count == 0)
        {
            log.Warn(rawTarget.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? "Keine Instanzen in der Registry."
                : $"Instanz nicht gefunden: '{rawTarget}'. Versuche 'ls'.");
            return;
        }

        foreach (var name in targets)
        {
            log.Info($"[Provision] -> {name}");

            var ok1 = await prov.EnsureServerUpToDateAsync(name);
            if (!ok1) { log.Warn($"[Provision] {name}: Server-Update fehlgeschlagen."); continue; }

            var ok2 = await prov.EnsureInstanceStructureAsync(name);
            if (!ok2) { log.Warn($"[Provision] {name}: Instanzstruktur fehlgeschlagen."); continue; }

            await prov.DownloadModsAsync(name);
            await prov.InstallModsToInstanceAsync(name, preferJunction: false);
            log.Info($"[Provision] {name}: abgeschlossen.");
        }
    }

    private static async Task HandleModsAsync(IProvisioningService prov, IInstanceRegistry reg, string[] args, ILogService log)
    {
        if (args.Length < 2)
        {
            log.Warn("Nutzung: mods dl|install <name|all>");
            return;
        }

        var action = args[0].ToLowerInvariant();
        var rawTarget = string.Join(' ', args.Skip(1)).Trim();

        var targets = ResolveTargets(reg, rawTarget);
        if (targets.Count == 0)
        {
            log.Warn(rawTarget.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? "Keine Instanzen in der Registry."
                : $"Instanz nicht gefunden: '{rawTarget}'. Versuche 'ls'.");
            return;
        }

        foreach (var name in targets)
        {
            switch (action)
            {
                case "dl":
                case "download":
                    var n = await prov.DownloadModsAsync(name);
                    log.Info($"[Mods] {name}: {n} Mod(s) geprüft/geladen.");
                    break;

                case "install":
                    var m = await prov.InstallModsToInstanceAsync(name, preferJunction: false);
                    log.Info($"[Mods] {name}: {m} Mod(s) installiert/aktualisiert.");
                    break;

                default:
                    log.Warn("Nutzung: mods dl|install <name|all>");
                    return;
            }
        }
    }

    // -------- Helpers --------

    private static bool TryParseTarget(string[] args, out string target)
    {
        if (args.Length == 0) { target = ""; return false; }
        target = string.Join(' ', args).Trim();
        return !string.IsNullOrEmpty(target);
    }

    // Liefert eine validierte Namensliste:
    // - "all" -> alle Registry-Namen
    // - sonst: case-insensitive Match gegen Registry-Namen
    private static List<string> ResolveTargets(IInstanceRegistry reg, string rawTarget)
    {
        var all = reg.GetAll().Select(i => i.Name).ToList();
        if (rawTarget.Equals("all", StringComparison.OrdinalIgnoreCase))
            return all;

        var match = all.FirstOrDefault(n => string.Equals(n, rawTarget, StringComparison.OrdinalIgnoreCase));
        return match is null ? new List<string>() : new List<string> { match };
    }

    /// Flexibler Methodenaufruf (unterstützt Sync und Async). true bei Erfolg.
    private static async Task<bool> InvokeControllerAsync(object controller, string[] candidateMethodNames, string instanceName)
    {
        var t = controller.GetType();
        foreach (var name in candidateMethodNames)
        {
            var mi = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public, new[] { typeof(string) });
            if (mi == null) continue;

            try
            {
                var result = mi.Invoke(controller, new object[] { instanceName });
                if (result is Task task)
                    await task; // Async-Variante

                return true;
            }
            catch
            {
                // probiere nächsten Kandidaten
            }
        }
        return false;
    }
}
