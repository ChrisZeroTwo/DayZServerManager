// Path: Core/Domain/Services/IProvisioningService.cs
// File: IProvisioningService.cs
// Purpose: Schnittstelle für Server-/Mod-Provisionierung inkl. Auto-Installation von SteamCMD.

using System.Threading.Tasks;

namespace Core.Domain.Services;

public interface IProvisioningService
{
    /// <summary>Stellt sicher, dass steamcmd verfügbar ist (Download+Entpacken+Self-Update falls nötig).</summary>
    Task<string?> EnsureSteamCmdAsync();

    /// <summary>Installiert/aktualisiert den DayZ-Server für die angegebene Instanz (app_update 223350).</summary>
    Task<bool> EnsureServerUpToDateAsync(string instanceName);

    /// <summary>Stellt sicher, dass die Instanz-Verzeichnisstruktur existiert (server root, profiles, cfg).</summary>
    Task<bool> EnsureInstanceStructureAsync(string instanceName);

    /// <summary>Lädt alle im Instanz-Config definierten Mods in den Modcache (workshop_download_item 221100 …).</summary>
    Task<int> DownloadModsAsync(string instanceName);

    /// <summary>Installiert Mods aus dem Cache in die Instanz (Kopie/Junction).</summary>
    Task<int> InstallModsToInstanceAsync(string instanceName, bool preferJunction = false);

    /// <summary>Kombi: Server + Struktur + Mods (Download + Install) in einem Rutsch.</summary>
    Task<bool> ProvisionAllAsync(string instanceName, bool preferJunction = false);
}
