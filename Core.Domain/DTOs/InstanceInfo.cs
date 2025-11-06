// Path: Core/Domain/DTOs/InstanceInfo.cs
// File: InstanceInfo.cs
// Purpose: Modell einer DayZ-Serverinstanz: Pfade, RCON, Mods, Flags. Mit Json-Mapping für robuste Deserialisierung.

using System.Text.Json.Serialization;

namespace Core.Domain.DTOs;

public class InstanceInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("serverRoot")]
    public string ServerRoot { get; set; } = string.Empty;

    [JsonPropertyName("profilesPath")]
    public string ProfilesPath { get; set; } = string.Empty;

    [JsonPropertyName("editableConfigPath")]
    public string EditableConfigPath { get; set; } = string.Empty;

    [JsonPropertyName("runtimeConfigPath")]
    public string RuntimeConfigPath { get; set; } = string.Empty;

    [JsonPropertyName("logFileName")]
    public string LogFileName { get; set; } = "server_console.log";

    [JsonPropertyName("launch")]
    public LaunchConfig Launch { get; set; } = new();

    [JsonPropertyName("rcon")]
    public RconConfig Rcon { get; set; } = new();

    [JsonPropertyName("mods")]
    public List<ModInfo> Mods { get; set; } = new();

    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; set; }

    [JsonPropertyName("maintenanceMode")]
    public bool MaintenanceMode { get; set; }
}
