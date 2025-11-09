// Path: Modules/ModUpdateService/ModUpdateService.cs
// File: ModUpdateService.cs
// Purpose: Periodischer Update-Check (Server + Mods). Nutzt RestartOrchestrator.ScheduleRestart(string,int,int,string).
//          Optimiert: Restart nur bei echten Änderungen – erkennt diese über Marker-Datei (.modupdate.changed),
//          die vom ProvisioningService nur bei tatsächlichen Updates gesetzt wird.

using Core.Domain;
using Core.Domain.DTOs;
using Core.Domain.Services;
using Core.Logging;

namespace Modules.ModUpdateService;

public class ModUpdateService : IModUpdateService, IDisposable
{
    private readonly ILogService _log;
    private readonly IConfigService _config;
    private readonly IInstanceRegistry _registry;
    private readonly IProvisioningService _prov;
    private readonly IRestartOrchestrator _restart;

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    // Marker-Datei muss mit ProvisioningService übereinstimmen
    private const string ChangedMarkerName = ".modupdate.changed";

    public bool IsRunning { get; private set; }

    public ModUpdateService(
        ILogService log,
        IConfigService config,
        IInstanceRegistry registry,
        IProvisioningService prov,
        IRestartOrchestrator restart)
    {
        _log = log;
        _config = config;
        _registry = registry;
        _prov = prov;
        _restart = restart;

        Start();
    }

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LoopAsync(_loopCts.Token));
        _log.Info($"[ModUpdate] gestartet. Interval={_config.GetManagerConfig().UpdateCheckIntervalMinutes}m");
    }

    public void Stop()
    {
        if (!IsRunning) return;
        try { _loopCts?.Cancel(); } catch { }
        IsRunning = false;
        _log.Info("[ModUpdate] gestoppt.");
    }

    public async Task CheckNowAsync(CancellationToken token) => await CheckAllAsync(token);

    /// <summary>
    /// Manuell: Aktualisiert Mods/Server und plant Neustart nur, wenn der Provisioner echte Änderungen erkannt hat.
    /// </summary>
    public async Task<bool> UpdateInstanceModsAsync(string instanceName, CancellationToken token)
    {
        var inst = _registry.GetByName(instanceName);
        if (inst is null) { _log.Warn($"[ModUpdate] Unbekannte Instanz '{instanceName}'."); return false; }

        // Vorheriger Marker-Zustand (nur für Logs)
        var markerBefore = HasChangedMarker(inst.ServerRoot);

        // Reihenfolge: Mods laden/prüfen → installieren (nur bei Änderungen) → Server prüfen
        // (ProvisioningService führt Downloads/Spiegel nur bei echten Änderungen aus)
        var modsChanged = await _prov.DownloadModsAsync(inst.Name);
        if (modsChanged > 0)
            await _prov.InstallModsToInstanceAsync(inst.Name, preferJunction: false);

        await _prov.EnsureServerUpToDateAsync(inst.Name);

        var markerAfter = HasChangedMarker(inst.ServerRoot);

        if (markerAfter)
        {
            _log.Info($"[ModUpdate] Änderungen erkannt bei '{inst.Name}' (markerBefore={markerBefore}). Plane Neustart.");
            _restart.ScheduleRestart(inst.Name, 60, 60, "Updates installiert");
            ClearChangedMarker(inst.ServerRoot); // Doppel-Neustarts vermeiden
            return true;
        }

        _log.Info($"[ModUpdate] Keine Änderungen für '{inst.Name}'. Kein Neustart.");
        return false;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var min = Math.Max(1, _config.GetManagerConfig().UpdateCheckIntervalMinutes);
                await CheckAllAsync(ct);
                await Task.Delay(TimeSpan.FromMinutes(min), ct);
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                _log.Warn($"[ModUpdate] Hintergrundfehler: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
        }
    }

    /// <summary>
    /// Periodischer Check aller Instanzen:
    /// - Provisioning führt nur bei echten Änderungen Downloads/Kopien aus (dank SteamCMD-Parsing).
    /// - Danach prüfen wir den Marker. Nur wenn gesetzt → Restart planen & Marker löschen.
    /// </summary>
    private async Task CheckAllAsync(CancellationToken ct)
    {
        foreach (var inst in _registry.GetAll())
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var markerBefore = HasChangedMarker(inst.ServerRoot);

                // Server prüfen
                await _prov.EnsureServerUpToDateAsync(inst.Name);
                // Mods prüfen/ggf. laden (nur bei Änderungen)
                var modsChanged = await _prov.DownloadModsAsync(inst.Name);
                // Nur installieren, wenn sich wirklich etwas geändert hat
                if (modsChanged > 0)
                    await _prov.InstallModsToInstanceAsync(inst.Name, preferJunction: false);

                var markerAfter = HasChangedMarker(inst.ServerRoot);

                if (markerAfter)
                {
                    _log.Info($"[ModUpdate] Änderungen erkannt bei '{inst.Name}' (markerBefore={markerBefore}). Plane Neustart.");
                    _restart.ScheduleRestart(inst.Name, 60, 60, "Updates installiert");
                    ClearChangedMarker(inst.ServerRoot); // wichtig: einmalig konsumieren
                }
                else
                {
                    _log.Info($"[ModUpdate] Keine Änderungen für '{inst.Name}'. Kein Neustart.");
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"[ModUpdate] '{inst.Name}': Fehler beim Check: {ex.Message}");
            }
        }
    }

    // --- Marker-Utilities ---

    private static bool HasChangedMarker(string serverRoot)
    {
        try
        {
            var path = Path.Combine(serverRoot, ChangedMarkerName);
            return File.Exists(path);
        }
        catch { return false; }
    }

    private static void ClearChangedMarker(string serverRoot)
    {
        try
        {
            var path = Path.Combine(serverRoot, ChangedMarkerName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* ignore */ }
    }

    public void Dispose()
    {
        try { _loopCts?.Cancel(); } catch { }
    }
}
