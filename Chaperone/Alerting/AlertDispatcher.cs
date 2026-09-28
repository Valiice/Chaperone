using Chaperone.Core;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.ImGuiNotification;

namespace Chaperone.Alerting;

/// <summary>Turns an engine alert into chat, sound and notification, each per config. Framework thread only.</summary>
public sealed class AlertDispatcher
{
    private const ushort NameColour = 34;

    private readonly Configuration config;
    private readonly WorldResolver worlds;

    public AlertDispatcher(Configuration config, WorldResolver worlds)
    {
        this.config = config;
        this.worlds = worlds;
    }

    public void Raise(AlertEvent alert)
    {
        var targeterWorld = this.worlds.WorldName(alert.TargeterWorldId);
        Services.Log.Information($"Chaperone: {alert.TargeterName}@{targeterWorld} is targeting {alert.SubjectName} ({alert.DwellSeconds:0.0}s)");

        if (this.config.ChatAlerts)
        {
            var message = new SeStringBuilder()
                .Add(new PlayerPayload(alert.TargeterName, alert.TargeterWorldId))
                .AddUiForeground(NameColour)
                .AddText(alert.TargeterName)
                .AddIcon(BitmapFontIcon.CrossWorld)
                .AddText(targeterWorld)
                .AddUiForegroundOff()
                .Add(RawPayload.LinkTerminator)
                .AddText(" is targeting ")
                .AddUiForeground(NameColour)
                .AddText(alert.SubjectName)
                .AddUiForegroundOff()
                .Build();
            Services.ChatGui.Print(message, "Chaperone");
        }

        if (this.config.SoundAlerts)
        {
            SoundEffects.Play(this.config.SoundId);
        }

        if (this.config.NotificationAlerts)
        {
            Services.NotificationManager.AddNotification(new Notification
            {
                Title = "Chaperone",
                Content = $"{alert.TargeterName}@{targeterWorld} is targeting {alert.SubjectName}",
                Type = NotificationType.Warning,
                InitialDuration = TimeSpan.FromSeconds(8),
            });
        }
    }
}
