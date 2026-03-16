# auto-spire

Automating Slay the Spire 2. Read game state, send commands, play intelligently.

STS2 is a C#/.NET 9 game on a custom Godot 4.5.1 Mono engine ("MegaDot"). It ships with HarmonyLib, making .NET-level modding the primary approach.

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
mise install          # installs dotnet 9, dotnet 8, python 3.12 per mise.toml
```

### Install project tools

```bash
# Python venv + frida
python -m venv .venv
source .venv/bin/activate
pip install frida-tools

# ILSpy decompiler (project-local)
dotnet tool install ilspycmd --tool-path .dotnet-tools
```

### Day-to-day usage

```bash
cd auto-spire
source .venv/bin/activate   # activate python venv (frida-tools, etc.)
```

Mise activates dotnet/python automatically when you `cd` into the project (if your shell is configured).

## Key paths

| What | Path |
|------|------|
| Game install | `~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/` |
| Game app bundle | `.../SlayTheSpire2.app/Contents/` |
| Game DLL (C#) | `.../Resources/data_sts2_macos_arm64/sts2.dll` |
| Game PCK | `.../Resources/Slay the Spire 2.pck` (1.6 GB) |
| Game logs | `~/Library/Application Support/SlayTheSpire2/logs/godot.log` |
| Decompiled source | `./decompiled/` (gitignored, generated locally) |

## Decompiling sts2.dll

```bash
.dotnet-tools/ilspycmd ~/Library/Application\ Support/Steam/steamapps/common/"Slay the Spire 2"/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64/sts2.dll -p -o ./decompiled/
```

## Cleanup

Everything is project-scoped or mise-managed. To fully remove:

```bash
rm -rf .venv              # python venv + frida-tools
rm -rf .dotnet-tools      # ilspycmd
rm -rf decompiled         # decompiled game source
mise uninstall dotnet     # remove dotnet runtimes from ~/.local/share/mise/
mise uninstall python     # remove python from ~/.local/share/mise/
brew uninstall mise       # remove mise itself
```

Mise stores runtimes in `~/.local/share/mise/`. Nothing else is installed globally.
