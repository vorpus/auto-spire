# Modding Approach

## Chosen strategy: .NET Harmony patching

Since STS2 ships with HarmonyLib and runs on .NET 9, we can write a C# mod DLL that patches game methods at runtime. This is the same technique used by BepInEx, SMAPI, and most .NET game modding frameworks.

## Goal: CommunicationMod for STS2

Build a mod that:
1. Hooks into game state (combat, cards, enemies, events, map)
2. Exposes state as JSON over a local WebSocket server
3. Accepts commands (play card, end turn, choose event option, etc.)
4. An external Python client connects and drives the game

## Built-in Mod Loading System

**STS2 has a complete, official mod loading system.** No need for BepInEx or startup hooks.

### How it works (`ModManager.Initialize()`)

1. **Mod directory scanning**: Looks for `.pck` files recursively in `<game_dir>/mods/`
2. **Steam Workshop**: Also loads subscribed Workshop items (app ID `2868840`)
3. **For each `.pck` file found**:
   - Checks if the player has agreed to mod loading (`PlayerAgreedToModLoading` in settings)
   - Checks if the mod is disabled in settings
   - Loads a companion DLL with the same name (e.g. `mymod.pck` + `mymod.dll`)
   - DLL is loaded via `AssemblyLoadContext.LoadFromAssemblyPath()`
   - Loads the PCK into Godot via `ProjectSettings.LoadResourcePack()`
   - Reads `res://mod_manifest.json` from the PCK
   - If the DLL has classes with `[ModInitializer("MethodName")]` attribute, calls those static methods
   - If NO `ModInitializerAttribute` found, falls back to `Harmony.PatchAll(assembly)` automatically
4. **Assembly resolution**: `HandleAssemblyResolveFailure()` automatically resolves references to `sts2` and `0Harmony` assemblies

### Mod Manifest Format (`mod_manifest.json`)

```json
{
  "pck_name": "mymod",
  "name": "My Mod Display Name",
  "author": "AuthorName",
  "description": "What the mod does",
  "version": "1.0.0"
}
```

### Key Classes

| Class | Purpose |
|---|---|
| `ModManager` | Static class, loads/manages all mods, fires `OnModDetected` event |
| `Mod` | Mod instance: `pckName`, `modSource`, `wasLoaded`, `manifest`, `assembly` |
| `ModManifest` | JSON-deserialized manifest with name, author, version, etc. |
| `ModInitializerAttribute` | `[ModInitializer("Init")]` on a class — specifies static init method to call |
| `ModHelper` | `AddModelToPool<TPoolType, TModelType>()` — lets mods add content to game pools |
| `ModSettings` | Per-mod enable/disable tracking in player settings |
| `ModSource` | Enum: `None`, `ModsDirectory`, `SteamWorkshop` |
| `DisabledMod` | Serializable record of disabled mod name + source |

### Loading Approaches for Our Mod

