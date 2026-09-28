using FFXIVClientStructs.FFXIV.Client.UI;

namespace Chaperone.Alerting;

/// <summary>The sixteen chat sound effects. Game ids are 37..52; the ClientStructs helper takes 1..16.</summary>
public static class SoundEffects
{
    public static readonly string[] Names =
    [
        "se.1", "se.2", "se.3", "se.4", "se.5", "se.6", "se.7", "se.8",
        "se.9", "se.10", "se.11", "se.12", "se.13", "se.14", "se.15", "se.16",
    ];

    public static void Play(int id)
    {
        if (id is < 1 or > 16)
        {
            return;
        }

        UIGlobals.PlayChatSoundEffect((uint)id);
    }
}
