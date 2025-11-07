// Path: Modules/RestartOrchestrator/RestartOrchestrator.cs
// File: RestartOrchestrator.cs
// Purpose: Steuert geordnete Neustarts: Countdown → Lock → Broadcast → KickAll → Shutdown → Stop → optional Start.

using System.Collections.Concurrent;
using Core.Domain;
using Core.Domain.Events;
using Core.Domain.Services;
using Core.Logging;

namespace Modules.RestartOrchestrator;

public class RestartOrchestrator : IRestartOrchestrator, IDisposable
{
    private readonly IEventBus _bus;
    private readonly ILogService _log;
    private readonly IConfigService _config;
    private readonly IRconService _rcon;
    private readonly IProcessController _proc;

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public RestartOrchestrator(IEventBus bus, ILogService log, IConfigService config, IRconService rcon, IProcessController proc)
    {
        _bus = bus;
        _log = log;
        _config = config;
        _rcon = rcon;
        _proc = proc;

        // Auf Events hören: NeedsRestart → standardmäßiger Restart-Plan
        _bus.Subscribe<InstanceNeedsRestartEvent>(OnNeedsRestart);
    }

    public bool ScheduleRestart(string instanceName, int visibleSeconds = 60, int totalSeconds = 60, string reason = "Scheduled restart", bool autoStartAfter = true)
    {
        var inst = _config.GetInstances().FirstOrDefault(i => string.Equals(i.Name, instanceName, StringComparison.OrdinalIgnoreCase));
        if (inst is null)
        {
            _log.Warn($"[RestartOrch] Unbekannte Instanz '{instanceName}'.");
            return false;
        }

        lock (_gate)
        {
            // laufenden Plan abbrechen und neu starten
            if (_running.TryGetValue(instanceName, out var oldCts))
            {
                oldCts.Cancel();
                _running.TryRemove(instanceName, out _);
            }

            var cts = new CancellationTokenSource();
            _running[instanceName] = cts;

            _ = Task.Run(() => RunPlanAsync(inst.Name, visibleSeconds, totalSeconds, reason, autoStartAfter, cts.Token), cts.Token);
            _log.Info($"[RestartOrch] Restart geplant für '{inst.Name}' in {visibleSeconds}s (Grund: {reason}).");
            _bus.Publish(new DiscordNotifyEvent("Restart Scheduled", $"[{inst.Name}] in {visibleSeconds}s – {reason}", "info"));
            return true;
        }
    }

    public bool CancelRestart(string instanceName)
    {
        lock (_gate)
        {
            if (_running.TryGetValue(instanceName, out var cts))
            {
                cts.Cancel();
                _running.TryRemove(instanceName, out _);
                _log.Info($"[RestartOrch] Restart für '{instanceName}' abgebrochen.");
                _bus.Publish(new DiscordNotifyEvent("Restart Canceled", $"[{instanceName}] Abgebrochen.", "warn"));
                return true;
            }
            _log.Warn($"[RestartOrch] Kein geplanter Restart für '{instanceName}'.");
            return false;
        }
    }

    public bool IsScheduled(string instanceName) => _running.ContainsKey(instanceName);

    private async Task RunPlanAsync(string name, int visible, int total, string reason, bool autoStartAfter, CancellationToken ct)
    {
        try
        {
            // Sichtbarer Countdown (Broadcast Ticks)
            var visibleTicks = BuildVisibleTicks(visible);
            var start = DateTime.UtcNow;
            var end = start.AddSeconds(total <= 0 ? visible : total);
            foreach (var tick in visibleTicks)
            {
                var remaining = (int)(end - DateTime.UtcNow).TotalSeconds;
                if (remaining <= 0) break;
                if (ct.IsCancellationRequested) return;

                // Annäherung: nur senden, wenn wir über dem Tick sind
                if (remaining <= tick)
                {
                    var msg = $"Server restart in {remaining}s ({reason})";
                    _bus.Publish(new InstanceUpdateScheduledEvent(name, remaining, (int)(end - start).TotalSeconds));
                    await _rcon.BroadcastAsync(name, msg, ct);
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                }
                else
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
                }
            }

            if (ct.IsCancellationRequested) return;

            // Lock → KickAll → Shutdown
            await _rcon.LockAsync(name, ct);
            await _rcon.BroadcastAsync(name, $"Server restarting now: {reason}", ct);
            await _rcon.KickAllAsync(name, $"Restart: {reason}", ct);
            await _rcon.ShutdownGracefulAsync(name, reason, ct);

            // Stop absichern (falls Prozess noch läuft)
            if (_proc.IsRunning(name))
            {
                _log.Info($"[RestartOrch] Stop sichergestellt für '{name}'.");
                _proc.Stop(name, kill: false);
                // kurzer Fallback-Timeout
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                if (_proc.IsRunning(name))
                    _proc.Stop(name, kill: true);
            }

            _bus.Publish(new DiscordNotifyEvent("Restart Completed", $"[{name}] heruntergefahren.", "info"));

            // Optional Auto-Start
            if (autoStartAfter)
            {
                var ok = _proc.Start(name);
                _bus.Publish(new DiscordNotifyEvent("Instance Start", $"[{name}] AutoStart {(ok ? "OK" : "fehlgeschlagen")}.", ok ? "info" : "error"));
            }
        }
        catch (TaskCanceledException)
        {
            // Ignorieren (Cancel)
        }
        catch (Exception ex)
        {
            _log.Error($"[RestartOrch] Fehler im Plan für '{name}'.", ex);
            _bus.Publish(new DiscordNotifyEvent("Restart Error", $"[{name}] {ex.Message}", "error"));
        }
        finally
        {
            _running.TryRemove(name, out _);
        }
    }

    private static int[] BuildVisibleTicks(int visibleSeconds)
    {
        // Typische Ticks: 60,30,15,10,9,...,1 – gefiltert nach sichtbarem Fenster
        var baseTicks = new List<int> { 60, 30, 15, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 };
        return baseTicks.Where(t => t <= visibleSeconds && t > 0).ToArray();
    }

    private void OnNeedsRestart(InstanceNeedsRestartEvent e)
    {
        // Standard: 60s sichtbar; total = 60s
        ScheduleRestart(e.InstanceName, visibleSeconds: 60, totalSeconds: 60, reason: e.Reason, autoStartAfter: true);
    }

    public void Dispose()
    {
        foreach (var kv in _running)
            kv.Value.Cancel();
        _running.Clear();
    }
}
