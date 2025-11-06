// Path: Core/Common/FileUtils.cs
// File: FileUtils.cs
// Purpose: Datei-/Verzeichnis-Utilities (Existenz, Alter).

namespace Core.Common;

public static class FileUtils
{
    public static void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    public static bool FileIsOlderThan(string path, int seconds)
    {
        if (!File.Exists(path)) return true;
        var age = DateTime.Now - File.GetLastWriteTime(path);
        return age.TotalSeconds > seconds;
    }
}
