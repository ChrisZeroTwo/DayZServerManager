// Path: Core/Domain/Services/IInstanceRegistry.cs
// File: IInstanceRegistry.cs
// Purpose: Read-only Zugriff auf Instanzen; Snapshot aus ConfigService.

using Core.Domain.DTOs;

namespace Core.Domain.Services;

public interface IInstanceRegistry
{
    /// <summary>Initialisiert die Registry und lädt die Instanzen aus dem ConfigService.</summary>
    void Initialize();

    /// <summary>Alle bekannten Instanzen (Snapshot).</summary>
    IReadOnlyList<InstanceInfo> GetAll();

    /// <summary>Versucht eine Instanz per Name zu finden; null wenn unbekannt.</summary>
    InstanceInfo? GetByName(string name);

    /// <summary>Gibt true zurück, wenn eine Instanz mit diesem Namen existiert.</summary>
    bool Exists(string name);
}
