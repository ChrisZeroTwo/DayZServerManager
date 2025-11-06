// Path: Core/Logging/ILogService.cs
// File: ILogService.cs
// Purpose: Schlankes Logging-Interface für austauschbare Implementierungen.

namespace Core.Logging;

public interface ILogService
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
}
