// Path: Modules/ProvisioningService/ProvisioningService.cs
// File: ProvisioningService.cs
// Purpose: Provisionierung mit kopierbasiertem Mod-Deployment (ohne Junction).
//          Download/Update in Cache bei laufenden Servern, danach Overlay-Kopie in die Instanz.
//          Wenn Instanz läuft und Dateien aktualisiert wurden -> InstanceNeedsRestartEvent.

using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using Core.Domain;
using Core.Domain.DTOs;
using Core.Domain.Events;
using Core.Domain.Services;
using Core.Logging;

namespace Modules.ProvisioningService;

public class ProvisioningService : IProvisioningService
{
    private readonly IConfigService _config;
    private readonly IInstanceRegistry _registry;
    private readonly ILogService _log;
    private readonly IProcessController _proc;
    private readonly IEventBus _bus;

    private static readonly HttpClient _http = new();

    private const string SteamCmdZipUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
    private const string AppIdDayZServer = "223350";
    private const string AppIdDayZWorkshop = "221100";

    public ProvisioningService(
        IConfigService config,
        IInstanceRegistry registry,
        ILogService log,
        IProcessController proc,
        IEventBus bus)
    {
        _config = config;
        _registry = registry;
        _log = log;
        _proc = proc;
        _bus = bus;
    }

    // ----------------- Public API -----------------

    public async Task<bool> ProvisionAllAsync(string instanceName, bool preferJunction /*ignored*/)
    {
        var ok = await EnsureServerUpToDateAsync(instanceName);
        if (!ok) return false;

        ok = await EnsureInstanceStructureAsync(instanceName);
        if (!ok) return false;

        await DownloadModsAsync(instanceName);
        await InstallModsToInstanceAsync(instanceName, preferJunction: false);
        return true;
    }

