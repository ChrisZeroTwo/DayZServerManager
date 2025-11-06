// Path: Core/Domain/Services/IConfigService.cs
// File: IConfigService.cs
// Purpose: Öffentliche Schnittstelle des ConfigService (manager.json + server.json laden).

using Core.Domain.DTOs;

namespace Core.Domain.Services;

public interface IConfigService
{
    void Initialize(string managerConfigPath);
    void Reload();
    ManagerConfig GetManagerConfig();
    IReadOnlyList<InstanceInfo> GetInstances();
}
