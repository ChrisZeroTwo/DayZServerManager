// Path: Core/Logging/CompositeLogService.cs
// File: CompositeLogService.cs
// Purpose: Leitet Logging an mehrere ILogService-Instanzen weiter (Console + File).

namespace Core.Logging;

public class CompositeLogService : ILogService
{
    private readonly ILogService[] _targets;
    public CompositeLogService(params ILogService[] targets) => _targets = targets;

    public void Info(string message) { foreach (var t in _targets) t.Info(message); }
    public void Warn(string message) { foreach (var t in _targets) t.Warn(message); }
    public void Error(string message, Exception? ex = null) { foreach (var t in _targets) t.Error(message, ex); }
}
