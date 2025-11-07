// Path: Core/Logging/FileLogService.cs
// File: FileLogService.cs
// Purpose: Schreibt Logs in eine Datei (append). Thread-sicher, einfacher Roll-Over bei 5 MB.

namespace Core.Logging;

public class FileLogService : ILogService, IDisposable
{
    private readonly string _path;
    private readonly object _gate = new();

    public FileLogService(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(_path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
    }

    public void Info(string message) => Write("INFO ", message);
    public void Warn(string message) => Write("WARN ", message);
    public void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : $"{message} :: {ex}");

    private void Write(string level, string message)
    {
        lock (_gate)
        {
            try
            {
                // einfacher Roll-Over ab ~5 MB
                if (File.Exists(_path) && new FileInfo(_path).Length > 5 * 1024 * 1024)
                {
                    var bak = _path + "." + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log";
                    File.Move(_path, bak, overwrite: false);
                }
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
            catch { /* Logging soll nie werfen */ }
        }
    }

    public void Dispose() { }
}
