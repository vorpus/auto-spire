# Auto-Spire: Research — Hooking into Slay the Spire 2

## Game Facts (from local inspection)

| Property | Value |
|----------|-------|
| Engine | **MegaDot v4.5.1.m.8.mono** (custom Godot 4.5.1 + .NET/C#) |
| Runtime | **.NET 9.0** (osx-arm64) |
| Game DLL | `sts2.dll` (C# assembly, the actual game logic) |
| PCK file | `Slay the Spire 2.pck` (1.6 GB) |
| Binary | arm64 + x86_64 universal, **symbols NOT stripped** |
| Key deps | `0Harmony 2.4.2` (runtime patching), `MonoMod.Backports`, `GodotSharp 4.5.1`, `Steamworks.NET`, `Sentry`, `FMOD` |
| Version | v0.98.3 (Early Access, playtesters env) |
| Install | `~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/` |
| Logs | `~/Library/Application Support/SlayTheSpire2/logs/godot.log` |

### Critical finding: Harmony + .NET = Easiest modding path

The game ships with **0Harmony.dll** (HarmonyLib) — a .NET runtime patching library widely used for modding. This is the same library used by SMAPI (Stardew Valley), BepInEx, MelonLoader, etc. Combined with the game being **C# on .NET 9**, this means:

- **sts2.dll** can be fully decompiled with ILSpy/dnSpy/ilspycmd to readable C# source
- Harmony patches can intercept any method at runtime (prefix/postfix/transpiler)
- We can inject our own .NET assembly that loads alongside the game

---

## Approaches Ranked (Best → Worst)

### 1. ★★★★★ .NET Assembly Injection + Harmony Patching (RECOMMENDED)

**How:** Write a C# mod DLL. Load it into the game's .NET runtime. Use HarmonyLib to patch game methods — intercept state reads, inject commands.

**Why this is best:**
- The game **already ships Harmony** — it's part of their dependency chain
- `sts2.dll` is a standard .NET assembly → fully decompilable to C# source
- Can read ANY game state by patching getters or hooking into combat/card/event systems
- Can send commands by calling game methods directly (play card, end turn, choose event option)
- No memory offsets, no pointer chains, no binary RE needed
- Survives game updates well (method names/signatures rarely change as much as memory layouts)

**Loading mechanism options:**
1. **BepInEx for Godot** — BepInEx is a modding framework that patches .NET games. There may be a Godot-compatible version or we can adapt it.
2. **Doorstop (Unity Doorstop-style)** — Inject a .NET assembly at CLR startup by setting environment variables (`DOTNET_STARTUP_HOOKS` or similar)
3. **Modify the game's .NET entry point** — Patch the sts2.dll or runtimeconfig to load our assembly
4. **GDExtension** — Write a native GDExtension that bootstraps our C# code

**What we need:**
- Decompile `sts2.dll` to understand class structure (ILSpy)
- Identify key classes: combat state, player, enemies, hand, deck, card play logic, event choices
- Write a mod that exposes a WebSocket/TCP API with JSON game state + accepts commands
- This is essentially building a **CommunicationMod** (like STS1 had) for STS2

**Complexity:** Medium | **Reliability:** Excellent | **Platform:** Cross-platform

### 2. ★★★★☆ Frida Dynamic Instrumentation

**How:** Attach Frida to the running game process. Use JavaScript to hook Godot engine functions and .NET methods. Read scene tree, call functions.

**Why it's good:**
- Binary has **full Godot C++ symbols** (verified via `nm`) — can hook `SceneTree::get_root`, `Object::call`, etc.
- Hot-reload scripts without restarting the game
- Can also hook into the .NET layer via `frida-clr` or by hooking the CLR bridge
- Great for **exploration/prototyping** before building a proper mod

**Limitations:**
- Requires SIP considerations on macOS (Steam games are usually fine)
- Higher overhead than native C# patching
- JavaScript ↔ native bridge can be awkward for complex Godot types

**Complexity:** Medium | **Reliability:** Good | **Platform:** Cross-platform

### 3. ★★★☆☆ Godot Remote Debugger Protocol

**How:** Launch game with `--remote-debug tcp://127.0.0.1:6007`. Connect a custom client that speaks Godot's binary protocol. Inspect scene tree, evaluate expressions.

**Why it could work:**
- Non-invasive (no file modification)
- Can inspect the full scene tree and evaluate GDScript expressions
- Can read/set properties on any node

**Limitations:**
- Game may need to be launched in debug mode (may need to swap binary or set flags)
- The protocol is binary, undocumented, and version-specific
- Sending commands is limited to expression evaluation and property setting
- Need to build a custom protocol client

**Complexity:** High | **Reliability:** Medium | **Platform:** Cross-platform

### 4. ★★★☆☆ PCK Extraction + GDScript Mod Injection

**How:** Extract the .pck file with gdre_tools, add a GDScript autoload that runs a WebSocket server, repack.

**Why it could work:**
- Full access to game internals from GDScript
- Can use `Input.parse_input_event()` for perfect input simulation

**Limitations:**
- Since this is a Mono build, game logic is in C# (sts2.dll), not GDScript
- PCK may contain scenes/resources but the logic is in .NET — GDScript mod would need to call into C# classes
- Breaks on every game update (must re-patch PCK)

**Complexity:** Medium-High | **Reliability:** Medium | **Platform:** Cross-platform

### 5. ★★☆☆☆ OS-Level Input Simulation (PyAutoGUI / CGEvent)

**How:** Send mouse clicks and key presses at the OS level using CGEvent or PyAutoGUI.

**Limitations:**
- Requires game in foreground/focused
- No game state reading (must combine with screenshots or memory reading)
- Fragile — depends on exact window positions, resolution, UI layout
- Slow iteration loop

**Best used as:** Complement to a state-reading approach, not standalone.

### 6. ★☆☆☆☆ Screenshot-Based (Last Resort)

**How:** Capture screenshots, use CV/OCR to read state, use OS input to act.

**Why it's last:**
- Highest latency, lowest reliability
- Enormous engineering effort for CV pipeline
- No access to hidden information (deck contents, draw pile, exact enemy intents)

---

## Recommended Strategy

### Phase 1: Explore (now)
1. Decompile `sts2.dll` with ILSpy to map out game classes
2. Prototype with Frida to quickly explore the running game's state
3. Identify the key classes/methods for combat state, card playing, etc.

### Phase 2: Build the Bridge
1. Write a C# mod DLL that uses Harmony to hook into game state
2. Expose a WebSocket server on localhost with:
   - **GET state**: Returns JSON with full game state (HP, hand, enemies, map, relics, potions, etc.)
   - **POST command**: Accepts commands (play card, end turn, choose event option, etc.)
3. Find a reliable loading mechanism (BepInEx, DOTNET_STARTUP_HOOKS, or similar)

### Phase 3: Build the AI
1. Python client connects to the WebSocket
2. Implements game logic understanding (card synergies, enemy patterns, etc.)
3. Decision engine (could be rule-based, MCTS, or ML)

---

## Sandbox Strategy for Tool Installation

To avoid cluttering userspace, use isolated environments:

### Option A: Nix (recommended for reproducibility)
```bash
# Use nix-shell for ephemeral environments
nix-shell -p dotnet-sdk_9 ilspy frida-tools python3
# Everything disappears when you exit the shell
```

### Option B: Docker container
```bash
# Dockerfile with all tools pre-installed
docker run -v ./:/work -it auto-spire-tools
```

### Option C: Mise/asdf for version management + venv for Python
```bash
# .tool-versions in project root manages runtimes
mise install dotnet 9.0
mise install python 3.12

# Python venv for pip packages
python -m venv .venv
source .venv/bin/activate
pip install frida-tools pyautogui
```

### Option D: Homebrew bundle (least isolated but simplest)
```bash
# Brewfile in project root
brew bundle --file=Brewfile
# Can uninstall later with: brew bundle cleanup --file=Brewfile
```

### Recommended: Nix flake (if you use Nix) or Mise + venv (pragmatic)

For this project specifically, we need:
- **dotnet-sdk 9.0** — to build C# mod DLLs and run ILSpy
- **ilspycmd** — .NET decompiler (installed via `dotnet tool`)
- **frida-tools** — for Frida prototyping (pip package)
- **Python 3.12+** — for the AI client and Frida scripts
- Optionally: **gdre_tools** — for PCK extraction (GitHub release binary)

---

## STS1 Precedent: CommunicationMod

The original Slay the Spire had `CommunicationMod` by ForgottenArbiter:
- Exposed full game state as JSON via stdin/stdout to an external process
- Commands: `PLAY <card> <target>`, `END`, `POTION`, `CHOOSE`, `PROCEED`, `SKIP`, etc.
- Python wrapper: `spirecomm` library
- Multiple bots built on top (MCTS, greedy, neural network)

**Our goal is essentially CommunicationMod for STS2**, but using WebSocket instead of stdin/stdout for better ergonomics.

---

## Next Steps

- [ ] Set up sandboxed dev environment (Mise + venv or Nix)
- [ ] Decompile `sts2.dll` — map out game architecture
- [ ] Prototype Frida script to explore running game state
- [ ] Research BepInEx/GodotSharp modding for .NET Godot games
- [ ] Design the communication protocol (WebSocket JSON API)
- [ ] Build the mod DLL
- [ ] Build the Python client
- [ ] Build the AI decision engine
