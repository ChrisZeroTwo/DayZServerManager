// Path: Core/Domain/Services/IRconService.cs
// File: IRconService.cs
// Purpose: Öffentliche Schnittstelle für RCON-Befehle (Lock/Unlock/Broadcast/KickAll/Shutdown) pro Instanz.

namespace Core.Domain.Services;

public interface IRconService
{
    /// <summary>Sendet einen beliebigen RCON-Befehl (roh) an die Instanz.</summary>
    Task<bool> SendRawAsync(string instanceName, string command, CancellationToken ct = default);

    /// <summary>Setzt den Server in den Locked-Status (Spieler können nicht joinen).</summary>
    Task<bool> LockAsync(string instanceName, CancellationToken ct = default);

    /// <summary>Hebt Lock auf.</summary>
    Task<bool> UnlockAsync(string instanceName, CancellationToken ct = default);

    /// <summary>Nachricht an alle Spieler (In-Game Nachricht).</summary>
    Task<bool> BroadcastAsync(string instanceName, string message, CancellationToken ct = default);

    /// <summary>Kickt alle Spieler.</summary>
    Task<bool> KickAllAsync(string instanceName, string reason = "Server maintenance", CancellationToken ct = default);

    /// <summary>„Graceful“ Shutdown (nutzt #shutdown-Mechanik oder äquivalent).</summary>
    Task<bool> ShutdownGracefulAsync(string instanceName, string reason = "Restart", CancellationToken ct = default);
}
