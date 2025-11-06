// Path: Modules/InstanceRegistry/InstanceRegistry.cs
// File: InstanceRegistry.cs
// Purpose: Thread-sichere, read-only Registry über Instanz-DTOs aus dem ConfigService.

using System.Linq;                // nötig für ToList/ToDictionary
using Core.Domain.DTOs;
using Core.Domain.Services;
using Core.Logging;

namespace Modules.InstanceRegistry;

public class InstanceRegistry : IInstanceRegistry
{
    private readonly IConfigService _config;
    private readonly ILogService _log;
    private Dictionary<string, InstanceInfo> _byName = new(StringComparer.OrdinalIgnoreCase);
    private List<InstanceInfo> _all = new();

    public InstanceRegistry(IConfigService config, ILogService log)
    {
        _config = config;
        _log = log;
    }

    public void Initialize()
    {
        var src = _config.GetInstances();
        _all = src.ToList();
        _byName = _all.ToDictionary(i => i.Name, i => i, StringComparer.OrdinalIgnoreCase);
        _log.Info($"[InstanceRegistry] Initialisiert: {_all.Count} Instanzen indexiert.");
    }

    public IReadOnlyList<InstanceInfo> GetAll() => _all;
    public InstanceInfo? GetByName(string name) => _byName.TryGetValue(name, out var inst) ? inst : null;
    public bool Exists(string name) => _byName.ContainsKey(name);
}