    public async Task<string?> EnsureSteamCmdAsync()
    {
        var configured = _config.GetManagerConfig().SteamCmdPath?.Trim();
        string targetExe;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (Directory.Exists(configured))
                targetExe = Path.Combine(configured, "steamcmd.exe");
            else if (configured.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                targetExe = configured;
            else
                targetExe = Path.Combine(configured, "steamcmd.exe");
        }
        else
        {
            var tools = Path.Combine(AppContext.BaseDirectory, "tools", "steamcmd");
            targetExe = Path.Combine(tools, "steamcmd.exe");
        }

        if (File.Exists(targetExe))
            return targetExe;

        try
        {
            var targetDir = Path.GetDirectoryName(targetExe)!;
            Directory.CreateDirectory(targetDir);

            var tmpZip = Path.Combine(Path.GetTempPath(), $"steamcmd_{Guid.NewGuid():N}.zip");
            _log.Info($"[Provision] SteamCMD fehlt – lade herunter: {SteamCmdZipUrl}");
            using (var resp = await _http.GetAsync(SteamCmdZipUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(tmpZip);
                await resp.Content.CopyToAsync(fs);
            }

            _log.Info($"[Provision] Entpacke SteamCMD nach: {targetDir}");
            ZipFile.ExtractToDirectory(tmpZip, targetDir, overwriteFiles: true);
            File.Delete(tmpZip);

            var (rc, so, se) = await RunSteamCmdAsync(targetExe, "+quit", TimeSpan.FromMinutes(5));
            if (rc != 0)
                _log.Warn($"[Provision] SteamCMD Self-Update ExitCode={rc}. Details:\n{Tail(so, se)}");

            if (!File.Exists(targetExe))
            {
                _log.Warn($"[Provision] steamcmd.exe nach Entpacken nicht gefunden: {targetExe}");
                return null;
            }

            _log.Info($"[Provision] SteamCMD bereit: {targetExe}");
            return targetExe;
        }
        catch (Exception ex)
        {
            _log.Warn($"[Provision] SteamCMD-Installation fehlgeschlagen: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> EnsureServerUpToDateAsync(string instanceName)
    {
        var inst = _registry.GetByName(instanceName);
        if (inst is null)
        {
            _log.Warn($"[Provision] Unbekannte Instanz '{instanceName}'.");
            return false;
        }

        var serverDir = inst.ServerRoot;
        if (string.IsNullOrWhiteSpace(serverDir))
        {
            _log.Warn($"[Provision] ServerRoot fehlt in Instance '{instanceName}'.");
            return false;
        }
        Directory.CreateDirectory(serverDir);

        var steamExe = await EnsureSteamCmdAsync();
        if (string.IsNullOrWhiteSpace(steamExe) || !File.Exists(steamExe))
        {
            _log.Warn("[Provision] SteamCMD konnte nicht bereitgestellt werden.");
            return false;
        }

        var login = BuildLoginArgsFromConfigOrEnv();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            // Wichtig: force_install_dir vor login
            var args = $"+force_install_dir \"{serverDir}\" {login} +app_update {AppIdDayZServer} validate +quit";
            _log.Info($"[Provision] Server-Update prüfen … Versuch {attempt}/3 (app_update {AppIdDayZServer})");
            var (rc, stdout, stderr) = await RunSteamCmdAsync(steamExe, args, TimeSpan.FromMinutes(30));
            if (rc == 0)
            {
                _log.Info("[Provision] Server ist installiert/aktualisiert.");
                return true;
            }

            var hint = ClassifySteamError(stdout, stderr);
            _log.Warn($"[Provision] app_update fehlgeschlagen (rc={rc}). {hint}\n{Tail(stdout, stderr)}");

            if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(10 * attempt));
            else return false;
        }

        return false;
    }

    public Task<bool> EnsureInstanceStructureAsync(string instanceName)
    {
        var inst = _registry.GetByName(instanceName);
        if (inst is null)
        {
            _log.Warn($"[Provision] Unbekannte Instanz '{instanceName}'.");
            return Task.FromResult(false);
        }

        var root = inst.ServerRoot;
        var profiles = inst.ProfilesPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            _log.Warn($"[Provision] ServerRoot fehlt in Instance '{instanceName}'.");
            return Task.FromResult(false);
        }

        Directory.CreateDirectory(root);
        if (!string.IsNullOrWhiteSpace(profiles))
            Directory.CreateDirectory(profiles);

        var cfgPath = Path.Combine(root, "config", "serverDZ.cfg");
        Directory.CreateDirectory(Path.GetDirectoryName(cfgPath)!);
        if (!File.Exists(cfgPath))
        {
            File.WriteAllText(cfgPath, """
hostName = "My DayZ Server";
password = "";
passwordAdmin = "admin";
maxPlayers = 60;
verifySignatures = 2;
forceSameBuild = 1;
""");
            _log.Info($"[Provision] Basis-Config erzeugt: {cfgPath}");
        }

        return Task.FromResult(true);
    }

    public async Task<int> DownloadModsAsync(string instanceName)
    {
        var inst = _registry.GetByName(instanceName);
        if (inst is null)
        {
            _log.Warn($"[Provision] Unbekannte Instanz '{instanceName}'.");
            return 0;
        }

        var mods = inst.Mods ?? new List<ModInfo>();
        if (mods.Count == 0)
        {
            _log.Info($"[Provision] Keine Mods in Instanz '{instanceName}' definiert.");
            return 0;
        }

        var steamExe = await EnsureSteamCmdAsync();
        if (string.IsNullOrWhiteSpace(steamExe) || !File.Exists(steamExe))
        {
            _log.Warn("[Provision] SteamCMD konnte nicht bereitgestellt werden.");
            return 0;
        }

        var modCacheRoot = GetNormalizedModCacheRoot(_config.GetManagerConfig().ModCachePath);
        if (string.IsNullOrWhiteSpace(modCacheRoot))
        {
            _log.Warn("[Provision] modCachePath fehlt in manager.json.");
            return 0;
        }
        Directory.CreateDirectory(modCacheRoot);

        var steamWorkshopRoot = GetSteamWorkshopRoot(steamExe);
        var login = BuildLoginArgsFromConfigOrEnv();

        var success = 0;
        foreach (var mod in mods)
        {
            long id = mod.WorkshopId;
            if (id <= 0) continue;

            var args = $"{login} +workshop_download_item {AppIdDayZWorkshop} {id} validate +quit";
            _log.Info($"[Provision] Lade/prüfe Mod {id} …");
            var (rc, so, se) = await RunSteamCmdAsync(steamExe, args, TimeSpan.FromMinutes(20));
            if (rc == 0)
            {
                // Von Steam-Workshop in unseren Cache spiegeln (Server darf laufen)
                var src = Path.Combine(steamWorkshopRoot, id.ToString());
                var dst = Path.Combine(modCacheRoot, id.ToString());
                if (Directory.Exists(src))
                {
                    MirrorDirectory(src, dst);
                    _log.Info($"[Provision] Mod {id} im Cache bereit → {dst}");
                }
                else
                {
                    _log.Warn($"[Provision] Workshop-Quelle nicht gefunden für {id}: {src}");
                }
                success++;
            }
            else
            {
                var hint = ClassifySteamError(so, se);
                _log.Warn($"[Provision] Mod {id} Download-Fehler (rc={rc}). {hint}\n{Tail(so, se)}");
            }
        }

        return success;
    }

    public Task<int> InstallModsToInstanceAsync(string instanceName, bool preferJunction /*ignored*/)
    {
        var inst = _registry.GetByName(instanceName);
        if (inst is null)
        {
            _log.Warn($"[Provision] Unbekannte Instanz '{instanceName}'.");
            return Task.FromResult(0);
        }

        var mods = inst.Mods ?? new List<ModInfo>();
        if (mods.Count == 0)
        {
            _log.Info($"[Provision] Keine Mods in Instanz '{instanceName}' definiert.");
            return Task.FromResult(0);
        }

        var modCacheRoot = GetNormalizedModCacheRoot(_config.GetManagerConfig().ModCachePath);
        if (string.IsNullOrWhiteSpace(modCacheRoot))
        {
            _log.Warn("[Provision] modCachePath fehlt in manager.json.");
            return Task.FromResult(0);
        }

        var running = _proc.IsRunning(instanceName);
        var anyUpdated = false;
        var installed = 0;

        foreach (var mod in mods)
        {
            long id = mod.WorkshopId;
            if (id <= 0) continue;

            var cacheDir = Path.Combine(modCacheRoot, id.ToString());
            if (!Directory.Exists(cacheDir))
            {
                _log.Warn($"[Provision] Modcache fehlt für {id}: {cacheDir}");
                continue;
            }

            var targetName = !string.IsNullOrWhiteSpace(mod.Name) ? $"@{mod.Name}" : $"@{id}";
            var targetDir = Path.Combine(inst.ServerRoot, targetName);
            Directory.CreateDirectory(targetDir);

            // Overlay-Kopie: vorhandene Dateien bleiben, aktualisierte werden überschrieben
            var (copied, overwritten, failed) = OverlayCopy(cacheDir, targetDir);
            if (copied > 0 || overwritten > 0)
            {
                _log.Info($"[Provision] Mod installiert/aktualisiert: {targetName} (neu: {copied}, ersetzt: {overwritten}, fehlgeschlagen: {failed})");
                installed++;
                if (overwritten > 0) anyUpdated = true;
            }
        }

        if (running && anyUpdated)
        {
            var reason = "Mods aktualisiert – Neustart empfohlen, um neue Dateien zu laden.";
            _bus.Publish(new InstanceNeedsRestartEvent(instanceName, reason));
            _log.Info($"[Provision] Instanz '{instanceName}' läuft – {reason}");
        }

        return Task.FromResult(installed);
    }

    // ----------------- Helpers -----------------

    private string BuildLoginArgsFromConfigOrEnv()
    {
        var m = _config.GetManagerConfig();
        var u = m.Steam?.Username;
        var p = m.Steam?.Password;
        var g = m.Steam?.GuardCode;

        if (string.IsNullOrWhiteSpace(u))
            u = Environment.GetEnvironmentVariable("STEAM_USERNAME");
        if (string.IsNullOrWhiteSpace(p))
            p = Environment.GetEnvironmentVariable("STEAM_PASSWORD");
        if (string.IsNullOrWhiteSpace(g))
            g = Environment.GetEnvironmentVariable("STEAM_GUARD");

        if (!string.IsNullOrWhiteSpace(u) && !string.IsNullOrWhiteSpace(p))
        {
            var login = $"+login {Escape(u)} {Escape(p)}";
            if (!string.IsNullOrWhiteSpace(g))
                login += $" +set_steam_guard_code {Escape(g)}";
            _log.Info("[Provision] Steam-Login via Konfiguration aktiv (Username gesetzt).");
            return login;
        }

        _log.Info("[Provision] Nutze anonymous Login (keine Credentials in JSON/Env).");
        return "+login anonymous";
    }

    private static string Escape(string s)
        => s.Contains(' ') ? $"\"{s.Replace("\"", "\\\"")}\"" : s;

    private static string Tail(string stdout, string stderr, int maxLines = 30)
    {
        var sb = new StringBuilder();
        void AppendTail(string label, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var tail = lines.Skip(Math.Max(0, lines.Length - maxLines));
            sb.AppendLine($"--- {label} (tail) ---");
            foreach (var l in tail) sb.AppendLine(l);
        }
        AppendTail("STDOUT", stdout);
        AppendTail("STDERR", stderr);
        return sb.ToString();
    }

    private static string ClassifySteamError(string stdout, string stderr)
    {
        var txt = (stdout + "\n" + stderr).ToLowerInvariant();

        if (txt.Contains("please use force_install_dir before logon"))
            return "Hinweis: Reihenfolge der Argumente – wurde behoben (force_install_dir vor login).";
        if (txt.Contains("no subscription"))
            return "Hinweis: App erfordert lizenzierten Account – mit Steam-Login (nicht anonymous) erneut ausführen.";
        if (txt.Contains("timed out") || txt.Contains("timeout"))
            return "Hinweis: Timeout – Netzwerk / Steam-Backend. Retry läuft automatisch.";
        if (txt.Contains("could not connect") || txt.Contains("servers are busy"))
            return "Hinweis: Keine Verbindung / Steam-Server ausgelastet.";
        if (txt.Contains("disk write failure"))
            return "Hinweis: Schreibfehler – Speicherplatz/Antivirus prüfen.";
        if (txt.Contains("login failure") || txt.Contains("two-factor") || txt.Contains("steam guard"))
            return "Hinweis: Login-Problem – ggf. Guard-Code setzen oder Credentials prüfen.";

        return "Siehe Details unten (StdOut/StdErr).";
    }

    private static async Task<(int rc, string stdout, string stderr)> RunSteamCmdAsync(string steamExe, string arguments, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(steamExe, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(steamExe) ?? Environment.CurrentDirectory
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        var finished = await Task.Run(() => p.WaitForExit((int)timeout.TotalMilliseconds));
        if (!finished)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return (-1, stdout.ToString(), stderr.ToString());
        }

        return (p.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static string GetSteamWorkshopRoot(string steamExe)
        => Path.Combine(Path.GetDirectoryName(steamExe) ?? ".", "steamapps", "workshop", "content", AppIdDayZWorkshop);

    // modCachePath normalisieren: wenn bereits auf \221100 endet → so lassen, sonst anhängen
    private static string GetNormalizedModCacheRoot(string? modCachePath)
    {
        if (string.IsNullOrWhiteSpace(modCachePath)) return string.Empty;
        var p = modCachePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var leaf = Path.GetFileName(p);
        if (string.Equals(leaf, AppIdDayZWorkshop, StringComparison.OrdinalIgnoreCase))
            return p; // schon …\221100
        return Path.Combine(p, AppIdDayZWorkshop); // …\modcache\221100
    }

    // Overlay-Kopie: erstellt/verändert Dateien, löscht standardmäßig nichts
    private static (int created, int overwritten, int failed) OverlayCopy(string src, string dst)
    {
        var created = 0; var overwritten = 0; var failed = 0;
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var target = Path.Combine(dst, rel);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!File.Exists(target)) { File.Copy(file, target, overwrite: false); created++; }
                else { File.Copy(file, target, overwrite: true); overwritten++; }
            }
            catch { failed++; }
        }
        return (created, overwritten, failed);
    }

    private static void MirrorDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        // Keine aggressive Löschung → Locks egal; wir überschreiben nur
        OverlayCopy(sourceDir, destDir);
    }
}
