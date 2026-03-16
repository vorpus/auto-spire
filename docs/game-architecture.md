# STS2 Game Architecture

What we know about Slay the Spire 2's internals. Update this as we decompile and explore.

## Engine

- Custom Godot 4.5.1 Mono build called "MegaDot v4.5.1.m.8.mono"
- .NET 9.0 runtime (osx-arm64)
- Godot binary has full C++ symbols (not stripped)

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| GodotSharp | 4.5.1 | Godot C# bindings |
| 0Harmony | 2.4.2 | Runtime method patching (modding) |
| MonoMod.Backports | 1.1.2 | Low-level .NET patching utilities |
| Steamworks.NET | 1.0.0 | Steam integration |
| Sentry | 5.0.0 | Error reporting |
| FMOD | - | Audio (native dylib) |
| SmartFormat | 3.3.0 | String formatting/localization |
| Spine | - | 2D skeletal animation (native GDExtension) |

## Game DLL: sts2.dll

The main game logic. C# assembly, fully decompilable with ILSpy.

### Known classes/namespaces

#### AutoSlay — Built-in Automation System (`MegaCrit.Sts2.Core.AutoSlay`)

The game ships with a complete built-in automation/testing framework. See detailed analysis below in [AutoSlay System](#autoslay-system).

#### Core Game Classes (discovered via AutoSlay references)

| Class/Namespace | Purpose |
|---|---|
| `RunManager.Instance` | Singleton managing run state; has `DebugOnlyGetState()` returning `RunState`, `RoomEntered` event |
| `RunState` | Run state: `TotalFloor`, `CurrentRoom`, `CurrentActIndex`, `ActFloor`, `VisitedMapCoords`, `Acts`, `CurrentRoomCount`, `BaseRoom` |
| `CombatManager.Instance` | Singleton for combat: `IsInProgress`, `IsPlayPhase`, `DebugOnlyGetState()` returning `CombatState`, `CheckWinCondition()` |
| `CombatState` | Combat state: `HittableEnemies`, `Enemies`, `PlayerCreatures` |
| `NGame.Instance` | Root game node singleton; has `DebugSeedOverride`, `GetTree()` |
| `SaveManager.Instance` | Saves: `PrefsSave` (has `FastMode`), `SetFtuesEnabled()`, `ObtainEpochOverride()` |
| `NOverlayStack.Instance` | UI overlay stack: `Peek()`, `ScreenCount`, `Remove()` |
| `NMapScreen.Instance` | Map screen: `IsOpen`, `IsVisibleInTree()` |
| `NModalContainer.Instance` | Modal dialogs: `OpenModal` |
| `LocalContext.GetMe(RunState)` | Gets local `Player` from run state |
| `Player` | Player: `Creature`, `Potions`, `HasOpenPotionSlots` |
| `Creature` | Creature: `IsAlive`, `Powers`, `CombatState` |
| `CardModel` | Card: `Id.Entry`, `CanPlay()`, `TargetType`, `CombatState` |
| `PotionModel` | Potion: `Id.Entry`, `TargetType`, `EnqueueManualUse()` |
| `AbstractRoom` | Room: `RoomType` |
| `RoomType` (enum) | `Monster`, `Elite`, `Boss`, `Event`, `Shop`, `Treasure`, `RestSite`, `Unassigned` |
| `PileType` (enum) | Card pile types; `.GetPile(player)` returns `CardPile` |
| `CardPile` | Has `Cards` list of `CardModel` |
| `MapPoint` / `MapCoord` | Map nodes: `coord` (row/col), `Children` |
| `NonInteractiveMode` | Has `AutoSlayerCheck` delegate |

#### Key Command Classes

| Class | Methods |
|---|---|
| `CardCmd` | `AutoPlay(context, card, target)` — plays a card programmatically |
| `PlayerCmd` | `EndTurn(player, canBackOut)` — ends the player's turn |
| `PowerCmd` | `Apply<T>(creature, amount, source, ...)` — applies a power; `Remove(power)` |
| `CreatureCmd` | `Kill(creatures)` — kills creatures directly |
| `CardSelectCmd` | `UseSelector(ICardSelector)` — injects custom card selection logic |

#### Built-in Mod System (`MegaCrit.Sts2.Core.Modding`)

See detailed analysis in [modding-approach.md](modding-approach.md#built-in-mod-loading-system).

#### Dev Console (`MegaCrit.Sts2.Core.DevConsole`)

See detailed analysis in [modding-approach.md](modding-approach.md#dev-console-commands).

#### Hooks System (`MegaCrit.Sts2.Core.Hooks`)

See detailed analysis in [modding-approach.md](modding-approach.md#hooks-system).

#### Debug Utilities (`MegaCrit.Sts2.Core.Debug`)

| Class | Purpose |
|---|---|
| `DebugSettings` | `DevSkip` reads `STS2_DEV_SKIP` env var; `IgnorePackedImages` always false |
| `DebugHotkey` | StringName constants for debug hotkeys (hide UI elements, speed controls, unlock chars) |
| `DebugActMap` | Test node for generating random act maps |
| `SentryService` | Error reporting; disables when mods loaded; `AttachGameState()` dumps rich game state |
| `ReleaseInfo/ReleaseInfoManager` | Build version/branch/commit/date tracking |

### Key systems to map

- [x] Combat state management — `CombatManager`, `CombatState`, play phase polling (see AutoSlay)
- [x] Card data model — `CardModel`, `CardPile`, `PileType`, `TargetType`
- [x] Player state — `Player`, `Creature`, `Potions`, `HasOpenPotionSlots`
- [x] Event/choice system — `NEventOptionButton`, `Option.IsLocked`, `Option.IsProceed`
- [x] Map/pathing system — `MapPoint`, `MapCoord`, `VisitedMapCoords`, `NMapPoint`
- [x] Modding system — `ModManager`, `ModManifest`, PCK+DLL loading, Harmony auto-patching
- [x] Dev console — 39 built-in commands, extensible via mods
- [x] Hooks system — 60+ game event hooks on `AbstractModel`
- [x] Enemy data model — `MonsterModel`, `MoveState`, `AbstractIntent`, `AttackIntent`
- [x] Core entity models — full property-level analysis of all game entities
- [ ] Save/serialization (how state is persisted)

## AutoSlay System

### Overview

AutoSlay is MegaCrit's **built-in automation/QA testing framework** that can play complete runs autonomously. It is a fully async, polling-based system that navigates the main menu, selects a character, plays through all rooms floor-by-floor, handles every screen/overlay, and quits the game when done.

### Architecture

**Pattern**: Async polling loop with handler dispatch.

The core loop in `AutoSlayer.PlayRunAsync()`:
1. Wait for game init, configure settings (fast mode, unlock all characters, set seed)
2. Navigate main menu: abandon existing run if needed, click Singleplayer, pick random character
3. Main floor loop (`while TotalFloor < 49`):
   - Get current room type from `RunState.CurrentRoom.RoomType`
   - Dispatch to appropriate `IRoomHandler`
   - After combat rooms: wait for rewards screen
   - Drain all overlay screens via `DrainOverlayScreensAsync()` (dispatches to `IScreenHandler`s)
   - Handle special post-room logic (rest site proceed, event proceed, boss act transitions)
   - Navigate map via `MapScreenHandler`

**No event loop or signals for coordination** — it uses `async/await` with `WaitHelper.Until()` polling at 100ms intervals. The one exception is `MapScreenHandler` which subscribes to `RunManager.Instance.RoomEntered` event.

### Activation

- `AutoSlayer.Start(seed, logFile)` — starts an async run
- Sets `IsActive = true`, `NonInteractiveMode.AutoSlayerCheck` returns `IsActive`
- Sets `SaveManager.Instance.PrefsSave.FastMode = FastModeType.Fast`
- Disables FTUEs, unlocks all epochs (characters)
- Uses a deterministic `Rng` seeded from the seed string
- On completion/failure, calls `NGame.Instance.GetTree().Quit(exitCode)`

### How it reads game state

- **Run state**: `RunManager.Instance.DebugOnlyGetState()` returns `RunState` with floor, act, room, visited coords
- **Combat state**: `CombatManager.Instance.IsInProgress`, `IsPlayPhase`, `DebugOnlyGetState()` for enemies/creatures
- **UI state**: Polls Godot scene tree nodes by absolute path (e.g. `/root/Game/RootSceneContainer/Run/RoomContainer/EventRoom`)
- **Overlay stack**: `NOverlayStack.Instance.Peek()` and `ScreenCount`
- **Buttons/options**: `UiHelper.FindAll<T>(node)` recursive search, then checks `IsEnabled`, `Visible`, `IsLocked`

### How it sends commands/actions

Two distinct mechanisms:

1. **Direct game commands** (bypasses UI):
   - `CardCmd.AutoPlay(context, card, target)` — plays cards
   - `PlayerCmd.EndTurn(player, canBackOut)` — ends turn
   - `PowerCmd.Apply<T>(creature, amount, source)` — applies buffs (999 Plating, 999 Regen for invincibility)
   - `CreatureCmd.Kill(creatures)` — kills enemies directly
   - `PotionModel.EnqueueManualUse(target)` — uses potions

2. **UI simulation** (clicks buttons):
   - `UiHelper.Click(button)` calls `button.ForceClick()` (not mouse simulation)
   - `EmitSignal(SignalName.Pressed, ...)` / `EmitSignal(SignalName.Released, ...)` for card holders and clickable controls
   - Node path navigation: `root.GetNode<T>(path)`, `GetNodeOrNull<T>(path)`

### Combat Strategy

The combat handler is **not intelligent** — it's a QA stress-test tool:
- Applies 999 Plating + 999 Regen at combat start (effectively invincible)
- Uses all potions at start of each turn
- Plays random playable cards at random targets
- Ends turn when no more playable cards remain
- For event combat: also applies 100 Strength and directly kills enemies via `CreatureCmd.Kill()`

### Handler Registry

**Room Handlers** (`IRoomHandler` — dispatched by `RoomType`):
| RoomType | Handler | Strategy |
|---|---|---|
| Monster, Elite, Boss | `CombatRoomHandler` | Apply buffs, play random cards |
| Event | `EventRoomHandler` | Click random unlocked options, handle Ancient dialogue, FakeMerchant |
| Shop | `ShopRoomHandler` | Buy all affordable items randomly (except card removal) |
| Treasure | `TreasureRoomHandler` | Open chest, pick up all relics |
| RestSite | `RestSiteRoomHandler` | Pick random enabled option |

**Screen Handlers** (`IScreenHandler` — dispatched by `Type`):
| Screen Type | Handler | Strategy |
|---|---|---|
| `NRewardsScreen` | `RewardsScreenHandler` | Click all reward buttons (skip potions if full) |
| `NCardRewardSelectionScreen` | `CardRewardScreenHandler` | Pick random card |
| `NDeckUpgradeSelectScreen` | `DeckUpgradeScreenHandler` | Upgrade random card |
| `NDeckTransformSelectScreen` | `DeckTransformScreenHandler` | Transform random card |
| `NDeckEnchantSelectScreen` | `DeckEnchantScreenHandler` | Enchant random card |
| `NDeckCardSelectScreen` | `DeckCardSelectScreenHandler` | Select random cards |
| `NSimpleCardSelectScreen` | `SimpleCardSelectScreenHandler` | Select random cards |
| `NChooseACardSelectionScreen` | `ChooseACardScreenHandler` | Pick random card |
| `NChooseABundleSelectionScreen` | `ChooseABundleScreenHandler` | Pick random bundle |
| `NChooseARelicSelection` | `ChooseARelicScreenHandler` | Pick random relic |
| `NGameOverScreen` | `GameOverScreenHandler` | Click continue, then return to main menu |
| `NCrystalSphereScreen` | `CrystalSphereScreenHandler` | Click random hidden cells, then proceed |

### Helper Classes

- **`UiHelper`**: `Click(button)` via `ForceClick()`, `FindAll<T>(node)` recursive node search, `FindFirst<T>(node)`
- **`WaitHelper`**: `Until(condition, ct, timeout)` polls at 100ms, `ForNode<T>(root, path)` waits for node existence+visibility+enabled, `WithTimeout()` wraps async operations, `ForTask()` waits for task completion. Includes `DumpSceneTreeContext()` for debugging.
- **`Watchdog`**: Tracks last activity time, throws `AutoSlayTimeoutException` if no progress for 30s, logs warnings every 5s
- **`AutoSlayCardSelector`**: Implements `ICardSelector` for random card selection when game prompts for card choices (injected via `CardSelectCmd.UseSelector()`)

### Timeouts (from `AutoSlayConfig`)

| Config | Value |
|---|---|
| `runTimeout` | 25 minutes |
| `defaultRoomTimeout` | 2 minutes |
| `defaultScreenTimeout` | 30 seconds |
| `gameInitTimeout` | 10 seconds |
| `pollingInterval` | 100ms |
| `buttonClickDelay` | 100ms |
| `maxFloor` | 49 |
| `watchdogTimeout` | 30 seconds |

## File structure

### PCK contents

_TODO: Extract and document after running gdre_tools or similar_

### Save files

Located at `~/Library/Application Support/SlayTheSpire2/steam/<steam_id>/`

- `profile.save` / `profile.save.backup`
- `settings.save` / `settings.save.backup`
- Per-profile directory (e.g. `profile1/`)

### Logs

`~/Library/Application Support/SlayTheSpire2/logs/godot.log`

Useful log lines observed:
- AtlasManager loads: `card_atlas` (840 sprites), `relic_atlas` (336), `power_atlas` (268), `potion_atlas` (63), `intent_atlas` (310)
- ModelIdSerializationCache: 19 categories, 1597 entries, 57 epochs
- Save system: PrefsSave v2, ProfileSave v2, SerializableProgress v21, RunHistory v8, SerializableRun v14, SettingsSave v4
