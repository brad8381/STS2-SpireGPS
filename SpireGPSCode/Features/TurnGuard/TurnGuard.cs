using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Orbs;
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

        var state = player.Creature.CombatState;
        if (state is null)
            return warnings;

        // A temporarily downed / reviving enemy (Test Subject is the obvious
        // example) can remain in the combat state while not being hittable.
        // There is nothing useful Turn Guard can protect at that moment, so
        // do not block the game's End Turn flow.
        var actionableEnemies = state.HittableEnemies
            .Where(enemy => enemy.IsAlive)
            .ToArray();

        if (actionableEnemies.Length == 0)
            return warnings;

        // If deterministic effects that resolve before the enemies act will
        // kill every currently actionable enemy, ending the turn is already
        // safe. This includes Poison/Plague and guaranteed end-turn orb damage.
        if (actionableEnemies.All(enemy =>
                WillDieBeforeActing(player, enemy, actionableEnemies.Length)))
        {
            return warnings;
        }

        var playableCards = pcs.Hand.Cards
            .Where(card =>
            {
                try { return card.CanPlay(); }
                catch { return false; }
            })
            .ToArray();

        bool hasPlayableCards = playableCards.Length > 0;

        // Tainted cards are still technically playable, but in the Infested
        // Prism fight the player may intentionally end the turn rather than
        // take the Tainted attack-damage penalty. Treat an all-Tainted
        // playable hand as non-actionable for the playable-card reminder only.
        if (hasPlayableCards &&
            SpireGpsSettings.TurnGuardIgnoreAllTainted &&
            playableCards.All(IsTainted))
        {
            hasPlayableCards = false;
        }

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

    private static bool IsTainted(MegaCrit.Sts2.Core.Models.CardModel card)
    {
        try
        {
            var affliction = card.Affliction;
            if (affliction is null)
                return false;

            return string.Equals(
                       affliction.GetType().Name,
                       "Tainted",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       affliction.Id.Entry,
                       "TAINTED",
                       StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
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
            var actionableEnemies = state.HittableEnemies
                .Where(enemy => enemy.IsAlive)
                .ToArray();

            foreach (var enemy in actionableEnemies)
            {
                // Deterministic end-turn/start-of-enemy-turn damage can remove
                // this enemy before its attack. Do not report its intent as
                // lethal incoming damage in that case.
                if (WillDieBeforeActing(player, enemy, actionableEnemies.Length))
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

    private static bool WillDieBeforeActing(
        Player player,
        Creature enemy,
        int actionableEnemyCount)
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
                 string.Equals(
                     power.GetType().Assembly.GetName().Name,
                     "PlagueBringer",
                     StringComparison.OrdinalIgnoreCase)));

            if (plague is not null && plague.Amount >= enemy.CurrentHp)
                return true;

            decimal guaranteedOrbDamage =
                GetGuaranteedEndTurnOrbDamage(player, enemy, actionableEnemyCount);

            if (guaranteedOrbDamage <= 0m)
                return false;

            // Orb damage is normal damage and can be absorbed by current Block.
            // Keep this conservative: if the enemy has Intangible, do not try
            // to predict the per-hit caps here.
            if (enemy.GetPower<IntangiblePower>() is not null)
                return false;

            decimal effectiveEnemyHp = enemy.CurrentHp + enemy.Block;
            return guaranteedOrbDamage >= effectiveEnemyHp;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Turn Guard automatic-kill check skipped: {ex.Message}");
        }

        return false;
    }

    private static decimal GetGuaranteedEndTurnOrbDamage(
        Player player,
        Creature enemy,
        int actionableEnemyCount)
    {
        var pcs = player.PlayerCombatState;
        if (pcs is null || actionableEnemyCount <= 0 || !enemy.IsHittable)
            return 0m;

        decimal damage = 0m;

        foreach (var orb in pcs.OrbQueue.Orbs)
        {
            switch (orb)
            {
                // Glass damages every hittable enemy, so its passive is
                // deterministic regardless of enemy count.
                case GlassOrb glass:
                    damage += Math.Max(0m, glass.PassiveVal);
                    break;

                // Lightning chooses a random hittable enemy. It is only
                // deterministic when exactly one enemy can currently be hit.
                case LightningOrb lightning when actionableEnemyCount == 1:
                    damage += Math.Max(0m, lightning.PassiveVal);
                    break;
            }
        }

        return damage;
    }

    private static void OnCombatStateChanged(CombatState state)
    {
        if (!SpireGpsSettings.TurnGuardEnabled || !SpireGpsSettings.TurnGuardAutoUnready)
        {
            ResetSnapshot();
            return;
        }

        var me = LocalContext.GetMe(state);
        int livingPlayers = state.Players.Count(player => player.Creature.IsAlive);
        if (me is null || livingPlayers < 2)
        {
            // Once a teammate dies, the survivor is effectively playing solo
            // for Turn Guard purposes. Do not auto-unready them based on a
            // dead teammate's state transitions.
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

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.HandlePlayerDeath))]
internal static class TurnGuardDeadPlayerReadyPatch
{
    private static void Postfix(Player player, ref Task __result)
    {
        __result = CompleteDeathHandling(__result, player);
    }

    private static async Task CompleteDeathHandling(Task original, Player player)
    {
        await original;

        try
        {
            if (!SpireGpsSettings.TurnGuardEnabled ||
                !CombatManager.Instance.IsInProgress ||
                !player.Creature.IsDead ||
                player.Creature.CombatState.CurrentSide != CombatSide.Player ||
                CombatManager.Instance.IsPlayerReadyToEndTurn(player))
            {
                return;
            }

            // STS2 marks already-dead players ready when a new player turn
            // starts, but a teammate can also die DURING the current player
            // turn. In that case the dead player can remain in the ready-count
            // and prevent the surviving player from ever advancing the turn.
            // HandlePlayerDeath runs deterministically on each client, so mark
            // that dead player ready locally after vanilla cleanup completes.
            CombatManager.Instance.SetReadyToEndTurn(player, canBackOut: false);
            MainFile.Logger.Info(
                $"Turn Guard: marked dead teammate {player.NetId} ready so the surviving player can end turn.");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Turn Guard dead-player readiness fix skipped: {ex.Message}");
        }
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
