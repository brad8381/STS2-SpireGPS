using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Potions;
using SpireGPS.Compatibility;
using SpireGPS.Config;
using SpireGPS.UI;

namespace SpireGPS.Features.PotionGuard;

[HarmonyPatch(typeof(NPotionPopup), "OnDiscardButtonPressed")]
internal static class PotionDiscardGuardPatch
{
    private static readonly System.Reflection.FieldInfo? HolderField =
        AccessTools.Field(typeof(NPotionPopup), "_holder");

    private static PotionModel? _armedPotion;
    private static ulong _armedUntil;

    private static bool Prefix(NPotionPopup __instance)
    {
        if (!SpireGpsSettings.PotionGuardEnabled ||
            CompatibilityManager.ShouldYieldPotionGuard())
        {
            return true;
        }

        try
        {
            var holder = HolderField?.GetValue(__instance) as NPotionHolder;
            var potion = holder?.Potion?.Model;
            if (potion is null || !LocalContext.IsMine(potion))
                return true;

            ulong now = Time.GetTicksMsec();
            if (ReferenceEquals(_armedPotion, potion) && now <= _armedUntil)
            {
                Disarm();
                return true;
            }

            _armedPotion = potion;
            _armedUntil = now + (ulong)(Math.Max(0.5f, SpireGpsSettings.PotionGuardConfirmSeconds) * 1000f);
            SpireGpsToast.Show(
                "Potion Guard: click Discard again to confirm.",
                SpireGpsSettings.PotionGuardConfirmSeconds);
            return false;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Potion Guard failed open: {ex}");
            return true;
        }
    }

    private static void Disarm()
    {
        _armedPotion = null;
        _armedUntil = 0;
    }
}
