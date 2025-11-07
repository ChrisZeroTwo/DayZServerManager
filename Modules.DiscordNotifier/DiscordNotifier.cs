// Path: Modules/DiscordNotifier/DiscordNotifier.cs
// File: DiscordNotifier.cs
// Purpose: Hört auf Events, filtert per manager.json und postet Meldungen an Discord-Webhook.

using System.Net.Http;
using System.Text;
using System.Text.Json;
using Core.Domain;
using Core.Domain.DTOs;
using Core.Domain.Events;
using Core.Domain.Services;
using Core.Logging;

namespace Modules.DiscordNotifier;

public class DiscordNotifier : IDiscordNotifier
{
    private readonly IEventBus _bus;
    private readonly ILogService _log;
    private readonly IConfigService _cfg;

    private static readonly HttpClient _http = new();

    private bool _enabled;
    private string? _webhook;
    private DiscordNotifyFlags _flags = new();

    public bool IsEnabled => _enabled && !string.IsNullOrWhiteSpace(_webhook);

    public DiscordNotifier(IEventBus bus, ILogService log, IConfigService cfg)
    {
        _bus = bus;
        _log = log;
        _cfg = cfg;

        // Subscriptions – Filter entscheidet zur Laufzeit
        _bus.Subscribe<DiscordNotifyEvent>(OnGeneric);
        _bus.Subscribe<InstanceStartedEvent>(e => TrySend(_flags.InstanceStarted, $"✅ **{e.InstanceName}** gestartet."));
        _bus.Subscribe<InstanceStoppedEvent>(e => TrySend(_flags.InstanceStopped, $"🛑 **{e.InstanceName}** gestoppt ({e.Reason})."));
        _bus.Subscribe<InstanceUpdateScheduledEvent>(e => TrySend(_flags.RestartScheduled, $"⏳ **{e.InstanceName}** Neustart in {e.VisibleSeconds}s."));
        _bus.Subscribe<InstanceFrozenEvent>(e => TrySend(_flags.MonitorFreeze, $"🥶 **{e.InstanceName}** Freeze-Verdacht (Log-Age {e.LastWriteAge.TotalSeconds:n0}s)."));
        _bus.Subscribe<InstanceNeedsRestartEvent>(e => TrySend(_flags.InstanceNeedsRestart, $"♻️ **{e.InstanceName}** benötigt Neustart: {e.Reason}."));
        _bus.Subscribe<InstanceUpdateCompletedEvent>(e =>
            TrySend(_flags.InstanceUpdateCompleted, $"📦 **{e.InstanceName}** Mods aktualisiert: {string.Join(", ", e.UpdatedMods)}"));
        // RCON-Hinweise kommen als DiscordNotifyEvent – Flag 'rconCommands' greift dort.
    }

    public void Initialize()
    {
        var m = _cfg.GetManagerConfig();

        // Defaults/Fallbacks
        _enabled = true;
        _webhook = null;
        _flags = new DiscordNotifyFlags();

        // Neue Struktur bevorzugen
        if (m.Discord is not null)
        {
            _enabled = m.Discord.Enabled;
            _webhook = string.IsNullOrWhiteSpace(m.Discord.Webhook) ? m.DiscordWebhook : m.Discord.Webhook; // Fallback
            _flags = m.Discord.Notify ?? new DiscordNotifyFlags();
        }
        else
        {
            // Nur alter Webhook vorhanden?
            _webhook = m.DiscordWebhook;
            _enabled = !string.IsNullOrWhiteSpace(_webhook);
            _flags = new DiscordNotifyFlags();
        }

        _log.Info($"[Discord] {(IsEnabled ? "aktiviert" : "deaktiviert")} (Filter: gen={_flags.Generic}, start={_flags.InstanceStarted}, stop={_flags.InstanceStopped}, rsched={_flags.RestartScheduled}, rcomp={_flags.RestartCompleted}, freeze={_flags.MonitorFreeze}, modAvail={_flags.ModUpdateAvailable}, needsRestart={_flags.InstanceNeedsRestart}, updDone={_flags.InstanceUpdateCompleted}, rcon={_flags.RconCommands})");
    }

    private void OnGeneric(DiscordNotifyEvent e)
    {
        if (!IsEnabled) return;

        var isRcon = e.Title?.Contains("RCON", StringComparison.OrdinalIgnoreCase) == true;
        if (isRcon && !_flags.RconCommands) return; // RCON stumm

        if (_flags.Generic)
            _ = PostAsync($"**{e.Title}** – {e.Message} ({e.Level})");
    }

    private void TrySend(bool allowed, string content)
    {
        if (!IsEnabled || !allowed) return;
        _ = PostAsync(content);
    }

    private async Task PostAsync(string content)
    {
        try
        {
            if (!IsEnabled || string.IsNullOrWhiteSpace(_webhook)) return;

            var payload = JsonSerializer.Serialize(new { content });
            using var req = new HttpRequestMessage(HttpMethod.Post, _webhook)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                var text = await resp.Content.ReadAsStringAsync(); // <-- hier war der Copy-Paste-Fehler
                _log.Warn($"[Discord] Webhook HTTP {(int)resp.StatusCode}: {text}");
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"[Discord] Fehler beim Senden: {ex.Message}");
        }
    }
}
