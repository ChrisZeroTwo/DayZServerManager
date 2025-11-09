// Path: Modules/RestartOrchestrator/RestartOrchestrator.cs
// File: RestartOrchestrator.cs
// Purpose: Orchestrierter Neustart mit Vorwarnung, Lock, Kick, Stop, Start.
//          Interface-konform:
//            - bool ScheduleRestart(string instance, int secondsA, int secondsB, string reason, bool notify)
//            - bool CancelRestart(string instance)
//            - bool IsScheduled(string instance)

using Core.Domain.Services;
using Core.Logging;

namespace Modules.RestartOrchestrator;

public class RestartOrchestrator : IRestartOrchestrator
{
    private readonly ILogService _log;
    private readonly IRconService _rcon;
    private readonly IProcessController _proc;

    private readonly Dictionary<string, CancellationTokenSource> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    public RestartOrchestrator(ILogService log, IRconService rcon, IProcessController proc)
    {
        _log = log;
        _rcon = rcon;
        _proc = proc;
    }

    public bool IsScheduled(string instanceName) => _pending.ContainsKey(instanceName);

    public bool CancelRestart(string instanceName)
    {
        if (_pending.Remove(instanceName, out var cts))
        {
            try { cts.Cancel(); } catch { /* ignore */ }
            _log.Info($"[Restart] Geplanter Neustart für '{instanceName}' abgebrochen.");
            return true;
        }
        return false;
    }

    // Interface: bool ScheduleRestart(string,int,int,string,bool)
    // secondsA = Vorwarnzeit; secondsB = Wartezeit nach Kick bis Stop.
    public bool ScheduleRestart(string instanceName, int secondsA, int secondsB, string reason, bool notify)
    {
        try
        {
            // vorhandenen Plan ersetzen
            CancelRestart(instanceName);
            var cts = new CancellationTokenSource();
            _pending[instanceName] = cts;

            _ = Task.Run(() => RunRestartAsync(instanceName, secondsA, secondsB, reason, notify, cts.Token));
            _log.Info($"[Restart] Neustart geplant in {secondsA}s für '{instanceName}' ({reason}).");
            return true;
        }
        catch (Exception ex)
        {
            _log.Warn($"[Restart] Planen fehlgeschlagen für '{instanceName}': {ex.Message}");
            return false;
        }
    }

    private async Task RunRestartAsync(string instance, int warnSeconds, int postKickSeconds, string reason, bool notify, CancellationToken ct)
    {
        try
        {
            // Entscheidend: nur RCON/Lock/Kick/Stop, wenn die Instanz aktuell läuft.
            var wasRunning = IsRunningSafe(instance);

            if (wasRunning)
            {
                if (notify) await SafeSay(instance, $"- Server wird in {warnSeconds}s neu gestartet: {reason} -");
                await SafeLock(instance, true);
                await DelaySafe(TimeSpan.FromSeconds(warnSeconds), ct);

                if (notify) await SafeSay(instance, "- KickAll wegen Update -");
                SafeKickAll(instance);
                await DelaySafe(TimeSpan.FromSeconds(postKickSeconds), ct);

                if (notify) await SafeSay(instance, "- Server stoppt für Update -");
                SafeStop(instance);
                await DelaySafe(TimeSpan.FromSeconds(60), ct); // fixer Puffer für Stop
            }
            else
            {
                _log.Info($"[Restart] Instanz '{instance}' läuft nicht – überspringe RCON/Lock/Kick/Stop.");
            }

            // In beiden Fällen am Ende (Re-)Start ausführen:
            SafeStart(instance);
            await DelaySafe(TimeSpan.FromSeconds(3), ct);

            // Nach dem Start ggf. entsperren + Abschluss-Nachricht, aber nur wenn jetzt wirklich läuft
            if (IsRunningSafe(instance))
            {
                await SafeLock(instance, false);
                if (notify) await SafeSay(instance, "- Server-Update abgeschlossen -");
            }

            _log.Info($"[Restart] '{instance}' erfolgreich neu gestartet.");
        }
        catch (OperationCanceledException)
        {
            _log.Info($"[Restart] Ablauf für '{instance}' abgebrochen.");
        }
        catch (Exception ex)
        {
            _log.Warn($"[Restart] '{instance}': Fehler: {ex.Message}");
        }
        finally
        {
            _pending.Remove(instance);
        }
    }

    // ---- helpers ----

    // Prüft sicher, ob Instanz läuft (falls Controller kein IsRunning hat, über Reflection fallbacken)
    private bool IsRunningSafe(string instance)
    {
        try
        {
            // Bevorzugt direktes Interface
            if (_proc is not null)
            {
                try
                {
                    // Viele Implementierungen haben IsRunning(string)
                    var mi = _proc.GetType().GetMethod("IsRunning", new[] { typeof(string) });
                    if (mi != null)
                    {
                        var res = mi.Invoke(_proc, new object[] { instance });
                        if (res is bool b) return b;
                    }
                }
                catch { /* ignore */ }
            }
        }
        catch { /* ignore */ }
        return false;
    }

    private async Task SafeSay(string instance, string msg)
    { try { await _rcon.BroadcastAsync(instance, msg); } catch { } }

    private async Task SafeLock(string instance, bool locked)
    { try { if (locked) await _rcon.LockAsync(instance); else await _rcon.UnlockAsync(instance); } catch { } }

    private void SafeKickAll(string instance)
    { try { _rcon.KickAllAsync(instance, "Server-Update").GetAwaiter().GetResult(); } catch { } }

    private void SafeStart(string instance)
    {
        if (TryCall(_proc, "StartInstance", instance)) return;
        if (TryCall(_proc, "Start", instance)) return;
    }

    private void SafeStop(string instance)
    {
        if (TryCall(_proc, "StopInstance", instance)) return;
        if (TryCall(_proc, "Stop", instance)) return;
    }

    private static bool TryCall(object target, string method, string name)
    {
        try
        {
            var mi = target.GetType().GetMethod(method, new[] { typeof(string) });
            if (mi == null) return false;
            mi.Invoke(target, new object[] { name });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task DelaySafe(TimeSpan t, CancellationToken ct)
    {
        try { await Task.Delay(t, ct); } catch (TaskCanceledException) { }
    }
}
