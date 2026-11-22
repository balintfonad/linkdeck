# LinkDeck

An offline, local-only launcher for Windows. Press a global hotkey and an Alt+Tab-style overlay appears, but instead of windows it shows **your** items: web pages, folders, files, programs and remote desktop (RDP) connections.

- No account, no server, no telemetry. Everything lives in local files.
- No passwords are ever stored.
- For people who don't want to manage browser bookmarks, and for IT staff who find connection managers too heavy.

## Using it

| Do this | To |
|---|---|
| `Alt+Ctrl`, release | Open the launcher and keep it open (nothing is launched) |
| Keep holding `Alt`, tap `Ctrl` | Next item (like Tab in Alt+Tab) |
| `Shift+Ctrl` while holding `Alt` | Previous item |
| Release `Alt` after tapping `Ctrl` | Open the selected item |
| `Space` while holding `Alt` | Keep the launcher open to search and browse |
| `Enter` / `Shift+Enter` | Open / copy the URL or path |
| `Ctrl+1` … `Ctrl+9` | Open the Nth visible item |
| `Tab` / `Shift+Tab` | Next / previous tab (All, then your groups). Also works while holding Alt - LinkDeck swallows it, so Windows' Alt+Tab does not open |
| `+` button, `Ctrl+N`, tray menu *Add item…*, or drag a link, file, folder, Start-menu app, shortcut or `.rdp` file onto the launcher | Add an item (the dialog can also pick from open and installed apps) |
| Right-click a tile, or `Del` with an empty search box | Open, copy, edit, give it a colour (colour wheel), move or copy to another group, or remove the item |
| ☰ menu (top left of the open launcher) | Settings…, Keep open, Center on screen, Add item…, New group… |
| Drag the grab bar at the top (or any empty part) | Move the launcher; it reopens where you left it |
| Drag a tile onto another one, or `Ctrl+Shift+arrows` | Arrange items by hand (switches the order to manual; ☰ → Manual order toggles it) |
| Drag any edge or corner | Resize the launcher like a terminal: it shows columns × rows and snaps to whole tiles; the size is remembered (also in Settings → General) |
| `+ Group` button, or right-click a tab | Create your own group; right-click a group tab to rename or delete it (its items move to another group). Every group is yours: a new configuration starts with Web, Apps, Files & folders and Remote desktop groups, and new items go to the group for their type unless you pick one. Settings → *Sort into type groups…* sorts an existing configuration the same way |
| Click into the launcher while holding Alt | Same as Space: keep it open |
| `Esc` | Close |

Left-click the tray icon to open the launcher; right-click it for Settings, the config file, *Pause hotkey* and Exit. The hold and step keys (Alt, Ctrl or Win) can be changed in Settings.

> **AltGr keyboards** (Hungarian, German, Polish, …): only the **left** Alt opens LinkDeck, so typing `@`, `{`, `[` with AltGr never triggers it. Other apps' `Ctrl+Alt+…` shortcuts keep working: pressing any other key while holding cancels the gesture and the key goes to the app.

## Install

Download from [Releases](../../releases):

- `LinkDeck-win-x64-Setup.exe` (or `arm64`): installer, per-user, no admin rights needed.
- `LinkDeck-<version>-win-x64-portable.zip`: unzip anywhere. Put a `config.json` next to `LinkDeck.exe` to keep config and usage data in that folder (portable mode). Portable mode needs a folder only you can change (e.g. in your user profile or on a USB stick); in a folder other accounts can write to, such as `C:\Tools\…`, LinkDeck ignores that `config.json` and says so.

Verify downloads against `SHA256SUMS.txt`.

**Antivirus warnings:** LinkDeck is not code-signed yet and installs a global keyboard hook, which some antivirus products flag. The source is here; build it yourself if in doubt. Signing is planned.

## Configuration

| File | Location |
|---|---|
| `config.json` - items, groups, settings | `%APPDATA%\LinkDeck\` |
| `state.json` - usage statistics (never share) | `%APPDATA%\LinkDeck\` |
| Favicon cache | `%LOCALAPPDATA%\LinkDeck\icons\` |
| Logs (7 days) | `%LOCALAPPDATA%\LinkDeck\logs\` |
| `trust.dat` - items you confirmed (DPAPI-protected, this computer only) | `%LOCALAPPDATA%\LinkDeck\` |

`config.json` is plain JSON, safe to hand-edit, track in git or share with your team. LinkDeck reloads it automatically when it changes. Items added or changed by editing the file (rather than in LinkDeck) show what they run and ask once before they open. See [`config.example.json`](config.example.json) and the [JSON Schema](schema/config.schema.json) (editors such as VS Code give autocompletion through the `$schema` line).

```json
{ "id": ".", "type": "url",  "name": "Jira",        "url": "https://jira.example.com" }
{ "id": ".", "type": "path", "name": "VS Code",     "path": "%LOCALAPPDATA%\\Programs\\Microsoft VS Code\\Code.exe", "args": "--new-window" }
{ "id": ".", "type": "rdp",  "name": "Prod server", "host": "10.0.0.15", "port": 3389, "username": "admin" }
```

Setting `"keepOpen": true` (or *Keep open* in the menu or Settings → General) keeps the launcher open after launching an item or clicking elsewhere; only Esc closes it. `"launchOnRelease"` (default `true`, Settings → General) launches the selected item when you release the held key after stepping; turn it off to only select and press Enter to launch. `"backgroundOpacity"` (0.2–1, Settings → General) makes the launcher background see-through; an item's `"color"` (`#RRGGBB`, or the colour wheel) tints its tile.

Rules: no comments (GUI saves would drop them); unknown fields are ignored and dropped on the next save; invalid items are skipped with a notification; if the file is not valid JSON LinkDeck keeps the last good version and never overwrites your file.

> ⚠️ **Only load configuration files from sources you trust.** A `config.json` can start any program on your computer.

## Privacy and security

- **The keyboard hook only reacts to the configured hold and step keys (and Space, Esc and Shift while they are held). LinkDeck never logs or stores keystrokes.**
- The only network access is fetching a site's favicon from **that site itself**, once, when you add a web item (5 s timeout, 200 KB limit, images only).
- Only `http` and `https` URLs can be opened; `.url` and similar link files that could carry other schemes are not opened.
- Items changed in `config.json` by another program must be confirmed, with the full program, arguments or address shown, before they open.
- Icons are never read from network shares, so drawing tiles never sends your Windows credentials anywhere.
- RDP: LinkDeck never sees passwords. Windows asks for them and can remember them in its Credential Manager. Connections never redirect your local drives, whatever `Default.rdp` says.

See [SECURITY.md](SECURITY.md) for reporting vulnerabilities.

## Building

Requires the .NET 10 SDK (see `global.json`) on Windows.

```powershell
dotnet build LinkDeck.slnx
dotnet test LinkDeck.slnx
dotnet run --project src/LinkDeck.Desktop
# NativeAOT build:
dotnet publish src/LinkDeck.Desktop -c Release -r win-x64 -p:PublishAot=true
```

Architecture: `LinkDeck.Core` (platform-agnostic logic: models, config store, search, popup state machine, parsers) ← `LinkDeck.Platform.Windows` (launching, shortcuts, autostart, shell icons) and `LinkDeck.Desktop` (Avalonia UI, tray, SharpHook hotkey). Core never references a platform project, so Linux and macOS adapters can be added later. The design spec is in [`docs/superpowers/specs`](docs/superpowers/specs).

Contributions are welcome - see [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE)
