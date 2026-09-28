using Dalamud.Plugin;

namespace Chaperone;

public sealed class Plugin : IDalamudPlugin
{
    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();
        Services.Log.Information("Chaperone loaded (scaffold)");
    }

    public void Dispose()
    {
    }
}
