// Path: Modules/ProcessControl/ProcessController.cs
// File: ProcessController.cs
// Purpose: Start/Stop von DayZ-Instanzen. Nutzt IConfigService (DTOs), publiziert Events.

using System.Collections.Concurrent;
using System.Diagnostics;
using Core.Common;
using Core.Domain;
using Core.Domain.DTOs;
using Core.Domain.Events;
using Core.Domain.Services;
using Core.Logging;
using System.IO;
using System.Linq;
using System.Threading;

namespace Modules.ProcessControl;

public class ProcessController : IProcessController
{
    private readonly IConfigService _config;
    private readonly ILogService _log;
    private readonly IEventBus _bus;

    // Merkt sich laufende Prozesse pro Instanz
    private readonly ConcurrentDictionary<string, Process> _running = new(StringComparer.OrdinalIgnoreCase);
    // Guard: verhindert parallele Starts derselben Instanz
    private readonly ConcurrentDictionary<string, byte> _starting = new(StringComparer.OrdinalIgnoreCase);

    public ProcessController(IConfigService config, ILogService log, IEventBus bus)
    {
        _config = config;
        _log = log;
        _bus = bus;
    }

    public bool Start(string instanceName)
    {
        if (IsRunning(instanceName))
        {
            _log.Warn($"[ProcessControl] Start ignoriert – Instanz '{instanceName}' läuft bereits (PID={GetPid(instanceName)}).");
            return false;
        }

        // Doppelstart-Guard (z. B. CLI + Orchestrator gleichzeitig)
        if (!_starting.TryAdd(instanceName, 1))
        {
            _log.Warn($"[ProcessControl] Start ignoriert – Instanz '{instanceName}' wird bereits gestartet.");
            return false;
        }

        var inst = Find(instanceName);
        if (inst is null)
        {
            _starting.TryRemove(instanceName, out _);
            _log.Warn($"[ProcessControl] Unbekannte Instanz '{instanceName}'.");
            return false;
        }

        try
        {
            // 1) Runtime-Config vorbereiten (editierbare -> runtime kopieren)
            PrepareRuntimeConfig(inst);

            // 2) Exe und Startinfo bestimmen
            var exe = ResolveServerExe(inst.ServerRoot);
            if (exe is null)
            {
                _starting.TryRemove(instanceName, out _);
                _log.Error($"[ProcessControl] Konnte Server-Exe nicht finden unter '{inst.ServerRoot}'.");
                return false;
            }

            var args = BuildCommandLine(inst);

            // Sichtbares Fenster erzwingen:
            // cmd.exe /c start "<Fenstertitel>" "DayZServer_x64.exe" <args>
            var cmd = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
            var title = $"DayZ {inst.Name}";
            var psi = new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = $"/c start \"{title}\" \"{Path.GetFileName(exe)}\" {args}",
                WorkingDirectory = inst.ServerRoot,
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal,
                Verb = "open"
            };

            _log.Info($"[ProcessControl] Starte '{instanceName}' [via start] ␦ {Path.GetFileName(exe)} {args}");
            var starter = Process.Start(psi);
            if (starter is null)
            {
                _starting.TryRemove(instanceName, out _);
                _log.Error($"[ProcessControl] Start fehlgeschlagen: '{instanceName}'.");
                return false;
            }

            // Kurze Wartezeit, dann DayZ-Child-Prozess anhand Exe-Pfad suchen & registrieren (synchron, kein await)
            Process? serverProc = null;
            for (int i = 0; i < 20 && serverProc is null; i++) // bis ~2s
            {
                Thread.Sleep(100);
                serverProc = FindServerProcessByExe(exe);
            }

            if (serverProc is null)
            {
                // Fallback: Tracke zumindest den gestarteten cmd-Prozess
                _log.Warn("[ProcessControl] Konnte DayZ-Prozess nach Start nicht finden – tracke Starter-Prozess (cmd) als Fallback.");
                _running[instanceName] = starter;
            }
            else
            {
                _running[instanceName] = serverProc;
                _log.Info($"[ProcessControl] DayZ-Prozess erfasst (PID={serverProc.Id}).");
                serverProc.EnableRaisingEvents = true;
                serverProc.Exited += (_, __) =>
                {
                    _running.TryRemove(instanceName, out var _removedProc);
                    _bus.Publish(new InstanceStoppedEvent(instanceName, "exited"));
                };
            }

            _bus.Publish(new InstanceStartedEvent(instanceName, DateTime.Now));
            _bus.Publish(new DiscordNotifyEvent("Instance Started", $"'{instanceName}' (PID {_running[instanceName].Id})", "info"));

            // Health-Probe asynchron starten (früher Exit / Log-Erstellung)
            _ = Task.Run(() => HealthProbeAsync(inst, _running[instanceName]));

            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"[ProcessControl] Start-Fehler für '{instanceName}'.", ex);
            return false;
        }
        finally
        {
            _starting.TryRemove(instanceName, out _);
        }
    }

    public bool Stop(string instanceName, bool kill = false)
    {
        if (!_running.TryGetValue(instanceName, out var p) || p.HasExited)
        {
            _running.TryRemove(instanceName, out var _removedProc1);
            _log.Warn($"[ProcessControl] Stop: Instanz '{instanceName}' läuft nicht.");
            return false;
        }

        try
        {
            if (kill)
            {
                _log.Warn($"[ProcessControl] Erzwinge Kill für '{instanceName}' (PID {p.Id}).");
                p.Kill(entireProcessTree: true); // schließt auch gestartete Childs
            }
            else
            {
                _log.Info($"[ProcessControl] Versuche sanftes Beenden für '{instanceName}' (PID {p.Id}).");
                if (!p.CloseMainWindow())
                    p.Kill(entireProcessTree: true);
            }

            p.WaitForExit(TimeSpan.FromSeconds(10).Milliseconds);
        }
        catch (Exception ex)
        {
            _log.Warn($"[ProcessControl] Stop-Fehler für '{instanceName}': {ex.Message}");
        }
        finally
        {
            _running.TryRemove(instanceName, out var _removedProc2);
            _bus.Publish(new InstanceStoppedEvent(instanceName, kill ? "killed" : "stopped"));
            _bus.Publish(new DiscordNotifyEvent("Instance Stopped", $"'{instanceName}' beendet.", "warn"));
        }

        return true;
    }

    public bool IsRunning(string instanceName)
    {
        return _running.TryGetValue(instanceName, out var p) && !p.HasExited;
    }

    public int? GetPid(string instanceName)
    {
        return _running.TryGetValue(instanceName, out var p) && !p.HasExited ? p.Id : null;
    }

    // --- Helpers ---

    private InstanceInfo? Find(string name)
        => _config.GetInstances().FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));

    private void PrepareRuntimeConfig(InstanceInfo inst)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(inst.EditableConfigPath) || string.IsNullOrWhiteSpace(inst.RuntimeConfigPath))
                return;

            var src = inst.EditableConfigPath;
            var dst = inst.RuntimeConfigPath;

            FileUtils.EnsureDirectory(Path.GetDirectoryName(dst)!);

            if (!File.Exists(src))
            {
                _log.Warn($"[ProcessControl] EditableConfig fehlt: {src}");
                return;
            }

            File.Copy(src, dst, overwrite: true);
            _log.Info($"[ProcessControl] Config kopiert: {src} -> {dst}");
        }
        catch (Exception ex)
        {
            _log.Warn($"[ProcessControl] Konnte Config nicht kopieren: {ex.Message}");
        }
    }

    private static string? ResolveServerExe(string serverRoot)
    {
        var root = Path.GetFullPath(serverRoot);
        var candidates = new[]
        {
            Path.Combine(root, "DayZServer_x64.exe"),
            Path.Combine(root, "DayZServer.exe")
        };
        var exe = candidates.FirstOrDefault(File.Exists);
        return exe is null ? null : Path.GetFullPath(exe);
    }

    private static string BuildCommandLine(InstanceInfo inst)
    {
        // Helfer: Absolutpfade sicherstellen
        static string Abs(string? p, string? baseDir = null)
        {
            if (string.IsNullOrWhiteSpace(p)) return string.Empty;
            if (Path.IsPathRooted(p)) return Path.GetFullPath(p);
            var root = string.IsNullOrWhiteSpace(baseDir) ? AppContext.BaseDirectory : baseDir!;
            return Path.GetFullPath(Path.Combine(root, p));
        }

        var args = new List<string>();

        // Ports (nur Port & QueryPort aus DTO)
        if (inst.Launch?.Port > 0) args.Add($"-port={inst.Launch.Port}");
        if (inst.Launch?.QueryPort > 0) args.Add($"-queryport={inst.Launch.QueryPort}");

        // profiles & config – IMMER absolut + gequotet
        if (!string.IsNullOrWhiteSpace(inst.ProfilesPath))
            args.Add($"-profiles=\"{Abs(inst.ProfilesPath)}\"");

        // aktives cfg-File (serverDZ.cfg / .active)
        if (!string.IsNullOrWhiteSpace(inst.RuntimeConfigPath))
            args.Add($"-config=\"{Abs(inst.RuntimeConfigPath)}\"");

        // sinnvolle Defaults
        args.Add("-adminlog");
        args.Add("-freezecheck");
        args.Add("-dologs");

        // MOD-Listen
        var modList = BuildClientModList(inst);
        if (!string.IsNullOrWhiteSpace(modList))
            args.Add($"\"-mod={modList}\""); // gesamte Zuweisung quoten

        var serverModList = BuildServerModList(inst);
        if (!string.IsNullOrWhiteSpace(serverModList))
            args.Add($"\"-serverMod={serverModList}\""); // optional

        // Zusätzliche Argumente aus Launch (falls gesetzt)
        if (!string.IsNullOrWhiteSpace(inst.Launch?.AdditionalArgs))
            args.Add(inst.Launch.AdditionalArgs!.Trim());

        return string.Join(' ', args);
    }

    private static string BuildClientModList(InstanceInfo inst)
    {
        var result = new List<string>();

        if (inst.Mods is { Count: > 0 })
        {
            foreach (var m in inst.Mods)
            {
                var p = m?.LocalPath;
                if (string.IsNullOrWhiteSpace(p)) continue;

                var abs = Path.IsPathRooted(p)
                    ? Path.GetFullPath(p)
                    : Path.GetFullPath(Path.Combine(inst.ServerRoot, p));

                if (Directory.Exists(abs))
                    result.Add(abs);
            }
        }

        if (result.Count == 0 && Directory.Exists(inst.ServerRoot))
        {
            var atFolders = Directory.EnumerateDirectories(inst.ServerRoot, "@*",
                new EnumerationOptions { RecurseSubdirectories = false });
            foreach (var f in atFolders)
                result.Add(Path.GetFullPath(f));
        }

        return string.Join(';', result);
    }

    private static string BuildServerModList(InstanceInfo inst)
    {
        var serverMods = new List<string>();
        var sm = Path.Combine(inst.ServerRoot, "@servermods");
        if (Directory.Exists(sm))
            serverMods.Add(Path.GetFullPath(sm));
        return string.Join(';', serverMods);
    }

    // --- Health-Probe nach Start: prüft Exit & Log-Erstellung (dynamisches Timeout je nach Mod-Anzahl) ---
    private async Task HealthProbeAsync(InstanceInfo inst, Process p)
    {
        try
        {
            await Task.Delay(2000);
            if (p.HasExited)
            {
                _log.Warn($"[ProcessControl] Prozess für '{inst.Name}' endete kurz nach Start. ExitCode={p.ExitCode}.");
                _running.TryRemove(inst.Name, out var _removedProc);
                _bus.Publish(new InstanceStoppedEvent(inst.Name, $"exited({p.ExitCode})"));
                return;
            }

            var modCount = inst.Mods?.Count ?? 0;
            var maxWaitSec = Math.Min(180, 30 + (5 * modCount));
            var profiles = string.IsNullOrWhiteSpace(inst.ProfilesPath) ? inst.ServerRoot : inst.ProfilesPath!;
            var logName = string.IsNullOrWhiteSpace(inst.LogFileName) ? "server_console.log" : inst.LogFileName!;
            var expectedLog = Path.GetFullPath(Path.Combine(profiles, logName));

            var found = false;
            var elapsed = 0;
            var stepMs = 500;

            while (elapsed < maxWaitSec * 1000)
            {
                if (File.Exists(expectedLog)) { found = true; break; }
                if (elapsed > 0 && elapsed % 30000 == 0)
                    _log.Info($"[ProcessControl] Warte auf Log '{logName}' … {elapsed / 1000}s/{maxWaitSec}s (Mods={modCount}).");

                await Task.Delay(stepMs);
                elapsed += stepMs;

                if (p.HasExited)
                {
                    _log.Warn($"[ProcessControl] Prozess '{inst.Name}' beendet sich während Startphase. ExitCode={p.ExitCode}.");
                    _running.TryRemove(inst.Name, out var _removedProc2);
                    _bus.Publish(new InstanceStoppedEvent(inst.Name, $"exited({p.ExitCode})"));
                    return;
                }
            }

            if (!found)
            {
                _log.Warn($"[ProcessControl] Kein Log '{logName}' unter Profiles nach {maxWaitSec}s gefunden (erwartet: {expectedLog}). Prozess läuft: {!p.HasExited}. (Mods={modCount})");
                _log.Warn($"[ProcessControl] Prüfe Mission in serverDZ.cfg, Startparameter (-mod/-serverMod/-profiles), und WorkingDirectory.");
            }
            else
            {
                _log.Info($"[ProcessControl] Log erkannt: {expectedLog}");
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"[ProcessControl] HealthProbe-Fehler für '{inst.Name}': {ex.Message}");
        }
    }

    // Sucht DayZ-Serverprozess über den Exe-Pfad
    private static Process? FindServerProcessByExe(string exeFullPath)
    {
        static string SafeMainModulePath(Process pr)
        {
            try { return pr.MainModule?.FileName ?? string.Empty; }
            catch { return string.Empty; }
        }

        Process? best = null;
        foreach (var pr in Process.GetProcesses())
        {
            string name = string.Empty;
            try { name = pr.ProcessName; } catch { }

            if (!name.StartsWith("DayZServer", StringComparison.OrdinalIgnoreCase))
                continue;

            var path = SafeMainModulePath(pr);
            if (string.IsNullOrWhiteSpace(path)) continue;

            if (Path.GetFullPath(path).Equals(Path.GetFullPath(exeFullPath), StringComparison.OrdinalIgnoreCase))
            {
                // Nimm den jüngsten Start
                try
                {
                    if (best == null || pr.StartTime > best.StartTime)
                        best = pr;
                }
                catch { best = pr; }
            }
        }
        return best;
    }
}
