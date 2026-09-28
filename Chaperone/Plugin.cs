using Chaperone.Alerting;
using Chaperone.Core;
using Chaperone.Sensors;
using Chaperone.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;

namespace Chaperone;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/chaperone";

    private readonly Configuration config;
    private readonly WindowSystem windowSystem = new("Chaperone");
    private readonly ChaperoneEngine engine;
    private readonly WorldResolver worlds;
    private readonly WatchlistManager? watchlist;
    private readonly AlertDispatcher? alerts;
    private readonly ObjectTableSampler? sampler;
    private readonly MainWindow? mainWindow;
    private readonly SettingsWindow? settingsWindow;
    private readonly ContextMenuIntegration? contextMenu;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();
        this.config = Configuration.Load(pluginInterface);
        this.engine = new ChaperoneEngine(new EngineSettings());
        this.worlds = new WorldResolver();

        // Dalamud never calls Dispose on a plugin whose ctor threw, so tear down ourselves.
        try
        {
            this.watchlist = new WatchlistManager(this.config, this.worlds, this.engine);
            this.alerts = new AlertDispatcher(this.config, this.worlds);
            this.sampler = new ObjectTableSampler(this.config, this.engine, this.alerts.Raise);

            this.settingsWindow = new SettingsWindow(this.config, this.watchlist, this.sampler, this.engine, this.OnConfigChanged);
            this.mainWindow = new MainWindow(this.config, this.engine, this.sampler, () => this.settingsWindow!.Toggle());
            this.windowSystem.AddWindow(this.mainWindow);
            this.windowSystem.AddWindow(this.settingsWindow);

            this.contextMenu = new ContextMenuIntegration(this.watchlist, this.worlds);

            pluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
            pluginInterface.UiBuilder.OpenMainUi += this.OpenMain;
            pluginInterface.UiBuilder.OpenConfigUi += this.OpenSettings;
            Services.ClientState.Login += this.OnLogin;
            Services.ClientState.Logout += this.OnLogout;

            Services.CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
            {
                HelpMessage = "Toggle Chaperone. Subcommands: add First Last@World, remove First Last@World, target, list, config",
            });

            if (this.config.OpenOnLogin && Services.ClientState.IsLoggedIn)
            {
                this.mainWindow.IsOpen = true;
            }
        }
        catch
        {
            this.Teardown();
            throw;
        }
    }

    public void Dispose() => this.Teardown();

    private void Teardown()
    {
        Guard(() => Services.CommandManager.RemoveHandler(CommandName), "command handler");
        Guard(
            () =>
            {
                Services.ClientState.Login -= this.OnLogin;
                Services.ClientState.Logout -= this.OnLogout;
                Services.PluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
                Services.PluginInterface.UiBuilder.OpenMainUi -= this.OpenMain;
                Services.PluginInterface.UiBuilder.OpenConfigUi -= this.OpenSettings;
                this.windowSystem.RemoveAllWindows();
            },
            "UI wiring");
        Guard(() => this.contextMenu?.Dispose(), "context menu");
        Guard(() => this.sampler?.Dispose(), "sampler");
        Guard(() => this.config.Dispose(), "configuration flush");
    }

    private static void Guard(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Services.Log.Error(e, $"Chaperone teardown: {what} failed");
        }
    }

    private void OnLogin()
    {
        if (this.config.OpenOnLogin && this.mainWindow != null)
        {
            this.mainWindow.IsOpen = true;
        }
    }

    private void OnLogout(int type, int code) => this.sampler?.Reset();

    private void OnConfigChanged() => this.mainWindow?.ApplyConfig();

    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim();
        if (trimmed.Length == 0)
        {
            this.mainWindow?.Toggle();
            return;
        }

        var space = trimmed.IndexOf(' ');
        var verb = space < 0 ? trimmed : trimmed[..space];
        var rest = space < 0 ? "" : trimmed[(space + 1)..].Trim();

        switch (verb.ToLowerInvariant())
        {
            case "add":
                Services.ChatGui.Print(this.watchlist!.Add(rest) ?? $"Chaperone: now watching {rest}.");
                break;
            case "remove":
                Services.ChatGui.Print(this.watchlist!.Remove(rest) ?? $"Chaperone: removed {rest}.");
                break;
            case "target":
                Services.ChatGui.Print(this.watchlist!.AddCurrentTarget() ?? "Chaperone: added your current target.");
                break;
            case "list":
                this.PrintList();
                break;
            case "config":
            case "settings":
                this.OpenSettings();
                break;
            default:
                Services.ChatGui.Print("Chaperone: add First Last@World | remove First Last@World | target | list | config");
                break;
        }
    }

    private void PrintList()
    {
        var entries = this.watchlist!.Entries;
        if (entries.Count == 0)
        {
            Services.ChatGui.Print("Chaperone: the watchlist is empty.");
            return;
        }

        Services.ChatGui.Print($"Chaperone: watching {entries.Count} player(s):");
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            Services.ChatGui.Print($"  {e.Display}{(e.Enabled ? "" : " (off)")}");
        }
    }

    private void OpenMain()
    {
        if (this.mainWindow != null)
        {
            this.mainWindow.IsOpen = true;
        }
    }

    private void OpenSettings()
    {
        if (this.settingsWindow != null)
        {
            this.settingsWindow.IsOpen = true;
        }
    }
}
