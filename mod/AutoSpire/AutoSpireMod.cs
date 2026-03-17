using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Runs;

namespace AutoSpire;

/// <summary>
/// [ModuleInitializer] runs when the assembly is first loaded by the CLR,
/// before ModManager tries to read our PCK. We use this to Harmony-patch
/// ModManager.TryLoadModFromPck so it handles AutoSpire without needing
/// a valid Godot PCK file.
/// </summary>
public static class ModBootstrap
{
    [ModuleInitializer]
    public static void Bootstrap()
    {
        try
        {
            var harmony = new Harmony("auto-spire.bootstrap");

            var target = typeof(ModManager).GetMethod("TryLoadModFromPck",
                BindingFlags.Static | BindingFlags.NonPublic);
            var prefix = typeof(ModBootstrap).GetMethod(nameof(TryLoadModPrefix),
                BindingFlags.Static | BindingFlags.NonPublic);

            if (target != null && prefix != null)
            {
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AutoSpire] Bootstrap error: {ex}");
        }
    }

    /// <summary>
    /// Intercepts ModManager.TryLoadModFromPck for AutoSpire.pck.
    /// Skips the PCK manifest check and directly registers the mod + calls our initializer.
    /// Returns true (skip original) only for our mod; lets all other mods load normally.
    /// </summary>
    private static bool TryLoadModPrefix(string pckFilename, DirAccess dirAccess, ModSource source)
    {
        if (System.IO.Path.GetFileNameWithoutExtension(pckFilename) != "AutoSpire")
            return true; // not our mod, run original

        try
        {
            Log.Info("[AutoSpire] Bootstrap: intercepting mod load for AutoSpire");

            // Register the mod manually (same as ModManager would)
            var modsField = typeof(ModManager).GetField("_mods",
                BindingFlags.Static | BindingFlags.NonPublic);
            var mods = modsField?.GetValue(null) as List<Mod>;

            var mod = new Mod
            {
                pckName = "AutoSpire",
                modSource = source,
                wasLoaded = true,
                assembly = typeof(ModBootstrap).Assembly,
                assemblyLoadedSuccessfully = true,
                manifest = new ModManifest
                {
                    pckName = "AutoSpire",
                    name = "AutoSpire",
                    author = "auto-spire",
                    description = "Automation bridge for Slay the Spire 2",
                    version = "0.1.0"
                }
            };
            mods?.Add(mod);

            // Call our initializer
            AutoSpireMod.Initialize();

            Log.Info("[AutoSpire] Bootstrap: mod registered and initialized successfully");
        }
        catch (Exception ex)
        {
            Log.Error($"[AutoSpire] Bootstrap error: {ex}");
        }

        return false; // skip original TryLoadModFromPck for our mod
    }
}

