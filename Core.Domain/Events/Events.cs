// Path: Core/Domain/Events/Events.cs
// File: Events.cs
// Purpose: Domain-Events für Instanz-Lebenszyklus, Updates, RCON und Integrationen.

using System;
using System.Collections.Generic;

namespace Core.Domain.Events;

// Lebenszyklus
public record InstanceStartedEvent(string InstanceName, DateTime StartedAt);
public record InstanceStoppedEvent(string InstanceName, string Reason);
public record InstanceFrozenEvent(string InstanceName, TimeSpan LastWriteAge);
public record InstanceNeedsRestartEvent(string InstanceName, string Reason);

// Updates / Mods
public record ModUpdateAvailableEvent(string InstanceName, long WorkshopId, string ModName);
public record InstanceUpdateScheduledEvent(string InstanceName, int VisibleSeconds, int TotalSeconds);
public record InstanceUpdateCompletedEvent(string InstanceName, List<long> UpdatedMods);

// RCON / Spieler
public record InstanceLockedEvent(string InstanceName);
public record InstanceUnlockedEvent(string InstanceName);
public record PlayersKickedEvent(string InstanceName);

// Administration
public record MaintenanceModeChangedEvent(string InstanceName, bool MaintenanceMode);

// Extern / Integrationen
public record DiscordNotifyEvent(string Title, string Message, string Level);
public record CFToolsShutdownDetectedEvent(string InstanceName);
