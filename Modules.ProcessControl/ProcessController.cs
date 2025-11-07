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

namespace Modules.ProcessControl;

public class ProcessController : IProcessController
{
    private readonly IConfigService _config;
    private readonly ILogService _log;
    private readonly IEventBus _bus;

    // Merkt sich laufende Prozesse pro Instanz
    private readonly ConcurrentDictionary<string, Process> _running = new(StringComparer.OrdinalIgnoreCase);

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

        var inst = Find(instanceName);
        if (inst is null)
        {
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
                _log.Error($"[ProcessControl] Konnte Server-Exe nicht finden unter '{inst.ServerRoot}'.");
                return false;
            }

            var args = BuildCommandLine(inst);
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                WorkingDirectory = inst.ServerRoot,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _log.Info($"[ProcessControl] Starte '{instanceName}' → {Path.GetFileName(exe)} {args}");
            var p = Process.Start(psi);
            if (p is null)
            {
                _log.Error($"[ProcessControl] Start fehlgeschlagen: '{instanceName}'.");
                return false;
            }

            _running[instanceName] = p;
            _bus.Publish(new InstanceStartedEvent(instanceName, DateTime.Now));
            _bus.Publish(new DiscordNotifyEvent("Instance Started", $"'{instanceName}' (PID {p.Id})", "info"));
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"[ProcessControl] Start-Fehler für '{instanceName}'.", ex);
            return false;
        }
    }

    public bool Stop(string instanceName, bool kill = false)
    {
        if (!_running.TryGetValue(instanceName, out var p) || p.HasExited)
        {
            _running.TryRemove(instanceName, out _);
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
                // Hinweis: DayZ hat keinen STDIN-Exit; hier sanft via CloseMainWindow(), sonst Kill.
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
            _running.TryRemove(instanceName, out _);
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
        // Übliche Namen; wähle den ersten Treffer
        var candidates = new[]
        {
            Path.Combine(serverRoot, "DayZServer_x64.exe"),
            Path.Combine(serverRoot, "DayZServer.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string BuildCommandLine(InstanceInfo inst)
    {
        // Minimal: Ports, profiles, cfg + Mods aus DTO
        var args = new List<string>();

        if (inst.Launch?.Port > 0) args.Add($"-port={inst.Launch.Port}");
        if (inst.Launch?.QueryPort > 0) args.Add($"-queryport={inst.Launch.QueryPort}");
        if (!string.IsNullOrWhiteSpace(inst.ProfilesPath)) args.Add($"-profiles=\"{inst.ProfilesPath}\"");
        if (!string.IsNullOrWhiteSpace(inst.RuntimeConfigPath)) args.Add($"-config=\"{inst.RuntimeConfigPath}\"");

        // Mods (client + server)
        if (inst.Mods?.Count > 0)
        {
            var modList = string.Join(';', inst.Mods.Select(m => m.LocalPath).Where(p => !string.IsNullOrWhiteSpace(p)));
            if (!string.IsNullOrWhiteSpace(modList))
                args.Add($"-mod=\"{modList}\"");
        }

        if (!string.IsNullOrWhiteSpace(inst.Launch?.AdditionalArgs))
            args.Add(inst.Launch.AdditionalArgs);

        return string.Join(' ', args);
    }
}
