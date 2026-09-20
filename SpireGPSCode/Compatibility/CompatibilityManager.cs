using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using SpireGPS.Config;

namespace SpireGPS.Compatibility;

internal static class CompatibilityManager
{
    internal const string HarmonyId = "brad8381.banterstweaks";

    private static readonly HashSet<string> Logged = new();

    internal static bool ShouldYieldTurnGuard()
    {
        if (!SpireGpsSettings.YieldToOverlappingMods)
            return false;

        var target = AccessTools.Method(typeof(NEndTurnButton), nameof(NEndTurnButton.CallReleaseLogic));
        return target is not null && HasForeignInputPatch(target, "Turn Guard");
    }

    internal static bool ShouldYieldPotionGuard()
    {
        if (!SpireGpsSettings.YieldToOverlappingMods)
            return false;

        var target = AccessTools.Method(
            typeof(MegaCrit.Sts2.Core.Nodes.Potions.NPotionPopup),
            "OnDiscardButtonPressed");

        return target is not null && HasForeignInputPatch(target, "Potion Guard");
    }

    internal static bool ShouldYieldRoutePlanner()
    {
        if (!SpireGpsSettings.YieldToOverlappingMods)
            return false;

        if (!IsAssemblyLoaded("RouteSuggest", "STS2RouteSuggest"))
            return false;

        LogOnce(
            "RoutePlanner-RouteSuggest",
            "Compatibility: RouteSuggest detected. Route Planner will yield to it.");
        return true;
    }

    internal static bool ShouldYieldDrawingPalette()
    {
        if (!SpireGpsSettings.YieldToOverlappingMods)
            return false;

        if (IsAssemblyLoaded("BetterDrawing"))
        {
            LogOnce("DrawingPalette-BetterDrawing",
                "Compatibility: BetterDrawing detected. Banter's Tweak's - Drawing Palette will yield to it.");
            return true;
        }

        if (IsAssemblyLoaded("ColorDrawLib"))
        {
            LogOnce("DrawingPalette-ColorDrawLib",
                "Compatibility: ColorDrawLib detected. Banter's Tweak's - Drawing Palette will yield to it.");
            return true;
        }

        var target = AccessTools.Method(typeof(NMapDrawings), "CreateLineForPlayer");
        return target is not null && HasForeignPatch(target, "Drawing Palette");
    }

    internal static bool IsAssemblyLoaded(params string[] names)
    {
        return AppDomain.CurrentDomain.GetAssemblies().Any(a =>
        {
            string name = a.GetName().Name ?? string.Empty;
            return names.Any(expected => name.Equals(expected, StringComparison.OrdinalIgnoreCase));
        });
    }

    private static bool HasForeignInputPatch(MethodBase target, string module)
    {
        var info = Harmony.GetPatchInfo(target);
        if (info is null)
            return false;

        var owners = info.Prefixes
            .Concat(info.Transpilers)
            .Select(p => p.owner)
            .Where(owner => !string.Equals(owner, HarmonyId, StringComparison.Ordinal))
            .Distinct()
            .ToArray();

        if (owners.Length == 0)
            return false;

        LogOnce($"{module}:{string.Join(",", owners)}",
            $"Compatibility: {module} yielded because another mod patches {target.DeclaringType?.Name}.{target.Name}: {string.Join(", ", owners)}");
        return true;
    }

    private static bool HasForeignPatch(MethodBase target, string module)
    {
        var info = Harmony.GetPatchInfo(target);
        if (info is null)
            return false;

        var owners = info.Prefixes
            .Concat(info.Postfixes)
            .Concat(info.Transpilers)
            .Concat(info.Finalizers)
            .Select(p => p.owner)
            .Where(owner => !string.Equals(owner, HarmonyId, StringComparison.Ordinal))
            .Distinct()
            .ToArray();

        if (owners.Length == 0)
            return false;

        LogOnce($"{module}:{string.Join(",", owners)}",
            $"Compatibility: {module} yielded because another mod patches {target.DeclaringType?.Name}.{target.Name}: {string.Join(", ", owners)}");
        return true;
    }

    private static void LogOnce(string key, string message)
    {
        if (Logged.Add(key))
            MainFile.Logger.Info(message);
    }
}
