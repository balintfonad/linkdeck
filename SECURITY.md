# Security policy

## Reporting a vulnerability

Please report vulnerabilities **privately** through GitHub's [private vulnerability reporting](../../security/advisories/new). Do not open a public issue. You should get a response within a week.

## What LinkDeck does and does not do

- **Keyboard hook.** LinkDeck installs a global low-level keyboard hook (via SharpHook) to detect its hotkey. It only reacts to the configured hotkey (and `Esc` while the hotkey's modifiers are held) and suppresses those keys. **It never logs, stores or transmits keystrokes.**
- **No secrets.** Passwords are never stored. Every RDP connection uses a temporary `.rdp` file in `%TEMP%\LinkDeck\` with the address, the optional username and fixed safety settings; it is deleted after about 10 seconds and leftovers are removed at startup.
- **Changes from outside are confirmed.** LinkDeck remembers which items you added or edited in LinkDeck itself, as fingerprints of what they run (program, arguments, working directory, address, RDP host/port/user), in `%LOCALAPPDATA%\LinkDeck\trust.dat`, protected with Windows DPAPI for your account. An item that another program added to or changed in `config.json` (for example an "Outlook" tile retargeted to another `.exe`) is announced, and before it opens LinkDeck shows exactly what it runs and asks. Renaming, recolouring or reordering does not need confirmation. On a new computer, or if `trust.dat` was edited or copied, each item asks once.
- **Network.** The only network access is fetching a favicon directly from a web item's own site when the item is added: 5-second timeout, 200 KB limit, image content types only, redirects followed only to `http(s)` and never from `https` to `http`. Public sites are fetched without a proxy and only if their name resolves to public addresses, so a web page cannot steer requests into your local network. No telemetry, no automatic updates.
- **URL allow-list.** Only `http` and `https` URLs are opened. `file:`, `javascript:` and custom schemes are rejected. Link files that can carry any scheme (`.url`, `.website`, `.scf`, `.search-ms`, `.searchConnector-ms`, `.library-ms`) are neither imported nor opened; a `.url` with a web address is imported as that address.
- **No credentials sent for icons.** Icons are never read from network paths (`\\server\share\…`): Windows would connect and send your NTLM credentials just to draw a tile. Such items, and shortcuts whose target or icon is on the network, get a generic icon until you open them.
- **What runs is visible.** Arguments are shown in the quick-add dialog (including those of a dropped shortcut), in the footer and in tile tooltips. Names, paths, arguments and URLs with control, invisible or text-direction characters (e.g. `gpj‮.exe` shown as `exe.jpg`) are rejected.
- **RDP.** Hosts, ports and usernames are strictly validated so nothing can be injected into `mstsc` arguments or `.rdp` files. `mstsc.exe` is started from the System32 directory by absolute path. The generated `.rdp` file sets `authentication level:i:2` (warn if the server cannot be verified), CredSSP/NLA, and turns off drive, plug-and-play device and COM port redirection, so settings in `Documents\Default.rdp` cannot hand your local drives to a server. Clipboard, printers and smart cards keep their Windows defaults.
- **Portable mode only in private folders.** A `config.json` next to `LinkDeck.exe` is used only if no other account can change that folder (otherwise another user could plant items that run under your account). Removable FAT/exFAT drives, which have no permissions, are accepted.
- **Pinned dependencies.** All NuGet packages, including transitive ones, are pinned in `packages.lock.json`; CI and releases restore in locked mode.

## Known risks

- **A configuration file executes programs.** `path` items start arbitrary programs. Only use `config.json` files from sources you trust. Items from such a file ask for confirmation once, showing what they run.
- **Malware running as you.** Confirmation stops other users, synced or shared config files and untargeted tampering. A program already running under your account could also use DPAPI and edit `trust.dat`; no per-user app can fully defend against that.
- **Antivirus false positives.** Unsigned executables with global keyboard hooks may be flagged. Code signing is planned.
- **Elevated windows.** A non-elevated LinkDeck cannot receive the hotkey while an elevated (administrator) window has focus. This is a Windows security boundary, not a bug.
