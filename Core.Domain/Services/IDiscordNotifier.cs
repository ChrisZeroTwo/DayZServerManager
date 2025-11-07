// Path: Core/Domain/Services/IDiscordNotifier.cs
// File: IDiscordNotifier.cs
// Purpose: Schnittstelle für den Discord-Notifier; erlaubt Re-Init nach Config-Reload.

namespace Core.Domain.Services;

public interface IDiscordNotifier
{
    /// <summary>Liest aktuelle Config und (de)aktiviert sich entsprechend.</summary>
    void Initialize();

    /// <summary>True, wenn Notifier aktiv ist (Webhook vorhanden & enabled).</summary>
    bool IsEnabled { get; }
}
