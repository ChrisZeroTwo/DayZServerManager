// Path: Core/Domain/DTOs/ModInfo.cs
// File: ModInfo.cs
// Purpose: Workshop-Mod-Beschreibung. Json-Mapping für Felder.

using System.Text.Json.Serialization;

namespace Core.Domain.DTOs;

public class ModInfo
{
    [JsonPropertyName("workshopId")]
    public long WorkshopId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("localPath")]
    public string LocalPath { get; set; } = string.Empty;
}
