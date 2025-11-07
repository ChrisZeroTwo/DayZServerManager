// Path: Core/Domain/Services/IRestartOrchestrator.cs
// File: IRestartOrchestrator.cs
// Purpose: API für geplante Neustarts (Schedule/Cancel) pro Instanz und Orchestrierung von Countdown & Aktionen.

namespace Core.Domain.Services;

public interface IRestartOrchestrator
{
    /// <summary>Plan einen Neustart für eine Instanz. Sichtbarer Countdown in Sekunden; totalSeconds = gesamte Ablaufzeit.</summary>
    bool ScheduleRestart(string instanceName, int visibleSeconds = 60, int totalSeconds = 60, string reason = "Scheduled restart", bool autoStartAfter = true);

    /// <summary>Hebt einen geplanten Neustart auf.</summary>
    bool CancelRestart(string instanceName);

    /// <summary>Gibt true, wenn für Instanz ein Restart geplant/aktiv ist.</summary>
    bool IsScheduled(string instanceName);
}