The simplest approach:
1. Create `mymod.pck` (can be minimal/empty Godot PCK with just `mod_manifest.json`)
2. Create `mymod.dll` (our C# mod assembly referencing `sts2.dll` and `0Harmony.dll`)
3. Place both in `<game_dir>/mods/`
4. Either use `[ModInitializer]` for custom init, or let Harmony auto-patch

The game will auto-resolve references to `sts2` and `0Harmony`, so our DLL only needs to reference those.

### Mod Console Commands

The `DevConsole` constructor includes: `ReflectionHelper.GetSubtypesInMods<AbstractConsoleCmd>()` — meaning **mods can register their own console commands** by simply subclassing `AbstractConsoleCmd` in their assembly.

### Command Line Args

- `--nomods` — skips mod initialization entirely

## Loading mechanism (RESOLVED)

Use the built-in mod system. No external tools needed.

- [x] Built-in mod system via `<game_dir>/mods/` directory (PCK + DLL)
- [ ] ~~`DOTNET_STARTUP_HOOKS` environment variable~~ (not needed)
- [ ] ~~BepInEx for Godot/.NET~~ (not needed)
- [ ] ~~Patching sts2.runtimeconfig.json~~ (not needed)
- [ ] ~~GDExtension that bootstraps C# code~~ (not needed)
- [ ] ~~Modifying the game's entry point~~ (not needed)

## Harmony patching basics

```csharp
// Prefix: runs before the original method
[HarmonyPatch(typeof(SomeGameClass), "SomeMethod")]
class MyPatch {
    static void Prefix(SomeGameClass __instance) {
        // read state from __instance
    }

    static void Postfix(ref ReturnType __result) {
        // modify return value or react to method completion
    }
}
```

## Alternative tools explored

See `docs/research.md` for full analysis. Summary:
- Frida: good for prototyping, can attach to running game
- Godot remote debugger: possible but protocol is undocumented
- PCK modding: game logic is in C# not GDScript, so limited value
- OS-level input: fallback only, no state reading
- Screenshots: last resort

## Dev Console Commands

The game has 39 built-in console commands. Most are debug-only (not available in release builds) but `open`, `cloud`, `log`, and `getlogs` are available in release. The `DevConsole` constructor also loads subtypes from mods, so **our mod can add custom commands**.

### Command Reference

| Command | Args | Description | Networked | Debug-Only |
|---|---|---|---|---|
| `achievement` | `<unlock\|revoke\|check> [id]` | Unlock/revoke achievements | No | Yes |
| `act` | `<int\|string>` | Jump to act by index or replace current act | Yes | Yes |
| `afflict` | `<id> [amount] [hand-index]` | Apply affliction to hand card | Yes | Yes |
| `ancient` | `<id> <choice>` | Open ancient event with forced choice | Yes | Yes |
| `art` | `<type>` | List content missing art (affliction/card/enchantment/power/relic) | No | Yes |
| `block` | `<amount> [target-index]` | Give block to creature (0=player) | Yes | Yes |
| `card` | `<card-id> [pile]` | Spawn card into pile (hand default). SCREAMING_SNAKE_CASE | Yes | Yes |
| `cloud` | `delete` | Delete all Steam cloud saves | No | No |
| `damage` | `<amount> [target-index]` | Damage enemies or specific creature | Yes | Yes |
| `die` | | Kill the player | Yes | Yes |
| `draw` | `[count]` | Draw cards (default 1) | Yes | Yes |
| `dump` | | Dump Model ID database to logs | No | Yes |
| `enchant` | `<id> [amount] [hand-index]` | Enchant a hand card | Yes | Yes |
| `energy` | `<amount>` | Add energy to player | Yes | Yes |
| `event` | `<id>` | Jump to specific event | Yes | Yes |
| `fight` | `<id>` | Jump to specific encounter | Yes | Yes |
| `getlogs` | `[name]` | Zip logs and open directory | No | No |
| `godmode` | | Toggle invincibility (9999 Strength/Buffer/Regen) | Yes | Yes |
| `gold` | `<amount>` | Add/remove gold | Yes | Yes |
| `heal` | `<amount> [index]` | Heal player or ally | Yes | Yes |
| `instant` | | Toggle instant animation mode | No | Yes |
| `kill` | `[index\|all]` | Kill enemies | Yes | Yes |
| `leaderboard` | `[upload\|random] ...` | Manipulate leaderboard scores | No | Yes |
| `log` | `[type] <level>` | Set log level for log types | No | No |
| `log-history` | | Save command history to file | No | Yes |
| `multiplayer` | `[test]` | Open multiplayer menu or test scene | No | Yes |
| `open` | `logs\|saves\|root\|build-logs\|loc-override` | Open directory in file manager | No | No |
| `potion` | `<id>` | Add potion to belt. SCREAMING_SNAKE_CASE | Yes | Yes |
| `power` | `<id> <amount> <target-index>` | Apply power to creature | Yes | Yes |
| `relic` | `[add\|remove] <id>` | Add or remove relic | Yes | Yes |
| `remove_card` | `<id> [pile]` | Remove card from hand or deck | Yes | Yes |
| `room` | `<RoomType>` | Jump to room type | Yes | Yes |
| `sentry` | `<test\|message\|exception\|crash\|status>` | Test Sentry error reporting | No | Yes |
| `stars` | `<amount>` | Add stars (in combat) | Yes | Yes |
| `trailer` | | Toggle trailer mode (show/hide UI via hotkeys) | No | Yes |
| `travel` | | Toggle free map travel | Yes | Yes |
| `unlock` | `<cards\|potions\|relics\|monsters\|events\|epochs\|ascensions\|all>` | Mark content as discovered | No | Yes |
| `upgrade` | `[hand-index]` | Upgrade card in hand | Yes | Yes |
| `win` | | Win current combat (kill all enemies) | Yes | Yes |

### Key APIs Used by Console Commands

These show what programmatic operations the game supports:
- `ModelDb.AllCards`, `AllRelics`, `AllPotions`, `AllEncounters`, `AllEvents`, `AllAncients`, `AllPowers`, `Monsters`, `Acts`, `AllCharacters` — access to all game content
- `RunManager.Instance.EnterRoomDebug(roomType)`, `EnterRoom(room)`, `EnterAct(index)` — room/act navigation
- `CombatManager.Instance.DebugOnlyGetState()` — combat state access
- `CombatManager.Instance.CheckWinCondition()` — trigger win check
- `PileType.Hand.GetPile(player).Cards` — hand inspection
- `CardCmd.Upgrade(card)`, `CardCmd.Enchant(enchantment, card, amount)`, `CardCmd.Afflict(affliction, card, amount)` — card manipulation
- `CreatureCmd.Kill(creature)`, `CreatureCmd.Heal(creature, amount)`, `CreatureCmd.Damage(...)`, `CreatureCmd.GainBlock(...)` — creature manipulation
- `PlayerCmd.GainGold(amount, player)`, `PlayerCmd.GainEnergy(amount, player)`, `PlayerCmd.GainStars(amount, player)` — player resource manipulation
- `PotionCmd.TryToProcure(potion, player)` — add potions
- `RelicCmd.Obtain(relic, player)`, `RelicCmd.Remove(relic)` — relic manipulation
- `CardPileCmd.Add(card, pile)`, `CardPileCmd.Draw(context, count, player)`, `CardPileCmd.RemoveFromCombat(card)`, `CardPileCmd.RemoveFromDeck(card)` — card pile manipulation
- `PowerCmd.Apply(power, creature, amount)`, `PowerCmd.ModifyAmount(power, amount)`, `PowerCmd.Remove<T>(creature)` — power manipulation
- `SaveManager.Instance.PrefsSave.FastMode = FastModeType.Instant` — animation speed control

## Hooks System

The `Hook` static class (`MegaCrit.Sts2.Core.Hooks`) dispatches 60+ game events to all `AbstractModel` instances that are registered as "hook listeners" in the current run/combat state. These are **not modding hooks in the Harmony sense** — they are the game's internal event system used by relics, powers, and cards to react to game events.

### How Hooks Work

Each hook method:
1. Iterates `runState.IterateHookListeners(combatState)` or `combatState.IterateHookListeners()`
2. For each `AbstractModel` listener, calls the corresponding virtual method
3. Calls `model.InvokeExecutionFinished()` after each listener

The listeners are `AbstractModel` subclasses (relics, powers, cards, etc.) that override the virtual hook methods. They are **not extensible through a subscribe/unsubscribe API** — the iteration order comes from the game's internal model registration.

### Hook Categories

**Combat Flow**: `BeforeCombatStart`/Late, `AfterCombatEnd`, `AfterCombatVictory`/Early, `BeforePlayPhaseStart`, `AfterPlayerTurnStart`/Early/Late, `BeforeSideTurnStart`, `AfterSideTurnStart`, `BeforeTurnEnd`/VeryEarly/Late, `AfterTakingExtraTurn`

**Cards**: `BeforeCardPlayed`, `AfterCardPlayed`/Late, `BeforeCardAutoPlayed`, `AfterCardDrawn`/Early, `AfterCardDiscarded`, `AfterCardExhausted`, `AfterCardRetained`, `AfterCardChangedPiles`/Late, `AfterCardEnteredCombat`, `AfterCardGeneratedForCombat`, `BeforeCardRemoved`

**Damage/Block**: `BeforeAttack`, `AfterAttack`, `BeforeDamageReceived`, `AfterDamageReceived`/Late, `AfterDamageGiven`, `BeforeBlockGained`, `AfterBlockGained`, `AfterBlockBroken`, `AfterBlockCleared`

**Creatures**: `AfterCreatureAddedToCombat`, `AfterCurrentHpChanged`, `BeforeDeath`, `AfterDeath`, `AfterDiedToDoom`

**Powers**: `BeforePowerAmountChanged`, `AfterPowerAmountChanged`

**Resources**: `AfterEnergyReset`/Late, `AfterEnergySpent`, `AfterGoldGained`, `AfterStarsGained`, `AfterStarsSpent`, `AfterForge`

**Cards/Hand**: `BeforeHandDraw`/Late, `AfterHandEmptied`, `AfterShuffle`, `BeforeFlush`/Late

**Orbs**: `AfterOrbChanneled`, `AfterOrbEvoked`

**Potions**: `AfterPotionDiscarded`, `AfterPotionProcured`, `BeforePotionUsed`, `AfterPotionUsed`

**Map/Run**: `AfterActEntered`, `BeforeRoomEntered`, `AfterRoomEntered`, `AfterMapGenerated`, `AfterItemPurchased`, `BeforeRewardsOffered`, `AfterRewardTaken`, `AfterRestSiteHeal`, `AfterRestSiteSmith`

**Modifiers**: `AfterModifyingBlockAmount`, `AfterModifyingCardPlayCount`, `AfterModifyingCardRewardOptions`, `AfterModifyingDamageAmount`, `AfterModifyingHandDraw`, `AfterModifyingOrbPassiveTriggerCount`, `AfterModifyingPowerAmountGiven`/Received, `AfterModifyingRewards`, `AfterModifyingHpLostBeforeOsty`, `AfterModifyingHpLostAfterOsty`

**Prevention**: `AfterPreventingBlockClear`, `AfterPreventingDeath`, `AfterPreventingDraw`

**Other**: `AfterSummon`, `AfterOstyRevived`

### Modding Relevance

While we cannot directly add hook listeners through this system (they require being registered `AbstractModel` instances in the game state), we can:
1. **Harmony-patch the `Hook` methods** to intercept any game event
2. **Harmony-patch `AbstractModel` virtual methods** to add behavior to existing game objects
3. Use the hooks as a reference for what events the game fires and when

### ModifyDamageHookType

Flags enum: `None=0`, `Additive=2`, `Multiplicative=4`, `All=6`

## Mod API Summary

The game provides these extension points for mods:
1. **PCK + DLL loading** — drop files in `mods/` directory
2. **`[ModInitializer]` attribute** — custom initialization entry point
3. **Auto Harmony patching** — fallback if no initializer found
4. **`ModHelper.AddModelToPool()`** — add custom cards/relics/etc. to game pools
5. **Custom console commands** — subclass `AbstractConsoleCmd` in mod assembly
6. **Modded localization tables** — place in `res://<pck_name>/localization/<lang>/<file>`
7. **`ModManager.OnMetricsUpload`** — hook into run metrics

## Debug Features

- **`STS2_DEV_SKIP`** env var: enables `DebugSettings.DevSkip`
- **Debug hotkeys**: hide/show individual UI elements (combat UI, hand, HP bars, intents, etc.), speed controls, unlock characters
- **`shouldAllowDebugCommands`**: flag in `DevConsole` constructor controls whether debug-only commands are available
- **`FastModeType.Instant`**: skip all animations

## Progress log

_Update this as we make progress on the mod._
