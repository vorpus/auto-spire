# CLAUDE.md — Agent Instructions for auto-spire

## Project overview

Automating Slay the Spire 2 (Godot 4.5.1 Mono / C# / .NET 9). We're building a mod that reads game state and sends commands, then an AI client that plays the game.

## Before you start

- Activate the Python venv: `source .venv/bin/activate`
- Mise handles dotnet/python automatically if the shell is configured
- ilspycmd is at `.dotnet-tools/ilspycmd`
- The game is installed at `~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/`
- The main game assembly is `sts2.dll` inside `.../Resources/data_sts2_macos_arm64/`

## Documentation requirements

**When you learn something new about the game, update the docs:**

- `docs/game-architecture.md` — game internals, class names, namespaces, data models, systems. Update this when decompiling or exploring game code.
- `docs/modding-approach.md` — modding strategy, loading mechanisms, Harmony patterns, progress. Update when making mod progress.
- `docs/research.md` — broader research on approaches, tools, community resources. Update when exploring new techniques.
- `README.md` — setup instructions, key paths, cleanup steps. Update when adding new tools or changing the dev environment.

**Do not duplicate information across docs.** Each doc has a clear scope — put things in the right place and cross-reference if needed.

## Key technical facts

- STS2 uses a custom Godot build called "MegaDot v4.5.1.m.8.mono"
- Game logic is in C# (`sts2.dll`), NOT GDScript — the PCK has scenes/resources but not game logic
- The game ships with `0Harmony.dll` (v2.4.2) and `MonoMod.Backports` — Harmony patching is the primary modding path
- The Godot binary has full C++ symbols (useful for Frida/debugging)
- Decompiled C# source goes in `./decompiled/` (gitignored)

## Conventions

- Use `docs/` for all research and documentation
- Use `.venv/` for Python dependencies (gitignored)
- Use `.dotnet-tools/` for .NET CLI tools (gitignored)
- Keep `mise.toml` as the source of truth for runtime versions
- Don't install tools globally — use mise, venv, or dotnet --tool-path

## What NOT to do

- Don't install tools globally (no `dotnet tool install -g`, no `pip install` outside venv)
- Don't commit decompiled game source
- Don't commit game binaries or assets
- Don't pursue screenshot/CV approaches unless all other options are exhausted
