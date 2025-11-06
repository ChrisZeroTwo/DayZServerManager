// Path: Core/Domain/Services/IConfigService.cs
// File: IConfigService.cs
// Purpose: Öffentliche Schnittstelle des ConfigService: Laden von manager.json und server.json, read-only Zugriff.

using Core.Domain.DTOs;

namespace Core.Domain.Services;

public interface IConfigService
{
    void Initialize(string managerConfigPath);
    void Reload();
    ManagerConfig GetManagerConfig();
    IReadOnlyList<InstanceInfo> GetInstances();
}
