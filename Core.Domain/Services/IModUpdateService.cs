// Path: Core/Domain/Services/IModUpdateService.cs
// File: IModUpdateService.cs
// Purpose: Prüft und aktualisiert Workshop-Mods; periodische Checks und manuelle Updates.

namespace Core.Domain.Services;

public interface IModUpdateService
{
    /// <summary>Startet den periodischen Update-Check gemäß manager.json (modCheckIntervalSeconds).</summary>
    void Start();

    /// <summary>Stoppt den periodischen Update-Check.</summary>
    void Stop();

    /// <summary>Manueller Sofort-Check (prüft alle Instanzen/Mods und meldet verfügbares Update).</summary>
    Task CheckNowAsync(CancellationToken ct = default);

    /// <summary>Aktualisiert alle Mods einer Instanz mit steamcmd. Liefert true, wenn mind. ein Mod aktualisiert wurde.</summary>
    Task<bool> UpdateInstanceModsAsync(string instanceName, CancellationToken ct = default);

    /// <summary>True, wenn der Hintergrundtimer läuft.</summary>
    bool IsRunning { get; }
}
