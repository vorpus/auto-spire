# auto-spire

Automating Slay the Spire 2. Read game state, send commands, play intelligently.

STS2 is a C#/.NET 9 game on a custom Godot 4.5.1 Mono engine ("MegaDot"). It ships with HarmonyLib and a built-in mod loader, which we use to inject a bridge mod that exposes game state over HTTP.

## Architecture

```
STS2 Game Process
  └── AutoSpire.dll (mod, loaded by game's mod system)
        └── HTTP server on localhost:31452
              ├── GET /state    → full game state as JSON
              ├── GET /combat   → combat-specific state
              ├── POST /act     → send commands (play card, end turn, use potion)
              └── GET /ping     → health check

Python AI Client (planned)
  └── connects to HTTP API, reads state, sends commands
```

## API

### `GET /state` — Full game state

Returns run progress, player stats, deck, and combat info (if in combat).

```jsonc
{
  "phase": "combat",       // "menu" | "combat" | "map"
  "inRun": true,
  "floor": 1,
  "actIndex": 0,
  "roomType": "Monster",
  "player": {
    "hp": 80, "maxHp": 80, "block": 0, "gold": 99,
    "character": "IRONCLAD",
    "deck": [/* full card list with cost, type, keywords, upgrades */],
    "relics": [{"id": "BURNING_BLOOD", "name": "BurningBlood", "counter": 0}],
    "potions": [{"id": "FIRE_POTION", "targetType": "AnyEnemy", "rarity": "Common"}]
  },
  "combat": {
    "isPlayPhase": true,
    "round": 1,
    "player": {
      "energy": 3, "maxEnergy": 3, "stars": 0,
      "hand": [/* cards with canPlay, cost, targetType, keywords */],
      "drawPile": [/* full card details */],
      "discardPile": [],
      "exhaustPile": [],
      "powers": [{"id": "STRENGTH", "name": "Strength", "type": "Buff", "amount": 2}]
    },
    "enemies": [{
      "combatId": 1, "name": "Nibbit", "hp": 46, "maxHp": 46, "block": 0,
      "powers": [],
      "intent": {
        "moveId": "BUTT_MOVE",
        "intents": [{"type": "Attack", "damage": 8, "hits": 1, "totalDamage": 8}]
      }
    }]
  }
}
```

### `POST /act` — Send commands

```bash
# Play card at hand index 2, targeting enemy with combatId 1
curl -X POST http://localhost:31452/act -d '{"type":"play_card","cardIndex":2,"targetId":1}'

# End turn
curl -X POST http://localhost:31452/act -d '{"type":"end_turn"}'

# Use potion at index 0, targeting enemy with combatId 1
curl -X POST http://localhost:31452/act -d '{"type":"use_potion","potionIndex":0,"targetId":1}'
```

## Setup

### Prerequisites

Install [mise](https://mise.jdx.dev/) (manages project-scoped runtimes):

```bash
brew install mise
```

Add to your `~/.zshrc` (one-time):

```bash
eval "$(mise activate zsh)"
```

Restart your shell or `source ~/.zshrc`.

### Install project runtimes

```bash
cd auto-spire
mise install          # installs dotnet 9, dotnet 8, python 3.12, godot 4.5.1
```

### Install project tools

```bash
# Python venv
python -m venv .venv
source .venv/bin/activate
pip install frida-tools

# ILSpy decompiler (project-local)
dotnet tool install ilspycmd --tool-path .dotnet-tools
```

### Day-to-day usage

```bash
cd auto-spire
source .venv/bin/activate   # activate python venv
```

Mise activates dotnet/python/godot automatically when you `cd` into the project.

## Building the mod

```bash
./mod/build.sh    # builds DLL, creates PCK via Godot, deploys to game
```

This:
1. Compiles `mod/AutoSpire/` → `AutoSpire.dll`
2. Uses Godot 4.5.1 (via mise) to create `AutoSpire.pck` from `mod/pack/`
3. Copies both to `SlayTheSpire2.app/Contents/MacOS/mods/`

After deploying, restart STS2. Enable mods in game settings if prompted. Verify:

```bash
grep AutoSpire ~/Library/Application\ Support/SlayTheSpire2/logs/godot.log
curl http://localhost:31452/ping
```

## Key paths

| What | Path |
|------|------|
| Game install | `~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/` |
| Game DLL (C#) | `.../Resources/data_sts2_macos_arm64/sts2.dll` |
| Game logs | `~/Library/Application Support/SlayTheSpire2/logs/godot.log` |
| Mods directory | `.../SlayTheSpire2.app/Contents/MacOS/mods/` |
| Mod source | `./mod/AutoSpire/` |
| PCK project | `./mod/pack/` |
| Decompiled source | `./decompiled/` (gitignored) |

## Decompiling sts2.dll

```bash
.dotnet-tools/ilspycmd ~/Library/Application\ Support/Steam/steamapps/common/"Slay the Spire 2"/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64/sts2.dll -p -o ./decompiled/
```

Produces 3,298 C# files. Key namespaces: `MegaCrit.Sts2.Core.Combat`, `.Commands`, `.Entities.*`, `.AutoSlay`, `.Modding`.

## Cleanup

Everything is project-scoped or mise-managed:

```bash
rm -rf .venv              # python venv + frida-tools
rm -rf .dotnet-tools      # ilspycmd
rm -rf decompiled         # decompiled game source
mise uninstall dotnet     # remove dotnet runtimes
mise uninstall python     # remove python
mise uninstall godot      # remove godot
brew uninstall mise       # remove mise itself
```
