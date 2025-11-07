// Path: Modules/Monitoring/MonitoringService.cs
// File: MonitoringService.cs
// Purpose: Überwacht Logfiles. Liest Config dynamisch (pro Tick), meldet Freeze mit Debounce und prüft optional nur laufende Instanzen.

using Core.Domain;
using Core.Domain.DTOs;
using Core.Domain.Events;
using Core.Domain.Services;
using Core.Logging;

namespace Modules.Monitoring;

public class MonitoringService : IMonitoringService, IDisposable
{
    private readonly IConfigService _config;
    private readonly ILogService _log;
    private readonly IEventBus _bus;
    private readonly IProcessController _proc;

    private System.Threading.Timer? _timer;
    private readonly object _gate = new();

    // aktuelle, zur Laufzeit änderbare Werte
    private int _intervalSeconds;
    private int _freezeThresholdSeconds;
    private int _debounceSeconds;
    private bool _onlyWhenRunning;

    private readonly Dictionary<string, DateTime> _lastAlert = new(StringComparer.OrdinalIgnoreCase);

    public bool IsRunning { get; private set; }

    public MonitoringService(IConfigService config, ILogService log, IEventBus bus, IProcessController proc)
    {
        _config = config;
        _log = log;
        _bus = bus;
        _proc = proc;
        // Kein LoadConfig() im Konstruktor – ConfigService könnte noch nicht initialisiert sein.
    }

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning) return;

            // Erst jetzt laden – ConfigService ist bereits initialisiert
            LoadConfig(initial: true);

            _timer = new System.Threading.Timer(
                OnTick,
                null,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(Math.Max(5, _intervalSeconds)));

            IsRunning = true;
            _log.Info($"[Monitoring] gestartet. Interval={_intervalSeconds}s, FreezeThreshold={_freezeThresholdSeconds}s, Debounce={_debounceSeconds}s, OnlyWhenRunning={_onlyWhenRunning}");
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            IsRunning = false;
            _log.Info("[Monitoring] gestoppt.");
        }
    }

    private void OnTick(object? _)
    {
        try
        {
            // Konfiguration bei jedem Tick laden (Runtime-Änderungen ohne Neustart)
            LoadConfig();

            var instances = _config.GetInstances();
            foreach (var inst in instances)
            {
                if (_onlyWhenRunning && !_proc.IsRunning(inst.Name))
                    continue;

                var logPath = ResolveLogPath(inst);
                if (logPath is null)
                {
                    if (_proc.IsRunning(inst.Name))
                        _log.Warn($"[Monitoring] Logfile nicht gefunden für '{inst.Name}'. Erwartet: {inst.LogFileName}");
                    continue;
                }

                var age = DateTime.Now - File.GetLastWriteTime(logPath);
                if (age.TotalSeconds >= _freezeThresholdSeconds)
                {
                    // Debounce pro Instanz
                    if (_lastAlert.TryGetValue(inst.Name, out var last) &&
                        (DateTime.Now - last).TotalSeconds < _debounceSeconds)
                        continue;

                    _lastAlert[inst.Name] = DateTime.Now;

                    _log.Warn($"[Monitoring] Freeze-Verdacht: {inst.Name} (Log-age {age.TotalSeconds:n0}s) -> {logPath}");
                    _bus.Publish(new InstanceFrozenEvent(inst.Name, age));
                    _bus.Publish(new InstanceNeedsRestartEvent(inst.Name, $"Logfile stale {age.TotalSeconds:n0}s"));
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"[Monitoring] Tick-Fehler: {ex.Message}");
        }
    }

    private void LoadConfig(bool initial = false)
    {
        var m = _config.GetManagerConfig();
        var newInterval = Math.Max(5, m.CheckIntervalSeconds);
        var newFreezeThreshold = Math.Max(30, m.FreezeThresholdSeconds);
        var newDebounce = Math.Max(30, m.MonitorDebounceSeconds);
        var newOnlyWhenRunning = m.MonitorOnlyWhenRunning;

        // bei Änderungen kurz informieren
        if (!initial && (newInterval != _intervalSeconds || newFreezeThreshold != _freezeThresholdSeconds ||
                         newDebounce != _debounceSeconds || newOnlyWhenRunning != _onlyWhenRunning))
        {
            _log.Info($"[Monitoring] Config geändert → Interval={newInterval}s, Freeze={newFreezeThreshold}s, Debounce={newDebounce}s, OnlyWhenRunning={newOnlyWhenRunning}");
            // Timer-Intervall ggf. anpassen
            _timer?.Change(TimeSpan.Zero, TimeSpan.FromSeconds(newInterval));
        }

        _intervalSeconds = newInterval;
        _freezeThresholdSeconds = newFreezeThreshold;
        _debounceSeconds = newDebounce;
        _onlyWhenRunning = newOnlyWhenRunning;
    }

    private static string? ResolveLogPath(InstanceInfo inst)
    {
        if (string.IsNullOrWhiteSpace(inst.LogFileName))
            return null;

        if (Path.IsPathRooted(inst.LogFileName))
            return File.Exists(inst.LogFileName) ? inst.LogFileName : null;

        if (!string.IsNullOrWhiteSpace(inst.ProfilesPath))
        {
            var p = Path.Combine(inst.ProfilesPath, inst.LogFileName);
            if (File.Exists(p)) return p;
        }
        if (!string.IsNullOrWhiteSpace(inst.ServerRoot))
        {
            var p = Path.Combine(inst.ServerRoot, inst.LogFileName);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public void Dispose() => Stop();
}
