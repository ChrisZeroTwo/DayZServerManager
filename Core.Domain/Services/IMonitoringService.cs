// Path: Core/Domain/Services/IMonitoringService.cs
// File: IMonitoringService.cs
// Purpose: Start/Stop des Log-Monitorings für alle Instanzen.

namespace Core.Domain.Services;

public interface IMonitoringService
{
    /// <summary>Startet das Monitoring (periodische Prüfungen).</summary>
    void Start();

    /// <summary>Beendet das Monitoring und gibt Ressourcen frei.</summary>
    void Stop();

    /// <summary>True, wenn der Hintergrundtimer aktiv ist.</summary>
    bool IsRunning { get; }
}
