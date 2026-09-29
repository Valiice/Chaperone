# Chaperone

Dalamud plugin that shows who is targeting your friends. Think PeepingTom, but for other people.

Add players to a watchlist. Whenever one of them is near you, Chaperone lists everyone targeting them, live, in a small window. If a stranger keeps a friend targeted for longer than a few seconds, you get a chat line with their name and a sound. The player you are currently targeting is watched the same way while you have them targeted, so you also learn when someone else is looking at the same person.

## Installation

1. Add my custom plugin repository: https://raw.githubusercontent.com/Valiice/DalamudPluginRepo/master/repo.json
2. Search for **Chaperone** in the Plugin Installer.
3. Install and Enable.

Not on the official repo. Alternatively, build with `dotnet build` and load as a dev plugin.

## Usage

Open the window with `/chaperone`. Each row is one person who is targeting someone you watch: their name on the left, and on the right who they are targeting and for how long. A "mutual" tag means the watched person is targeting them back. Greyed rows are people who stopped, with how long ago. Watched friends who nobody is targeting do not take up space.

Row controls, same as PeepingTom:

- Hover: focus target the player
- Left click: post their name in chat as a link
- Right click: target them
- Alt + left click: examine them

Adding people:

- Right click any player and choose "Add to Chaperone"
- `/chaperone add First Last@World` (the world is optional and defaults to your current world)
- `/chaperone target` adds whoever you have targeted
- The Watchlist tab in settings has an add field and an "Add current target" button

Other commands: `/chaperone remove First Last@World`, `/chaperone list`, `/chaperone config`.

## Alerts

An alert fires once someone has held a watched friend targeted for the configured time (3 seconds by default), then not again for that pair for a minute. By default an alert plays a chat sound effect of your choice. A chat line with a clickable name and a Dalamud notification are both available as options.

By default these people never trigger alerts, only rows in the window: your party and alliance, your friend list, your free company, and other players on your watchlist. Each is a toggle in the Filters tab. Targeters in combat are ignored unless you turn that on.

## Settings

Alerts: channels, sound, threshold, cooldowns, flicker grace, history length.
Filters: who is ignored, pause in PvP, poll interval.
Window: open on login, hide in combat or duties, focus on hover, world markers, server info bar entry.
Debug: sampler state and poll cost.

## Limitations

- Chaperone can only see players loaded by your game client. A friend has to be near you, in the same zone and within range. It cannot watch anyone elsewhere.
- Only hard targets are visible for other players. Soft targets, focus targets and mouse-over are not sent by the server.
- History is kept in memory and cleared on logout.

## Privacy

Chaperone reads only what your client already has in memory to draw the players around you. Nothing is sent anywhere and nothing is written to disk except your own settings and watchlist.