[ModInitializer("Initialize")]
public static class AutoSpireMod
{
    private static HttpListener? _listener;
    private static CancellationTokenSource? _cts;
    private const int Port = 31452;
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            Log.Info("[AutoSpire] Initializing HTTP server...");
            _cts = new CancellationTokenSource();
            var thread = new Thread(RunServerSync)
            {
                IsBackground = true,
                Name = "AutoSpire-HTTP"
            };
            thread.Start();
            Log.Info($"[AutoSpire] HTTP server started on port {Port}");
        }
        catch (Exception ex)
        {
            Log.Error($"[AutoSpire] Init error: {ex}");
        }
    }

    private static void RunServerSync()
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            Log.Info($"[AutoSpire] Server listening on http://localhost:{Port}/");

            while (!_cts!.IsCancellationRequested)
            {
                try
                {
                    var context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
                }
                catch (HttpListenerException)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[AutoSpire] Server error: {ex}");
        }
    }

    private static void HandleRequest(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            var method = context.Request.HttpMethod;

            object? result = (path, method) switch
            {
                ("/state", "GET") => GetGameState(),
                ("/combat", "GET") => GetCombatState(),
                ("/act", "POST") => HandleAction(context),
                ("/ping", "GET") => new { status = "ok", mod = "AutoSpire" },
                _ => null
            };

            if (result == null)
            {
                Respond(context, 404, new { error = "not_found", path });
                return;
            }

            Respond(context, 200, result);
        }
        catch (Exception ex)
        {
            Log.Error($"[AutoSpire] Request error: {ex}");
            try { Respond(context, 500, new { error = ex.Message }); } catch { }
        }
    }

    private static void Respond(HttpListenerContext context, int status, object body)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(body, SerializerOptions);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        context.Response.OutputStream.Write(json);
        context.Response.Close();
    }

    // --- State Reading ---

    private static object GetGameState()
    {
        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
            return new { phase = "menu", inRun = false };

        var player = LocalContext.GetMe(runState);
        var combatMgr = CombatManager.Instance;

        return new
        {
            phase = combatMgr.IsInProgress ? "combat" : "map",
            inRun = true,
            floor = runState.TotalFloor,
            actIndex = runState.CurrentActIndex,
            actFloor = runState.ActFloor,
            roomType = runState.CurrentRoom?.RoomType.ToString(),
            player = player != null ? SerializePlayer(player) : null,
            combat = combatMgr.IsInProgress ? GetCombatState() : null
        };
    }

    private static object? GetCombatState()
    {
        var combatMgr = CombatManager.Instance;
        if (!combatMgr.IsInProgress)
            return new { inCombat = false };

        var state = combatMgr.DebugOnlyGetState();
        if (state == null)
            return new { inCombat = false };

        var player = LocalContext.GetMe(state);

        return new
        {
            inCombat = true,
            isPlayPhase = combatMgr.IsPlayPhase,
            round = state.RoundNumber,
            currentSide = state.CurrentSide.ToString(),
            player = player != null ? SerializePlayerCombat(player) : null,
            enemies = state.Enemies.Select(SerializeCreature).ToList()
        };
    }

    private static object SerializePlayer(Player player)
    {
        return new
        {
            hp = player.Creature.CurrentHp,
            maxHp = player.Creature.MaxHp,
            block = player.Creature.Block,
            gold = player.Gold,
            character = player.Character?.Id.Entry,
            relics = player.Relics.Select(SerializeRelic).ToList(),
            potions = player.Potions.Select(SerializePotion).ToList(),
            deck = player.Deck.Cards.Select(SerializeCard).ToList()
        };
    }

    private static object SerializePlayerCombat(Player player)
    {
        var pcs = player.PlayerCombatState;
        if (pcs == null)
            return SerializePlayer(player);

        return new
        {
            hp = player.Creature.CurrentHp,
            maxHp = player.Creature.MaxHp,
            block = player.Creature.Block,
            gold = player.Gold,
            energy = pcs.Energy,
            maxEnergy = pcs.MaxEnergy,
            stars = pcs.Stars,
            hand = pcs.Hand.Cards.Select(SerializeCard).ToList(),
            drawPile = pcs.DrawPile.Cards.Select(SerializeCard).ToList(),
            discardPile = pcs.DiscardPile.Cards.Select(SerializeCard).ToList(),
            exhaustPile = pcs.ExhaustPile.Cards.Select(SerializeCard).ToList(),
            powers = player.Creature.Powers.Select(SerializePower).ToList(),
            relics = player.Relics.Select(SerializeRelic).ToList(),
            potions = player.Potions.Select(SerializePotion).ToList(),
            deck = player.Deck.Cards.Select(SerializeCard).ToList()
        };
    }

    private static object SerializeCard(CardModel card)
    {
        return new
        {
            id = card.Id.Entry,
            name = card.GetType().Name,
            title = card.Title?.ToString(),
            type = card.Type.ToString(),
            rarity = card.Rarity.ToString(),
            cost = card.EnergyCost.GetWithModifiers(CostModifiers.All),
            canPlay = card.CanPlay(),
            targetType = card.TargetType.ToString(),
            upgraded = card.IsUpgraded,
            upgradeLevel = card.CurrentUpgradeLevel,
            keywords = card.Keywords.Select(k => k.ToString()).ToList()
        };
    }

    private static object SerializeCreature(Creature creature)
    {
        var result = new Dictionary<string, object?>
        {
            ["combatId"] = creature.CombatId,
            ["name"] = creature.Name,
            ["modelId"] = creature.ModelId.Entry,
            ["hp"] = creature.CurrentHp,
            ["maxHp"] = creature.MaxHp,
            ["block"] = creature.Block,
            ["isAlive"] = creature.IsAlive,
            ["isStunned"] = creature.IsStunned,
            ["powers"] = creature.Powers.Select(SerializePower).ToList()
        };

        if (creature.Monster?.NextMove is { } nextMove)
        {
            var targets = creature.CombatState?.Allies ?? Array.Empty<Creature>();
            result["intent"] = new
            {
                moveId = nextMove.Id,
                intents = nextMove.Intents.Select(i => SerializeIntent(i, targets, creature)).ToList()
            };
        }

        return result;
    }

    private static object SerializeIntent(AbstractIntent intent, IEnumerable<Creature> targets, Creature owner)
    {
        var result = new Dictionary<string, object?>
        {
            ["type"] = intent.IntentType.ToString(),
            ["className"] = intent.GetType().Name
        };

        if (intent is AttackIntent attack)
        {
            try
            {
                result["damage"] = attack.GetSingleDamage(targets, owner);
                result["hits"] = attack.Repeats > 0 ? attack.Repeats : 1;
                result["totalDamage"] = attack.GetTotalDamage(targets, owner);
            }
            catch { /* damage calc may fail outside normal context */ }
        }

        return result;
    }

    private static object SerializePower(PowerModel power)
    {
        return new
        {
            id = power.Id.Entry,
            name = power.GetType().Name,
            type = power.Type.ToString(),
            amount = power.Amount
        };
    }

    private static object SerializeRelic(RelicModel relic)
    {
        return new
        {
            id = relic.Id.Entry,
            name = relic.GetType().Name,
            counter = relic.StackCount
        };
    }

    private static object SerializePotion(PotionModel potion)
    {
        return new
        {
            id = potion.Id.Entry,
            name = potion.GetType().Name,
            targetType = potion.TargetType.ToString(),
            rarity = potion.Rarity.ToString()
        };
    }

    // --- Action Handling ---

    private static object HandleAction(HttpListenerContext context)
    {
        using var reader = new System.IO.StreamReader(context.Request.InputStream);
        var body = reader.ReadToEnd();
        var action = JsonSerializer.Deserialize<ActionRequest>(body, SerializerOptions);

        if (action == null)
            return new { error = "invalid_request" };

        return action.Type switch
        {
            "play_card" => PlayCard(action),
            "end_turn" => EndTurn(),
            "use_potion" => UsePotion(action),
            _ => new { error = "unknown_action", type = action.Type }
        };
    }

    private static object PlayCard(ActionRequest action)
    {
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        if (combatState == null)
            return new { error = "not_in_combat" };

        if (!CombatManager.Instance.IsPlayPhase)
            return new { error = "not_play_phase" };

        var player = LocalContext.GetMe(combatState);
        if (player?.PlayerCombatState == null)
            return new { error = "no_player" };

        var hand = player.PlayerCombatState.Hand.Cards;
        if (action.CardIndex < 0 || action.CardIndex >= hand.Count)
            return new { error = "invalid_card_index", max = hand.Count - 1 };

        var card = hand[action.CardIndex];
        if (!card.CanPlay())
            return new { error = "card_not_playable", cardId = card.Id.Entry };

        Creature? target = null;
        if (action.TargetId.HasValue)
        {
            target = combatState.Enemies
                .FirstOrDefault(e => e.CombatId == action.TargetId.Value);
            if (target == null)
                return new { error = "invalid_target", targetId = action.TargetId };
        }

        _ = CardCmd.AutoPlay(null!, card, target);

        return new { ok = true, played = card.Id.Entry };
    }

    private static object EndTurn()
    {
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        if (combatState == null)
            return new { error = "not_in_combat" };

        if (!CombatManager.Instance.IsPlayPhase)
            return new { error = "not_play_phase" };

        var player = LocalContext.GetMe(combatState);
        if (player == null)
            return new { error = "no_player" };

        PlayerCmd.EndTurn(player, canBackOut: false);
        return new { ok = true };
    }

    private static object UsePotion(ActionRequest action)
    {
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        var player = combatState != null ? LocalContext.GetMe(combatState) : null;

        if (player == null)
        {
            var runState = RunManager.Instance.DebugOnlyGetState();
            if (runState != null)
                player = LocalContext.GetMe(runState);
        }

        if (player == null)
            return new { error = "no_player" };

        var potions = player.Potions.ToList();
        if (action.PotionIndex < 0 || action.PotionIndex >= potions.Count)
            return new { error = "invalid_potion_index", max = potions.Count - 1 };

        var potion = potions[action.PotionIndex];

        Creature? target = null;
        if (action.TargetId.HasValue && combatState != null)
        {
            target = combatState.Enemies
                .FirstOrDefault(e => e.CombatId == action.TargetId.Value);
        }

        potion.EnqueueManualUse(target);
        return new { ok = true, used = potion.Id.Entry };
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public class ActionRequest
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("cardIndex")]
    public int CardIndex { get; set; }

    [JsonPropertyName("potionIndex")]
    public int PotionIndex { get; set; }

    [JsonPropertyName("targetId")]
    public uint? TargetId { get; set; }
}
