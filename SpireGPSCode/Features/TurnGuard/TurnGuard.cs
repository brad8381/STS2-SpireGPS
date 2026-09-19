using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;
using SpireGPS.Compatibility;
using SpireGPS.Config;
using SpireGPS.UI;

namespace SpireGPS.Features.TurnGuard;

internal static class TurnGuardService
{
    private static readonly System.Reflection.FieldInfo? CombatStateField =
        AccessTools.Field(typeof(NEndTurnButton), "_combatState");

    private static bool _lastReady;
    private static int _lastEnergy;
    private static int _lastHandHash;
    private static bool _initialized;

    internal static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        CombatManager.Instance.StateTracker.CombatStateChanged += OnCombatStateChanged;
        CombatManager.Instance.TurnStarted += _ => ResetSnapshot();
    }

    internal static Player? GetLocalPlayer(NEndTurnButton button)
    {
        var state = CombatStateField?.GetValue(button) as CombatState;
        return LocalContext.GetMe(state);
    }

    internal static IReadOnlyList<string> GetWarnings(Player player)
    {
        var warnings = new List<string>();
        var pcs = player.PlayerCombatState;
        if (pcs is null)
            return warnings;

        var livingEnemies = player.Creature.CombatState?.Enemies
            .Where(e => e.IsAlive)
            .ToArray() ?? Array.Empty<Creature>();

        // If every living enemy is guaranteed to die at the start of its turn
        // before it can act, ending the turn is already the sensible action.
        if (livingEnemies.Length > 0 && livingEnemies.All(WillDieBeforeActing))
            return warnings;

        bool hasPlayableCards = pcs.HasCardsToPlay();

        if (SpireGpsSettings.TurnGuardWarnPlayableCards && hasPlayableCards)
            warnings.Add("playable cards remain");

        // Energy on its own is not actionable. Only mention it when there is
        // still a card the player can actually play.
        if (SpireGpsSettings.TurnGuardWarnEnergy && hasPlayableCards && pcs.Energy > 0)
            warnings.Add($"{pcs.Energy} energy remains");

        if (SpireGpsSettings.TurnGuardWarnLethal && IsLethalIncoming(player, out int incoming))
            warnings.Add($"{incoming} incoming damage may be lethal");

        return warnings;
    }

    private static bool IsLethalIncoming(Player player, out int incoming)
    {
        incoming = 0;
        var state = player.Creature.CombatState;
        if (state is null || state.Players.Count != 1)
            return false;

        try
        {
            var targets = state.PlayerCreatures;
            foreach (var enemy in state.Enemies.Where(e => e.IsAlive))
            {
                // Poison/Plague lethal at turn start means this enemy never
                // reaches its move, so its intent should not count as incoming.
                if (WillDieBeforeActing(enemy))
                    continue;

                var monster = enemy.Monster;
                if (monster is null) continue;

                foreach (var intent in monster.NextMove.Intents)
                {
                    if (intent is AttackIntent attack)
                        incoming += attack.GetTotalDamage(targets, enemy);
                }
            }

            decimal effectiveHp = player.Creature.CurrentHp + player.Creature.Block;
            return incoming > 0 && incoming >= effectiveHp;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Turn Guard lethal check skipped: {ex.Message}");
            incoming = 0;
            return false;
        }
    }

    private static bool WillDieBeforeActing(Creature enemy)
    {
        try
        {
            var poison = enemy.GetPower<PoisonPower>();
            if (poison is not null && poison.CalculateTotalDamageNextTurn() >= enemy.CurrentHp)
                return true;

            // Optional compatibility with The Plaguebringer without taking a
            // compile-time dependency on that mod. Plague ticks at the start
            // of the affected creature's turn and ignores Block.
            var plague = enemy.Powers.FirstOrDefault(power =>
                power.GetType().FullName == "PB.Powers.PlaguePower" ||
                (power.GetType().Name == "PlaguePower" &&
                 string.Equals(power.GetType().Assembly.GetName().Name, "PlagueBringer", StringComparison.OrdinalIgnoreCase)));

            if (plague is not null && plague.Amount >= enemy.CurrentHp)
                return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Turn Guard automatic-kill check skipped: {ex.Message}");
        }

        return false;
    }

    private static void OnCombatStateChanged(CombatState state)
    {
        if (!SpireGpsSettings.TurnGuardEnabled || !SpireGpsSettings.TurnGuardAutoUnready)
        {
            ResetSnapshot();
            return;
        }

        var me = LocalContext.GetMe(state);
        if (me is null || state.Players.Count < 2)
        {
            ResetSnapshot();
            return;
        }

        var pcs = me.PlayerCombatState;
        if (pcs is null)
        {
            ResetSnapshot();
            return;
        }

        bool ready = CombatManager.Instance.IsPlayerReadyToEndTurn(me);
        int energy = pcs.Energy;
        int handHash = CalculateHandHash(me);

        if (ready && _lastReady && !CombatManager.Instance.AllPlayersReadyToEndTurn() &&
            (energy != _lastEnergy || handHash != _lastHandHash))
        {
            MainFile.Logger.Info("Turn Guard: hand/energy changed after ready; undoing end turn.");
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(
                new UndoEndPlayerTurnAction(me, state.RoundNumber));
            SpireGpsToast.Show("Turn Guard: hand or energy changed - unreadying you.");
            ready = false;
        }

        _lastReady = ready;
        _lastEnergy = energy;
        _lastHandHash = handHash;
    }

    private static int CalculateHandHash(Player player)
    {
        unchecked
        {
            int hash = 17;
            var pcs = player.PlayerCombatState;
            var hand = pcs?.Hand;
            if (hand is null)
                return hash;

            foreach (var card in hand.Cards)
                hash = hash * 31 + RuntimeHelpers.GetHashCode(card);
            return hash;
        }
    }

    private static void ResetSnapshot()
    {
        _lastReady = false;
        _lastEnergy = 0;
        _lastHandHash = 0;
    }
}

[HarmonyPatch(typeof(NEndTurnButton), nameof(NEndTurnButton.CallReleaseLogic))]
internal static class EndTurnGuardPatch
{
    private static string? _armedSignature;
    private static ulong _armedUntil;

    private static bool Prefix(NEndTurnButton __instance)
    {
        if (!SpireGpsSettings.TurnGuardEnabled || CompatibilityManager.ShouldYieldTurnGuard())
            return true;

        try
        {
            var me = TurnGuardService.GetLocalPlayer(__instance);
            if (me is null)
                return true;

            // Never guard the game's native "undo end turn" action.
            if (CombatManager.Instance.IsPlayerReadyToEndTurn(me))
                return true;

            var warnings = TurnGuardService.GetWarnings(me);
            if (warnings.Count == 0)
            {
                Disarm();
                return true;
            }

            string signature = string.Join("|", warnings);
            ulong now = Time.GetTicksMsec();
            if (_armedSignature == signature && now <= _armedUntil)
            {
                Disarm();
                return true;
            }

            _armedSignature = signature;
            _armedUntil = now + (ulong)(Math.Max(0.5f, SpireGpsSettings.TurnGuardConfirmSeconds) * 1000f);

            SpireGpsToast.Show(
                "Turn Guard: " + string.Join(" • ", warnings) + ". Click End Turn again to confirm.",
                SpireGpsSettings.TurnGuardConfirmSeconds);
            return false;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Turn Guard failed open: {ex}");
            return true;
        }
    }

    private static void Disarm()
    {
        _armedSignature = null;
        _armedUntil = 0;
    }
}
