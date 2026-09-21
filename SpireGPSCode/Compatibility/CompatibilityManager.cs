using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using SpireGPS.Config;
using SpireGPS.UI;

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

    internal static string WriteCompatibilityReport()
    {
        string directory = Path.Combine(OS.GetUserDataDir(), "banters_tweaks");
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "compatibility_report.txt");
        string report = BuildCompatibilityReport();
        File.WriteAllText(path, report);

        MainFile.Logger.Info($"Compatibility report written to {path}");
        SpireGpsToast.Show("Compatibility report written. Opening Banter's Tweak's data folder.");
        OS.ShellShowInFileManager(path);
        return path;
    }

    internal static string BuildCompatibilityReport()
    {
        var lines = new List<string>
        {
            "Banter's Tweak's - Compatibility Report",
            $"Generated: {DateTimeOffset.Now:O}",
            $"Yield to overlapping mods: {SpireGpsSettings.YieldToOverlappingMods}",
            string.Empty,
            "Loaded Mods"
        };

        var mods = GetLoadedModsReflectively()
            .Select(DescribeMod)
            .OrderBy(mod => mod.name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (mods.Length == 0)
        {
            lines.Add("  (none reported by ModManager)");
        }
        else
        {
            foreach (var mod in mods)
            {
                lines.Add(
                    $"  - {mod.name} | v{mod.version} | assembly={mod.assembly} | loaded={mod.loaded}");
            }
        }

        lines.Add(string.Empty);
        lines.Add("Overlap / Yield Checks");

        AppendPatchReport(
            lines,
            "Turn Guard",
            AccessTools.Method(typeof(NEndTurnButton), nameof(NEndTurnButton.CallReleaseLogic)),
            inputOnly: true);

        AppendPatchReport(
            lines,
            "Potion Guard",
            AccessTools.Method(
                typeof(MegaCrit.Sts2.Core.Nodes.Potions.NPotionPopup),
                "OnDiscardButtonPressed"),
            inputOnly: true);

        bool routeSuggest = IsAssemblyLoaded("RouteSuggest", "STS2RouteSuggest");
        lines.Add(
            $"  - Route Planner: RouteSuggest detected={routeSuggest}; " +
            $"Banter yields={SpireGpsSettings.YieldToOverlappingMods && routeSuggest}");

        bool betterDrawing = IsAssemblyLoaded("BetterDrawing");
        bool colorDraw = IsAssemblyLoaded("ColorDrawLib");
        lines.Add(
            $"  - Drawing Palette: BetterDrawing={betterDrawing}, ColorDrawLib={colorDraw}");

        AppendPatchReport(
            lines,
            "Drawing Palette map-line hook",
            AccessTools.Method(typeof(NMapDrawings), "CreateLineForPlayer"),
            inputOnly: false);

        lines.Add(string.Empty);
        lines.Add("Interpretation");
        lines.Add("  - 'Banter yields=True' means the Banter feature intentionally steps aside.");
        lines.Add("  - Foreign Harmony owners identify mods patching the same game method.");
        lines.Add("  - A shared patch does not automatically mean there is a conflict.");

        return string.Join(System.Environment.NewLine, lines);
    }

    private static void AppendPatchReport(
        List<string> lines,
        string module,
        MethodBase? target,
        bool inputOnly)
    {
        if (target is null)
        {
            lines.Add($"  - {module}: target method not found");
            return;
        }

        var info = Harmony.GetPatchInfo(target);
        if (info is null)
        {
            lines.Add($"  - {module}: no Harmony patches detected");
            return;
        }

        IEnumerable<Patch> patches = info.Prefixes.Concat(info.Transpilers);
        if (!inputOnly)
            patches = patches.Concat(info.Postfixes).Concat(info.Finalizers);

        string[] owners = patches
            .Select(patch => patch.owner)
            .Where(owner => !string.Equals(owner, HarmonyId, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(owner => owner, StringComparer.Ordinal)
            .ToArray();

        bool yields = SpireGpsSettings.YieldToOverlappingMods && owners.Length > 0;

        lines.Add(
            owners.Length == 0
                ? $"  - {module}: no foreign Harmony owners; Banter yields=False"
                : $"  - {module}: foreign owners=[{string.Join(", ", owners)}]; Banter yields={yields}");
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
