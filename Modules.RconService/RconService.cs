// Path: Modules/RconService/RconService.cs
// File: RconService.cs
// Purpose: Stub-RCON-Implementierung: löst Instanzdaten auf, "sendet" Kommandos (simuliert), loggt + publiziert Events.

using Core.Domain;
using Core.Domain.Events;
using Core.Domain.Services;
using Core.Logging;

namespace Modules.RconService;

public class RconService : IRconService
{
    private readonly IEventBus _bus;
    private readonly ILogService _log;
    private readonly IConfigService _config;

    // Simulation/DryRun: keine echte Netzwerkkommunikation (Austauschbar gegen realen Transport)
    private readonly bool _dryRun;

    public RconService(IEventBus bus, ILogService log, IConfigService config, bool dryRun = true)
    {
        _bus = bus;
        _log = log;
        _config = config;
        _dryRun = dryRun;
    }

    public async Task<bool> SendRawAsync(string instanceName, string command, CancellationToken ct = default)
    {
        var r = Resolve(instanceName);
        if (r is null) return false;

        _log.Info($"[RCON] ({instanceName}@{r.Host}:{r.Port}) -> {command}");
        _bus.Publish(new DiscordNotifyEvent("RCON", $"[{instanceName}] {command}", "info"));

        if (_dryRun)
        {
            await Task.Delay(20, ct);
            return true;
        }

        // Platzhalter für echte RCON-Transport-Schicht
        // await _transport.SendAsync(r.Host, r.Port, r.Password, command, ct);
        await Task.Delay(20, ct);
        return true;
    }

    public Task<bool> LockAsync(string instanceName, CancellationToken ct = default)
        => SendRawAsync(instanceName, "#lock", ct);

    public Task<bool> UnlockAsync(string instanceName, CancellationToken ct = default)
        => SendRawAsync(instanceName, "#unlock", ct);

    public Task<bool> BroadcastAsync(string instanceName, string message, CancellationToken ct = default)
        => SendRawAsync(instanceName, $"say -1 {Escape(message)}", ct);

    public async Task<bool> KickAllAsync(string instanceName, string reason = "Server maintenance", CancellationToken ct = default)
    {
        // Übliche Strategie: Broadcast → kurze Wartezeit → Kickall
        await BroadcastAsync(instanceName, $"Server restart in progress: {reason}", ct);
        await Task.Delay(1000, ct);
        return await SendRawAsync(instanceName, "#kick all", ct);
    }

    public async Task<bool> ShutdownGracefulAsync(string instanceName, string reason = "Restart", CancellationToken ct = default)
    {
        await BroadcastAsync(instanceName, $"Server will shutdown: {reason}", ct);
        await Task.Delay(1000, ct);
        // DayZ/BattlEye kennt i. d. R. #shutdown (je nach Konfiguration)
        return await SendRawAsync(instanceName, "#shutdown", ct);
    }

    // --- helpers ---

    private Core.Domain.DTOs.RconConfig? Resolve(string instanceName)
    {
        var inst = _config.GetInstances().FirstOrDefault(i => string.Equals(i.Name, instanceName, StringComparison.OrdinalIgnoreCase));
        if (inst is null)
        {
            _log.Warn($"[RCON] Instanz unbekannt: {instanceName}");
            return null;
        }

        var r = inst.Rcon;
        if (string.IsNullOrWhiteSpace(r.Host) || r.Port <= 0 || string.IsNullOrWhiteSpace(r.Password))
        {
            _log.Warn($"[RCON] RCON unvollständig konfiguriert für '{instanceName}'.");
            return null;
        }
        return r;
    }

    private static string Escape(string s)
        => s.Replace("\r", " ").Replace("\n", " ").Trim();
}
