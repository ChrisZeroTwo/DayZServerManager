// Path: Modules/ModUpdateService/ModUpdateService.cs
// File: ModUpdateService.cs
// Purpose: Periodischer Workshop-Check & steamcmd-Updates; meldet Events und stößt Neustart über Events an.

using System.Diagnostics;
using Core.Common;
using Core.Domain;
using Core.Domain.DTOs;
using Core.Domain.Events;
using Core.Domain.Services;
using Core.Logging;

namespace Modules.ModUpdateService;

public class ModUpdateService : IModUpdateService, IDisposable
{
    private readonly IConfigService _config;
    private readonly ILogService _log;
    private readonly IEventBus _bus;

    private System.Threading.Timer? _timer;
    private readonly object _gate = new();

    public bool IsRunning { get; private set; }

    // Heuristik: Wir erkennen "Update verfügbar" pragmatisch,
    // wenn das Mod-Verzeichnis fehlt ODER sehr alt ist (z.B. 24h) – als Platzhalter,
    // bis eine echte Versionsprüfung/Manifest-Auswertung ergänzt wird.
    private const int HeuristicAgeSeconds = 24 * 60 * 60;

    public ModUpdateService(IConfigService config, ILogService log, IEventBus bus)
    {
        _config = config;
        _log = log;
        _bus = bus;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning) return;
            var interval = Math.Max(60, _config.GetManagerConfig().ModCheckIntervalSeconds);
            _timer = new System.Threading.Timer(async _ => await SafeCheckTickAsync(), null, TimeSpan.Zero, TimeSpan.FromSeconds(interval));
            IsRunning = true;
            _log.Info($"[ModUpdate] gestartet. Interval={interval}s");
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            IsRunning = false;
            _log.Info("[ModUpdate] gestoppt.");
        }
    }

    public async Task CheckNowAsync(CancellationToken ct = default)
    {
        await CheckAllAsync(ct);
    }

    public async Task<bool> UpdateInstanceModsAsync(string instanceName, CancellationToken ct = default)
    {
        var inst = _config.GetInstances().FirstOrDefault(i => string.Equals(i.Name, instanceName, StringComparison.OrdinalIgnoreCase));
        if (inst is null)
        {
            _log.Warn($"[ModUpdate] Unbekannte Instanz '{instanceName}'.");
            return false;
        }

        var manager = _config.GetManagerConfig();
        if (string.IsNullOrWhiteSpace(manager.SteamCmdPath))
        {
            _log.Warn("[ModUpdate] steamcmd.exe-Pfad fehlt in manager.json.");
            return false;
        }

        var updated = new List<long>();
        foreach (var mod in inst.Mods)
        {
            var ok = await DownloadWorkshopModAsync(manager.SteamCmdPath, mod.WorkshopId, ct);
            if (ok)
            {
                updated.Add(mod.WorkshopId);
                // Hinweis: Kopier-/Sync-Strategie in dieses Projekt zu integrieren,
                // z. B. vom ModCache in instanzspezifischen @Mod-Ordner – abhängig von deiner Ordnerpolitik.
                // Platzhalter: hier nur Event + Log; das Kopieren kann ein separates "ModDeploy"-Modul übernehmen.
                _log.Info($"[ModUpdate] Mod aktualisiert: {mod.Name} ({mod.WorkshopId})");
            }
        }

        if (updated.Count > 0)
        {
            _bus.Publish(new InstanceUpdateCompletedEvent(inst.Name, updated));
            // Neustart anstoßen: Orchestrator übernimmt den Ablauf.
            _bus.Publish(new InstanceNeedsRestartEvent(inst.Name, "Mods updated"));
            return true;
        }

        return false;
    }

    // ---- intern ----

    private async Task SafeCheckTickAsync()
    {
        try { await CheckAllAsync(); }
        catch (Exception ex) { _log.Warn($"[ModUpdate] Tick-Fehler: {ex.Message}"); }
    }

    private async Task CheckAllAsync(CancellationToken ct = default)
    {
        var manager = _config.GetManagerConfig();
        var instances = _config.GetInstances();

        foreach (var inst in instances)
        {
            foreach (var mod in inst.Mods)
            {
                if (IsModLikelyOutdated(mod))
                {
                    _log.Info($"[ModUpdate] Update-Verdacht: {inst.Name} -> {mod.Name} ({mod.WorkshopId})");
                    _bus.Publish(new ModUpdateAvailableEvent(inst.Name, mod.WorkshopId, mod.Name));
                }
            }
        }

        await Task.CompletedTask;
    }

    private static bool IsModLikelyOutdated(ModInfo mod)
    {
        // Heuristik: wenn LocalPath leer/fehlend oder "alt"
        if (string.IsNullOrWhiteSpace(mod.LocalPath) || !Directory.Exists(mod.LocalPath))
            return true;

        // Prüfe irgendeine Datei im Mod-Ordner (z. B. meta.cpp)
        var meta = Path.Combine(mod.LocalPath, "meta.cpp");
        var probe = File.Exists(meta) ? meta : Directory.GetFiles(mod.LocalPath, "*.*", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (probe is null) return true;

        return FileUtils.FileIsOlderThan(probe, HeuristicAgeSeconds);
    }

    private async Task<bool> DownloadWorkshopModAsync(string steamCmdPath, long workshopId, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(steamCmdPath))
            {
                _log.Warn($"[ModUpdate] steamcmd.exe nicht gefunden: {steamCmdPath}");
                return false;
            }

            // Beispiel-Aufruf:
            // steamcmd +login anonymous +workshop_download_item 221100 <id> validate +quit
            var psi = new ProcessStartInfo
            {
                FileName = steamCmdPath,
                Arguments = $"+login anonymous +workshop_download_item 221100 {workshopId} validate +quit",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _log.Info($"[ModUpdate] Starte steamcmd für WorkshopId={workshopId}");
            using var p = Process.Start(psi);
            if (p is null)
            {
                _log.Error($"[ModUpdate] Konnte steamcmd nicht starten (WorkshopId={workshopId}).");
                return false;
            }

            // einfache Auslese/Timeout
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();

            // warte max. 10 Minuten
            var finished = p.WaitForExit((int)TimeSpan.FromMinutes(10).TotalMilliseconds);
            var stdout = await outTask;
            var stderr = await errTask;

            if (!finished)
            {
                try { p.Kill(true); } catch { /* ignore */ }
                _log.Warn($"[ModUpdate] steamcmd Timeout für {workshopId}");
                return false;
            }

            // Grobe Erfolgserkennung (heuristisch)
            var ok = p.ExitCode == 0 && stdout.Contains("Success", StringComparison.OrdinalIgnoreCase) || stdout.Contains("Downloading item", StringComparison.OrdinalIgnoreCase);
            if (!ok)
                _log.Warn($"[ModUpdate] steamcmd meldete keinen Erfolg für {workshopId}. Exit={p.ExitCode}. STDERR: {stderr}");

            return ok;
        }
        catch (Exception ex)
        {
            _log.Warn($"[ModUpdate] Fehler bei steamcmd für {workshopId}: {ex.Message}");
            return false;
        }
        finally
        {
            await Task.CompletedTask;
        }
    }

    public void Dispose() => Stop();
}
