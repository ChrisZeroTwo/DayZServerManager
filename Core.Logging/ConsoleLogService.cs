// Path: Core/Logging/ConsoleLogService.cs
// File: ConsoleLogService.cs
// Purpose: Einfache Console-Logger-Implementierung als Startpunkt.

namespace Core.Logging;

public class ConsoleLogService : ILogService
{
    public void Info(string message) => Console.WriteLine($"[INFO ] {message}");
    public void Warn(string message) => Console.WriteLine($"[WARN ] {message}");
    public void Error(string message, Exception? ex = null)
        => Console.WriteLine($"[ERROR] {message}{(ex is null ? "" : " :: " + ex.Message)}");
}
