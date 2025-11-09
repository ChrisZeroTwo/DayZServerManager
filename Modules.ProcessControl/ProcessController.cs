// Path: Modules/ProcessControl/ProcessController.cs
// File: ProcessController.cs
// Purpose: Start/Stop von DayZ-Instanzen. Direkter EXE-Start (kein cmd), sauberes Quoting, ausführliche Diagnose.

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

    private readonly ConcurrentDictionary<string, Process> _running = new(StringComparer.OrdinalIgnoreCase);
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
            PrepareRuntimeConfig(inst);

            var exe = ResolveServerExe(inst.ServerRoot);
            if (exe is null)
            {
                _starting.TryRemove(instanceName, out _);
                _log.Error($"[ProcessControl] Konnte Server-Exe nicht finden unter '{inst.ServerRoot}'.");
                return false;
            }

            var args = BuildCommandLine(inst);

            // Diagnosedatei (für manuellen Test)
            TryWriteLaunchScript(inst.ServerRoot, exe, args);

            // Direkter Start – KEIN cmd/cmd /k
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                WorkingDirectory = inst.ServerRoot,
                UseShellExecute = true,           // eigenes Fenster
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal,
                Verb = "open"
            };

            _log.Info($"[ProcessControl] Starte '{instanceName}' → {exe} {args}");
            var p = Process.Start(psi);
            if (p is null)
            {
                _starting.TryRemove(instanceName, out _);
                _log.Error($"[ProcessControl] Start fehlgeschlagen: '{instanceName}'.");
                return false;
            }

            p.EnableRaisingEvents = true;
            p.Exited += (_, __) =>
            {
                Process _removedProc;
                _running.TryRemove(instanceName, out _removedProc);
                _bus.Publish(new InstanceStoppedEvent(instanceName, "exited"));
            };

            _running[instanceName] = p;
            _bus.Publish(new InstanceStartedEvent(instanceName, DateTime.Now));
            _bus.Publish(new DiscordNotifyEvent("Instance Started", $"'{instanceName}' (PID {p.Id})", "info"));

            // Health-Probe (prüft Log/Exit; kein Fallback mehr)
            _ = Task.Run(() => HealthProbeAsync(inst, p));

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
            Process _removed;
            _running.TryRemove(instanceName, out _removed);
            _log.Warn($"[ProcessControl] Stop: Instanz '{instanceName}' läuft nicht.");
            return false;
        }

        try
        {
            if (kill)
            {
                _log.Warn($"[ProcessControl] Erzwinge Kill für '{instanceName}' (PID {p.Id}).");
                p.Kill(entireProcessTree: true);
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
            Process _removed2;
            _running.TryRemove(instanceName, out _removed2);
            _bus.Publish(new InstanceStoppedEvent(instanceName, kill ? "killed" : "stopped"));
            _bus.Publish(new DiscordNotifyEvent("Instance Stopped", $"'{instanceName}' beendet.", "warn"));
        }

        return true;
    }

    public bool IsRunning(string instanceName)
        => _running.TryGetValue(instanceName, out var p) && !p.HasExited;

    public int? GetPid(string instanceName)
        => _running.TryGetValue(instanceName, out var p) && !p.HasExited ? p.Id : null;

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
        static string Abs(string? p, string? baseDir = null)
        {
            if (string.IsNullOrWhiteSpace(p)) return string.Empty;
            if (Path.IsPathRooted(p)) return Path.GetFullPath(p);
            var root = string.IsNullOrWhiteSpace(baseDir) ? AppContext.BaseDirectory : baseDir!;
            return Path.GetFullPath(Path.Combine(root, p));
        }

        var args = new List<string>();

        // Ports
        if (inst.Launch?.Port > 0) args.Add($"-port={inst.Launch.Port}");
        if (inst.Launch?.QueryPort > 0) args.Add($"-queryport={inst.Launch.QueryPort}");

        // profiles & config – absolut
        if (!string.IsNullOrWhiteSpace(inst.ProfilesPath))
            args.Add($"-profiles=\"{Abs(inst.ProfilesPath)}\"");

        if (!string.IsNullOrWhiteSpace(inst.RuntimeConfigPath))
            args.Add($"-config=\"{Abs(inst.RuntimeConfigPath)}\"");

        // Defaults
        args.Add("-adminlog");
        args.Add("-freezecheck");
        args.Add("-dologs");

        // Mods
        var modList = BuildClientModList(inst);
        if (!string.IsNullOrWhiteSpace(modList))
            args.Add($"\"-mod={modList}\"");

        var serverModList = BuildServerModList(inst);
        if (!string.IsNullOrWhiteSpace(serverModList))
            args.Add($"\"-serverMod={serverModList}\"");

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

    private async Task HealthProbeAsync(InstanceInfo inst, Process p)
    {
        try
        {
            await Task.Delay(2000);
            if (p.HasExited)
            {
                Process _removed;
                _running.TryRemove(inst.Name, out _removed);
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
            const int stepMs = 500;

            while (elapsed < maxWaitSec * 1000)
            {
                if (File.Exists(expectedLog)) { found = true; break; }
                if (elapsed > 0 && elapsed % 30000 == 0)
                    _log.Info($"[ProcessControl] Warte auf Log '{logName}' … {elapsed / 1000}s/{maxWaitSec}s (Mods={modCount}).");

                await Task.Delay(stepMs);
                elapsed += stepMs;

                if (p.HasExited)
                {
                    Process _removed2;
                    _running.TryRemove(inst.Name, out _removed2);
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

    // --- Diagnose: Startskript schreiben ---
    private void TryWriteLaunchScript(string serverRoot, string exe, string args)
    {
        try
        {
            Directory.CreateDirectory(serverRoot);
            var bat = Path.Combine(serverRoot, "last_launch.cmd");
            var line = $"\"{exe}\" {args}";
            File.WriteAllText(bat, line + Environment.NewLine);
            _log.Info($"[ProcessControl] Launch-CMD geschrieben: {bat}");
        }
        catch (Exception ex)
        {
            _log.Warn($"[ProcessControl] Konnte Launch-CMD nicht schreiben: {ex.Message}");
        }
    }
}
