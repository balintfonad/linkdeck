# Contributing to LinkDeck

Thanks for helping! Bug reports, ideas and pull requests are all welcome.

## Getting started

1. Install the .NET 10 SDK (version pinned in `global.json`). Windows is required for the desktop app; `LinkDeck.Core` also builds and tests on Linux and macOS.
2. `dotnet build LinkDeck.slnx` and `dotnet test LinkDeck.slnx`.
3. Run the app: `dotnet run --project src/LinkDeck.Desktop`. Tip: put a test `config.json` next to the built `LinkDeck.exe` (portable mode) so your real configuration is not touched.

## Ground rules

- **Core stays platform-agnostic.** `LinkDeck.Core` must not reference Avalonia, Windows APIs or platform projects. Platform behaviour goes behind an interface in `Core/Abstractions` and is implemented in `LinkDeck.Platform.*`.
- **Everything must work under NativeAOT.** No reflection-based serialization (use `LinkDeckJsonContext`), compiled XAML bindings only, source-generated COM/P/Invoke.
- **Tests.** New logic needs unit tests. Process starts go through `IProcessRunner`, so launch tests never start anything. UI changes: add a ViewModel test and, where useful, an `Avalonia.Headless` test. Hotkey/focus/DPI changes: run the relevant part of `docs/manual-test-checklist.md`.
- **Style.** `dotnet format LinkDeck.slnx` before committing; CI runs `--verify-no-changes`. Warnings are errors.
- **Privacy.** Never log keystrokes, URLs of items, usernames or anything secret. No new network access without discussion.
- **Config format changes** need a `version` bump, a migration (`IConfigMigration`), schema update and tests.

## Pull requests

Keep them focused, describe what and why, and update `CHANGELOG.md` under *Unreleased* for user-visible changes. Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/).

By contributing you agree that your contributions are licensed under the MIT license, and you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).
