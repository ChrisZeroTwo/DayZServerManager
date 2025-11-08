// Path: Modules/ModUpdateService/ModUpdateService.cs
// File: ModUpdateService.cs
// Purpose: Periodischer Update-Check (Server + Mods). Nutzt RestartOrchestrator.ScheduleRestart(string,int,int,string).

using Core.Domain;
using Core.Domain.DTOs;
using Core.Domain.Services;
using Core.Logging;
using System.Text;

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

    public async Task<bool> UpdateInstanceModsAsync(string instanceName, CancellationToken token)
    {
        var inst = _registry.GetByName(instanceName);
        if (inst is null) { _log.Warn($"[ModUpdate] Unbekannte Instanz '{instanceName}'."); return false; }

        var before = Fingerprint(inst);
        await _prov.DownloadModsAsync(inst.Name);
        await _prov.InstallModsToInstanceAsync(inst.Name, preferJunction: false);
        var after = Fingerprint(inst);

        var changed = before != after;
        if (changed)
        {
            // Signatur deines Orchestrators: (string, int, int, string)
            _restart.ScheduleRestart(inst.Name, 60, 60, "Updates installiert");
        }
        return changed;
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

    private async Task CheckAllAsync(CancellationToken ct)
    {
        foreach (var inst in _registry.GetAll())
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var before = Fingerprint(inst);

                await _prov.EnsureServerUpToDateAsync(inst.Name);
                await _prov.DownloadModsAsync(inst.Name);
                await _prov.InstallModsToInstanceAsync(inst.Name, preferJunction: false);

                var after = Fingerprint(inst);
                if (before != after)
                {
                    _log.Info($"[ModUpdate] Änderungen erkannt bei '{inst.Name}'. Plane Neustart.");
                    _restart.ScheduleRestart(inst.Name, 60, 60, "Updates installiert");
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"[ModUpdate] '{inst.Name}': Fehler beim Check: {ex.Message}");
            }
        }
    }

    private static string Fingerprint(InstanceInfo inst)
    {
        static long SumDir(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return 0;
            long s = 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { s += File.GetLastWriteTimeUtc(f).Ticks; } catch { }
            }
            return s;
        }

        long sum = SumDir(inst.ServerRoot);
        if (inst.Mods != null)
        {
            foreach (var m in inst.Mods)
            {
                var modDir = Path.Combine(inst.ServerRoot, !string.IsNullOrWhiteSpace(m.Name) ? $"@{m.Name}" : $"@{m.WorkshopId}");
                sum += SumDir(modDir);
            }
        }
        return sum.ToString();
    }

    public void Dispose()
    {
        try { _loopCts?.Cancel(); } catch { }
    }
}
