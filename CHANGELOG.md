# Changelog

All notable changes to this project are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.1.0] - 2026-11-22

First release.

### Added

- **Alt+Tab-style launcher.** Hold `Alt` and press `Ctrl` to open it, keep holding Alt and tap Ctrl to move (Shift+Ctrl moves back), release Alt to open the selection, press Space (or click) to keep it open. Only the left Alt counts, so AltGr never triggers it; other apps' Ctrl+Alt shortcuts cancel the gesture and still work. `Alt+Ctrl` and releasing without stepping only opens the launcher. The hold and step keys (Alt, Ctrl or Win) can be changed; stored as `"hotkey": { "hold": "Alt", "step": "Ctrl" }`.
- Item types: web page (`url`), folder/file/program (`path`) and remote desktop (`rdp`).
- Fuzzy search and frecency ranking (7-day half-life), or manual order.
- Groups: new configurations start with Web, Apps, Files & folders and Remote desktop groups; all groups can be renamed, deleted and created (`+ Group`, right-click a group tab). Switch groups with `Tab` / `Shift+Tab`, also while holding Alt. Quick-add puts new items into the group for their type unless you choose one. Settings → *Sort into type groups…* sorts existing items.
- `Ctrl+1…9`, `Shift+Enter` to copy the target, `Ctrl+N` quick-add, `Del` to remove; right-click a tile to edit, remove, colour, move or copy it to another group.
- Add items by dragging links, files, folders, `.lnk` shortcuts, `.rdp` files or Start-menu apps (desktop and Store apps, stored as `shell:AppsFolder…`) onto the launcher, with the `+` button, *Add item…* in the tray menu, or by picking from open windows and Start-menu apps in quick-add. Quick-add has `File…`, `Folder…` and `New…` (group) buttons.
- Arrange items by dragging a tile onto another or with `Ctrl+Shift+arrows`. On "All" this changes only the All order (`"allOrder"`), on a group tab only that group's order. The first arrangement switches to manual order; ☰ → Manual order toggles it.
- Tile and group colours on a colour wheel (`"color"`).
- *Keep open* (the launcher stays open after launching or clicking elsewhere; Esc closes it) and *Launch on release* (on by default; off: stepping only selects and Enter launches) settings. The hotkey brings an open launcher to the front and keeps stepping, wrapping around.
- Background opacity (50–100 %) and blur (Off / Soft / Strong) for the launcher; tiles stay readable.
- Move the launcher by its grab bar or any empty part; resize it from any edge or corner like a terminal (snaps to whole tiles, shows columns × rows). Position and size are remembered (`"popupColumns"`, `"popupRows"`); ☰ → Center on screen resets the position.
- Menu button in the launcher: Settings, Keep open, Add item, New group.
- Settings window: groups, items, reordering, move/copy between groups (drag onto a group, Ctrl to copy), hotkey recorder, theme, start with Windows, sort mode. "← Back" (or Esc) saves and returns to the launcher; closing with unsaved changes asks Save / Don't save.
- Settings, quick-add and dialogs open on the monitor you are working on.
- Hand-editable `config.json` with JSON Schema, live reload, atomic saves, backups, migrations and read-only protection; missing settings get their defaults.
- System tray icon, single instance, portable mode, daily logs.
- Favicons fetched once from the item's own site; offline shell icons for paths.
- NativeAOT builds, Velopack installer and portable zip for win-x64 and win-arm64.

### Security

- **Changes from outside are confirmed.** Items added or changed in `config.json` by another program, or a synced or shared file, are announced and must be confirmed before they open. The confirmation shows the program, arguments and working directory, the address, or the RDP computer and user. Confirmed items are remembered as fingerprints in `%LOCALAPPDATA%\LinkDeck\trust.dat`, protected with Windows DPAPI; renaming or recolouring needs no confirmation.
- **Portable mode only in private folders.** A `config.json` next to `LinkDeck.exe` is used only when no other account can change that folder (not e.g. `C:\Tools\…`, where all users may write by default); otherwise it is ignored with a warning. Removable FAT/exFAT drives are accepted.
- **Only http and https.** `file:`, `javascript:` and custom URL schemes are rejected. `.url` files are imported only when they hold a web address, and `.url`, `.website`, `.scf`, `.search-ms`, `.searchConnector-ms` and `.library-ms` files are never opened, so they cannot bypass this rule.
- **No credentials sent for icons.** Icons are never read from network paths, including the icon or target of a shortcut, so drawing a tile cannot send your Windows (NTLM) credentials to another computer. Network icon paths in `config.json` are ignored with a warning.
- **What runs is visible.** Quick-add shows and lets you edit the arguments and working directory of a dropped shortcut or picked app; the footer and tile tooltips show arguments too. Names, paths, arguments, URLs and RDP usernames with control, invisible or text-direction characters (e.g. U+202E, which shows `gpj\u202E.exe` as `exe.jpg`) are rejected.
- **Safe RDP.** Passwords are never stored. Hosts, ports and usernames are strictly validated; `mstsc.exe` runs from System32 with a generated `.rdp` file that warns when the server cannot be verified, uses CredSSP, and never redirects local drives, devices or COM ports, whatever `Documents\Default.rdp` says.
- **Favicons cannot reach your local network.** A public site's name must resolve to public addresses, redirects and `<link rel="icon">` targets cannot point into the local network or from `https` to `http`, and public sites are fetched without a proxy. Intranet links still get their favicons.
- **Supply chain.** NuGet packages, including transitive ones, are pinned in `packages.lock.json` and restored in locked mode. In the release workflow only the publishing job has a write token, the tag name is validated, every GitHub Action is pinned to a commit SHA, and releases carry a build provenance attestation (`gh attestation verify <file> --repo balintfonad/linkdeck`).

[Unreleased]: https://github.com/balintfonad/linkdeck/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/balintfonad/linkdeck/releases/tag/v0.1.0
