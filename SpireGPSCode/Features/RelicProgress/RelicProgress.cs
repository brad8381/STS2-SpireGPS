using Godot;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Relics;
using SpireGPS.Config;

namespace SpireGPS.Features.RelicProgress;

internal static class RelicProgressService
{
    private static readonly MethodInfo? RefreshAmountMethod =
        AccessTools.Method(typeof(NRelicInventoryHolder), "RefreshAmount");

    internal static void RefreshAll()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || RefreshAmountMethod is null)
            return;

        foreach (var holder in FindNodes<NRelicInventoryHolder>(tree.Root))
        {
            try
            {
                RefreshAmountMethod.Invoke(holder, null);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"Relic Progress refresh skipped a holder: {ex.Message}");
            }
        }
    }

    private static IEnumerable<T> FindNodes<T>(Node root) where T : Node
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is T match)
                yield return match;

            foreach (var nested in FindNodes<T>(child))
                yield return nested;
        }
    }
}

internal readonly record struct RelicProgressInfo(int Current, int Threshold, bool CanShowReady);

internal static class RelicProgressRegistry
{
    internal static bool TryGet(RelicModel model, out RelicProgressInfo info)
    {
        info = default;

        if (!model.ShowCounter || model.DisplayAmount < 0)
            return false;

        string name = model.GetType().Name;
        int threshold;
        bool canShowReady = true;

        try
        {
            switch (name)
            {
                case "PenNib":
                    threshold = 10;
                    break;

                case "Girya":
                    threshold = 3;
                    canShowReady = false;
                    break;

                case "Nunchaku":
                case "TuningFork":
                case "IronClub":
                case "LetterOpener":
                case "Kunai":
                case "Shuriken":
                case "OrnamentalFan":
                    threshold = model.DynamicVars.Cards.IntValue;
                    break;

                case "HappyFlower":
                    threshold = model.DynamicVars["Turns"].IntValue;
                    break;

                case "StoneCalendar":
                    threshold = model.DynamicVars["DamageTurn"].IntValue;
                    break;

                case "Metronome":
                    threshold = model.DynamicVars["OrbCount"].IntValue;
                    break;

                case "VelvetChoker":
                    threshold = model.DynamicVars.Cards.IntValue;
                    canShowReady = false;
                    break;

                case "Pocketwatch":
                case "DiamondDiadem":
                    threshold = model.DynamicVars["CardThreshold"].IntValue;
                    canShowReady = false;
                    break;

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }

        if (threshold <= 0)
            return false;

        info = new RelicProgressInfo(model.DisplayAmount, threshold, canShowReady);
        return true;
    }
}

[HarmonyPatch(typeof(NRelicInventoryHolder), "RefreshAmount")]
internal static class RelicProgressAmountPatch
{
    private static readonly FieldInfo? AmountLabelField =
        AccessTools.Field(typeof(NRelicInventoryHolder), "_amountLabel");

    private static void Postfix(NRelicInventoryHolder __instance)
    {
        if (!SpireGpsSettings.RelicTrackerEnabled ||
            !SpireGpsSettings.RelicTrackerShowProgressFraction)
        {
            return;
        }

        try
        {
            if (AmountLabelField?.GetValue(__instance) is not MegaLabel label || !label.Visible)
                return;

            var model = __instance.Relic?.Model;
            if (model is null || !RelicProgressRegistry.TryGet(model, out var progress))
                return;

            bool ready = SpireGpsSettings.RelicTrackerShowReady &&
                         progress.CanShowReady &&
                         model.Status == RelicStatus.Active;

            string prefix = ready ? "★ " : string.Empty;
            label.SetTextAutoSize($"{prefix}{progress.Current}/{progress.Threshold}");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Relic Progress skipped a relic: {ex.Message}");
        }
    }
}
