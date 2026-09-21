using System.Reflection;
using Godot;
using SpireGPS.Compatibility;
using SpireGPS.Features.DrawingPalette;
using SpireGPS.Features.MapLegend;
using SpireGPS.Features.RelicProgress;
using SpireGPS.Features.SynergyHints;
using SpireGPS.Features.Trading;
using SpireGPS.Features.Wishlist;
using SpireGPS.Telemetry;

namespace SpireGPS.Config;

internal static class ModConfigBridge
{
    private static Type? _apiType;
    private static Type? _entryType;
    private static Type? _configType;
    private static bool _registered;
    internal static bool IsRegistered => _registered;
    private static int _registerAttempts;
    private const int MaxRegisterAttempts = 8;

    internal static void DeferredRegister()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        tree.ProcessFrame += OnNextFrame;
    }

    private static void OnNextFrame()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        tree.ProcessFrame -= OnNextFrame;

        _registerAttempts++;
        if (DetectAndRegister())
            return;

        if (_registerAttempts < MaxRegisterAttempts)
        {
            tree.ProcessFrame += OnNextFrame;
            return;
        }

        MainFile.Logger.Info("ModConfig not detected after startup retries; Banter's Tweak's will use built-in defaults.");
    }

    private static bool DetectAndRegister()
    {
        if (_registered) return true;

        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Type.EmptyTypes; }
            })
            .ToArray();

        _apiType = types.FirstOrDefault(t => t.FullName == "ModConfig.ModConfigApi");
        _entryType = types.FirstOrDefault(t => t.FullName == "ModConfig.ConfigEntry");
        _configType = types.FirstOrDefault(t => t.FullName == "ModConfig.ConfigType");

        if (_apiType is null || _entryType is null || _configType is null)
            return false;

        try
        {
            var register = _apiType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "Register")
                .OrderByDescending(m => m.GetParameters().Length)
                .First();

            Array entries = ToTypedArray(BuildSingleEntries());
            var names = new Dictionary<string, string> { ["en"] = MainFile.DisplayName };

            if (register.GetParameters().Length == 4)
                register.Invoke(null, new object[] { MainFile.ModId, MainFile.DisplayName, names, entries });
            else
                register.Invoke(null, new object[] { MainFile.ModId, MainFile.DisplayName, entries });

            _registered = true;
            ModConfigAccordion.EnsureInstalled();
            LoadSavedValues();
            MainFile.Logger.Info("Registered Banter's Tweak's settings with ModConfig.");
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"ModConfig registration failed: {ex}");
            return true;
        }
    }

    private sealed record ConfigGroup(string ModId, string DisplayName, List<object> Entries);

    private static IReadOnlyList<ConfigGroup> BuildGroups()
    {
        var priorityOptions = new[] { "None", "Elites ↑", "Treasure ↑", "Rest ↑", "Shop ↑", "Unknown ↑", "Monsters ↓", "Monsters ↑" };

        // Keep module sections alphabetic. They are flattened into one
        // Banter's Tweak's registration and decorated as nested accordions.
        return new[]
        {
            Group("ChoiceCompare", "Choice Compare",
                Toggle("choiceCompareEnabled", "Enable Choice Compare", true)),

            Group("Compatibility", "Compatibility",
                Toggle("yieldToOverlappingMods", "Yield to Overlapping QoL Mods", true),
                Button("writeCompatibilityReport", "QoL overlap diagnostics", "Write Report", _ => WriteCompatibilityReport())),

            Group("DeckXRay", "Deck X-Ray",
                Toggle("deckXRayEnabled", "Enable Deck X-Ray", true)),

            Group("DrawingPalette", "Drawing Palette",
                Toggle("drawingPaletteEnabled", "Enable Drawing Palette", true),
                Toggle("drawingPaletteRecolorExisting", "Recolor Existing Drawings When Color Changes", false),
                Toggle("drawingPaletteHostForceRecolor", "Host Forces Recolor Mode", false)),

            Group("GhostTurnPlanner", "Ghost Turn Planner",
                Toggle("ghostTurnPlannerEnabled", "Enable Ghost Turn Planner", true)),

            Group("MapLegend", "Map Legend",
                Toggle("mapLegendEnabled", "Enable Map Legend Tweaks", true),
                Toggle("mapLegendVisible", "Show Map Legend", true),
                Toggle("mapLegendMovable", "Allow Moving Map Legend", true)),

            Group("MultiplayerPings", "Multiplayer Pings",
                Toggle("multiplayerPingsEnabled", "Enable Multiplayer Pings", true)),

            Group("PotionGuard", "Potion Guard",
                Toggle("potionGuardEnabled", "Enable Potion Guard", true),
                SliderRange("potionGuardConfirmSeconds", "Discard Confirmation Window", 2.5f, 1f, 5f, 0.5f)),

            Group("RelicProgress", "Relic Progress",
                Toggle("relicTrackerEnabled", "Enable Relic Progress", true),
                Toggle("relicTrackerShowProgressFraction", "Show Progress Fractions", true),
                Toggle("relicTrackerShowReady", "Show Ready Indicator", true)),

            Group("RoutePlanner", "Route Planner",
                Toggle("routePlannerEnabled", "Enable Route Planner", true),
                Toggle("routePanelEnabled", "Show Route List", true),
                Toggle("highlightSelectedRoute", "Highlight Selected Route", true),
                Toggle("fadeUnselectedRoutes", "Fade Unselected Routes", false),
                Dropdown("defaultRouteSort", "Default Route Sort", "Preferred",
                    "Preferred", "Route Order", "Monsters", "Unknowns", "Elites", "Shops", "Rest Sites", "Treasures"),
                Toggle("sortDescending", "Sort Highest First", true),
                Header("Suggested Route"),
                Toggle("preferredRouteEnabled", "Show Suggested Route", true),
                Toggle("autoHighlightPreferredRoute", "Auto-highlight Suggested Route", true),
                Toggle("routeAutoSelectNextNode", "Auto-select Next Suggested Node", false),
                Toggle("routeRushAutoTravel", "Rush Mode - Skip Auto-travel Confirmation", false),
                Toggle("routePrioritiesEnabled", "Use Priority Sorting", true),
                Dropdown("routePriority1", "Priority 1", "Elites ↑", priorityOptions),
                Dropdown("routePriority2", "Priority 2", "Treasure ↑", priorityOptions),
                Dropdown("routePriority3", "Priority 3", "None", priorityOptions),
                Slider("monsterWeight", "Monster Weight", -1f),
                Slider("unknownWeight", "Unknown (?) Weight", 0f),
                Slider("eliteWeight", "Elite Weight", 2f),
                Slider("shopWeight", "Shop Weight", 1f),
                Slider("restWeight", "Campsite Weight", 2f),
                Slider("treasureWeight", "Treasure Weight", 1f)),

            Group("RunTelemetry", "Run Telemetry",
                Toggle("runTelemetryEnabled", "Record Local Run Telemetry", true),
                Toggle("postRunSummaryEnabled", "Show End-of-Run Quick Recap", true),
                Button("openTelemetryFolder", "Local data folder", "Open Folder", _ => OpenTelemetryFolder())),

            Group("SynergyHints", "Synergy Hints",
                Toggle("synergyHintsEnabled", "Enable Synergy Hints", true),
                SliderRange("synergyHintsMax", "Maximum Hints per Item", 3f, 1f, 6f, 1f)),

            Group("Trading", "Trading",
                Toggle("tradingEnabled", "Enable Trading (Host)", false),
                Toggle("tradingAllowCards", "Allow Card Trades (Host)", true),
                Toggle("tradingAllowRelics", "Allow Safe Relic Trades (Host)", true),
                Toggle("tradingAllowGold", "Allow Gold Trades (Host)", true),
                Toggle("tradingAllowGifting", "Allow Gifting (Host)", false)),

            Group("TriggerInspector", "Trigger Inspector",
                Toggle("triggerInspectorEnabled", "Enable Trigger Inspector", true)),

            Group("TurnGuard", "Turn Guard",
                Toggle("turnGuardEnabled", "Enable Turn Guard", true),
                Toggle("turnGuardWarnPlayableCards", "Warn When Playable Cards Remain", true),
                Toggle("turnGuardWarnEnergy", "Mention Remaining Energy With Playable Cards", true),
                Toggle("turnGuardWarnLethal", "Warn About Potentially Lethal Incoming Damage", true),
                Toggle("turnGuardAutoUnready", "Auto-unready If State Changes", true),
                SliderRange("turnGuardConfirmSeconds", "Second-click Confirmation Window", 2.5f, 1f, 5f, 0.5f)),

            Group("TurnTimeline", "Turn Timeline",
                Toggle("turnTimelineEnabled", "Enable Turn Timeline", true)),

            Group("Wishlist", "Wishlist",
                Toggle("wishlistEnabled", "Enable Build Wishlist", true))
        };
    }

    private static ConfigGroup Group(string suffix, string name, params object[] entries)
        => new(
            $"{MainFile.ModId}.{suffix}",
            name,
            entries.ToList());

    private static IReadOnlyList<object> BuildSingleEntries()
    {
        var entries = new List<object>();

        foreach (var group in BuildGroups())
        {
            entries.Add(Header(group.DisplayName));
            entries.AddRange(group.Entries);
        }

        return entries;
    }

    private static Array ToTypedArray(IReadOnlyList<object> entries)
    {
        var result = Array.CreateInstance(_entryType!, entries.Count);
        for (int i = 0; i < entries.Count; i++)
            result.SetValue(entries[i], i);
        return result;
    }

    private static object Header(string label) => Entry(e =>
    {
        Set(e, "Label", label);
        Set(e, "Type", Enum.Parse(_configType!, "Header"));
    });

    private static object Toggle(string key, string label, bool defaultValue) => Entry(e =>
    {
        Set(e, "Key", key);
        Set(e, "Label", label);
        Set(e, "Type", Enum.Parse(_configType!, "Toggle"));
        Set(e, "DefaultValue", defaultValue);
        Set(e, "OnChanged", new Action<object>(v => ApplyAndRefresh(key, v)));
    });

    private static object Slider(string key, string label, float defaultValue) => Entry(e =>
    {
        Set(e, "Key", key);
        Set(e, "Label", label);
        Set(e, "Type", Enum.Parse(_configType!, "Slider"));
        Set(e, "DefaultValue", defaultValue);
        Set(e, "Min", -10f);
        Set(e, "Max", 10f);
        Set(e, "Step", 1f);
        Set(e, "Format", "F0");
        Set(e, "Description", "Positive = prefer this room type. Negative = avoid it.");
        Set(e, "OnChanged", new Action<object>(v => ApplyAndRefresh(key, v)));
    });

    private static object SliderRange(string key, string label, float defaultValue, float min, float max, float step) => Entry(e =>
    {
        Set(e, "Key", key);
        Set(e, "Label", label);
        Set(e, "Type", Enum.Parse(_configType!, "Slider"));
        Set(e, "DefaultValue", defaultValue);
        Set(e, "Min", min);
        Set(e, "Max", max);
        Set(e, "Step", step);
        Set(e, "Format", step < 1f ? "F1" : "F0");
        Set(e, "OnChanged", new Action<object>(v => ApplyAndRefresh(key, v)));
    });

    private static object Dropdown(string key, string label, string defaultValue, params string[] options) => Entry(e =>
    {
        Set(e, "Key", key);
        Set(e, "Label", label);
        Set(e, "Type", Enum.Parse(_configType!, "Dropdown"));
        Set(e, "DefaultValue", defaultValue);
        Set(e, "Options", options);
        Set(e, "OnChanged", new Action<object>(v => ApplyAndRefresh(key, v)));
    });

    private static object Button(string key, string label, string buttonText, Action<object> onChanged) => Entry(e =>
    {
        Set(e, "Key", key);
        Set(e, "Label", label);
        Set(e, "Type", Enum.Parse(_configType!, "Button"));
        Set(e, "ButtonText", buttonText);
        Set(e, "OnChanged", onChanged);
    });

    private static void WriteCompatibilityReport()
    {
        try
        {
            string path = CompatibilityManager.WriteCompatibilityReport();
            MainFile.Logger.Info($"Compatibility diagnostics available at {path}");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Could not write compatibility report: {ex.Message}");
        }
    }

    private static void OpenTelemetryFolder()
    {
        try
        {
            string path = RunTelemetryService.GetAbsoluteBaseDirectory();
            Directory.CreateDirectory(path);
            OS.ShellShowInFileManager(path);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Could not open Banter data folder: {ex.Message}");
        }
    }

    internal static void SetValue(string key, object value)
    {
        if (!_registered || _apiType is null)
            return;

        try
        {
            _apiType.GetMethod("SetValue", BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, new object[] { MainFile.ModId, key, value });
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"ModConfig SetValue failed for {key}: {ex.Message}");
        }
    }

    private static void ApplyAndRefresh(string key, object value)
    {
        SpireGpsSettings.Apply(key, value);
        if (IsRouteSetting(key)) MainFile.RefreshRoutes();
        if (key.StartsWith("drawingPalette", StringComparison.OrdinalIgnoreCase))
            DrawingPaletteService.ApplySettingsChanged(key);
        if (key.StartsWith("mapLegend", StringComparison.OrdinalIgnoreCase))
            MapLegendService.ApplySettingsChanged(key);
        if (key == "wishlistEnabled")
            WishlistService.RefreshAllStars();
        if (key.StartsWith("synergyHints", StringComparison.OrdinalIgnoreCase))
            SynergyHintService.RefreshAll();
        if (key.StartsWith("relicTracker", StringComparison.OrdinalIgnoreCase))
            RelicProgressService.RefreshAll();
        if (key.StartsWith("trading", StringComparison.OrdinalIgnoreCase))
            TradingService.ApplySettingsChanged();
    }

    private static bool IsRouteSetting(string key)
        => key.StartsWith("route", StringComparison.OrdinalIgnoreCase)
           || key.EndsWith("Weight", StringComparison.OrdinalIgnoreCase)
           || key is "highlightSelectedRoute" or "fadeUnselectedRoutes" or "defaultRouteSort"
               or "sortDescending" or "preferredRouteEnabled" or "autoHighlightPreferredRoute";

    private static object Entry(Action<object> configure)
    {
        var instance = Activator.CreateInstance(_entryType!)!;
        configure(instance);
        return instance;
    }

    private static void Set(object obj, string property, object value)
        => obj.GetType().GetProperty(property)?.SetValue(obj, value);

    private static void LoadSavedValues()
    {
        Load("yieldToOverlappingMods", true);

        Load("routePlannerEnabled", true);
        Load("routePanelEnabled", true);
        Load("highlightSelectedRoute", true);
        Load("fadeUnselectedRoutes", false);
        Load("defaultRouteSort", "Preferred");
        Load("sortDescending", true);
        Load("preferredRouteEnabled", true);
        Load("autoHighlightPreferredRoute", true);
        Load("routeAutoSelectNextNode", false);
        Load("routeRushAutoTravel", false);

        Load("routePrioritiesEnabled", true);
        Load("routePriority1", "Elites ↑");
        Load("routePriority2", "Treasure ↑");
        Load("routePriority3", "None");

        Load("monsterWeight", -1f);
        Load("unknownWeight", 0f);
        Load("eliteWeight", 2f);
        Load("shopWeight", 1f);
        Load("restWeight", 2f);
        Load("treasureWeight", 1f);
        Load("turnGuardEnabled", true);
        Load("turnGuardWarnPlayableCards", true);
        Load("turnGuardWarnEnergy", true);
        Load("turnGuardWarnLethal", true);
        Load("turnGuardAutoUnready", true);
        Load("turnGuardConfirmSeconds", 2.5f);

        Load("potionGuardEnabled", true);
        Load("potionGuardConfirmSeconds", 2.5f);

        Load("relicTrackerEnabled", true);
        Load("relicTrackerShowProgressFraction", true);
        Load("relicTrackerShowReady", true);

        Load("drawingPaletteEnabled", true);
        Load("drawingPaletteRecolorExisting", false);
        Load("drawingPaletteHostForceRecolor", false);

        Load("mapLegendEnabled", true);
        Load("mapLegendVisible", true);
        Load("mapLegendMovable", true);

        Load("synergyHintsEnabled", true);
        Load("synergyHintsMax", 3f);
        Load("wishlistEnabled", true);
        Load("multiplayerPingsEnabled", true);

        Load("ghostTurnPlannerEnabled", true);
        Load("deckXRayEnabled", true);
        Load("triggerInspectorEnabled", true);
        Load("turnTimelineEnabled", true);
        Load("choiceCompareEnabled", true);
        Load("runTelemetryEnabled", true);
        Load("postRunSummaryEnabled", true);

        Load("tradingEnabled", false);
        Load("tradingAllowCards", true);
        Load("tradingAllowRelics", true);
        Load("tradingAllowGold", true);
        Load("tradingAllowGifting", false);

        MainFile.RefreshRoutes();
    }

    private static void Load<T>(string key, T fallback)
    {
        try
        {
            var getValue = _apiType!.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "GetValue" && m.IsGenericMethodDefinition)
                .MakeGenericMethod(typeof(T));
            var result = getValue.Invoke(null, new object[] { MainFile.ModId, key });
            SpireGpsSettings.Apply(key, result ?? fallback!);
        }
        catch
        {
            SpireGpsSettings.Apply(key, fallback!);
        }
    }
}
