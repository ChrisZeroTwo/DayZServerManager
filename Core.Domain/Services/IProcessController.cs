// Path: Core/Domain/Services/IProcessController.cs
// File: IProcessController.cs
// Purpose: Steuerung von DayZ-Instanzen (Start/Stop); löst Lebenszyklus-Events aus.

namespace Core.Domain.Services;

public interface IProcessController
{
    /// <summary>Startet die Instanz (kopiert vorher die Runtime-Config).</summary>
    bool Start(string instanceName);

    /// <summary>Beendet die Instanz sanft; Kill=true erzwingt.</summary>
    bool Stop(string instanceName, bool kill = false);

    /// <summary>Prüft, ob die Instanz als Prozess läuft.</summary>
    bool IsRunning(string instanceName);

    /// <summary>PID der laufenden Instanz, oder null.</summary>
    int? GetPid(string instanceName);
}
