// Path: Modules/ConfigService/ConfigService.cs
// File: ConfigService.cs
// Purpose: Implementiert IConfigService. Lädt manager.json und alle server.json im instancesPath, stellt DTOs read-only bereit.

using System.Text.Json;
using Core.Common;
using Core.Domain.DTOs;
using Core.Domain.Services;
using Core.Logging;

namespace Modules.ConfigService;

public class ConfigService : IConfigService
{
    private readonly ILogService _log;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    private string _managerConfigPath = string.Empty;
    private ManagerConfig _manager = new();
    private List<InstanceInfo> _instances = new();

    public ConfigService(ILogService log) => _log = log;

    public void Initialize(string managerConfigPath)
    {
        _managerConfigPath = managerConfigPath;
        Reload();
    }

    public void Reload()
    {
        if (string.IsNullOrWhiteSpace(_managerConfigPath))
            throw new InvalidOperationException("ConfigService: Initialize(managerConfigPath) muss zuerst aufgerufen werden.");

        _log.Info($"[ConfigService] Lade Manager-Config: {_managerConfigPath}");
        using (var fs = File.OpenRead(_managerConfigPath))
            _manager = JsonSerializer.Deserialize<ManagerConfig>(fs, _json) ?? new ManagerConfig();

        if (string.IsNullOrWhiteSpace(_manager.InstancesPath))
            throw new InvalidOperationException("manager.json enthält kein 'instancesPath'.");

        // Instanzdateien einlesen
        _instances = new List<InstanceInfo>();
        FileUtils.EnsureDirectory(_manager.InstancesPath);

        var files = Directory.GetFiles(_manager.InstancesPath, "*.json", SearchOption.TopDirectoryOnly);
        foreach (var file in files)
        {
            try
            {
                using var ifs = File.OpenRead(file);
                var instance = JsonSerializer.Deserialize<InstanceInfo>(ifs, _json);
                if (instance is null || string.IsNullOrWhiteSpace(instance.Name))
                {
                    _log.Warn($"[ConfigService] Überspringe ungültige server.json: {file}");
                    continue;
                }
                _instances.Add(instance);
            }
            catch (Exception ex)
            {
                _log.Warn($"[ConfigService] Konnte Instanzdatei nicht lesen: {file} :: {ex.Message}");
            }
        }

        _log.Info($"[ConfigService] Geladen: Instances={_instances.Count}, ModCache={_manager.ModCachePath}");
    }

    public ManagerConfig GetManagerConfig() => _manager;

    public IReadOnlyList<InstanceInfo> GetInstances() => _instances;
}
