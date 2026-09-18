using System.Reflection;
using Godot;

namespace SpireGPS.Config;

internal static class ModConfigBridge
{
    private static Type? _apiType;
    private static Type? _entryType;
    private static Type? _configType;
    private static bool _registered;

    internal static void DeferredRegister()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        tree.ProcessFrame += OnNextFrame;
    }

    private static void OnNextFrame()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        tree.ProcessFrame -= OnNextFrame;
        DetectAndRegister();
    }

    private static void DetectAndRegister()
    {
        if (_registered) return;

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
        {
            MainFile.Logger.Info("ModConfig not detected; all SpireGPS modules are enabled with built-in defaults.");
            return;
        }

        try
        {
            var entries = BuildEntries();
            var register = _apiType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "Register")
                .OrderByDescending(m => m.GetParameters().Length)
                .First();

            var names = new Dictionary<string, string> { ["en"] = "SpireGPS" };
            if (register.GetParameters().Length == 4)
                register.Invoke(null, new object[] { MainFile.ModId, "SpireGPS", names, entries });
            else
                register.Invoke(null, new object[] { MainFile.ModId, "SpireGPS", entries });

            _registered = true;
            LoadSavedValues();
            MainFile.Logger.Info("Registered SpireGPS settings with ModConfig.");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"ModConfig registration failed: {ex}");
        }
    }

    private static Array BuildEntries()
    {
        var entries = new List<object>
        {
            Header("Route Planner"),
            Toggle("routePlannerEnabled", "Enable Route Planner", true),
            Toggle("routePanelEnabled", "Show Route List", true),
            Toggle("highlightSelectedRoute", "Highlight Selected Route", true),
            Toggle("fadeUnselectedRoutes", "Fade Unselected Routes", false),
            Dropdown("defaultRouteSort", "Default Route Sort", "Preferred",
                "Preferred", "Route Order", "Monsters", "Unknowns", "Elites", "Shops", "Rest Sites", "Treasures"),
            Toggle("sortDescending", "Sort Highest First", true),

            Header("Suggested Route"),
            Toggle("preferredRouteEnabled", "Show Suggested Route", true),
            Toggle("autoHighlightPreferredRoute", "Auto-select Suggested Route", true),
            Slider("monsterWeight", "Monster Weight", -1f),
            Slider("unknownWeight", "Unknown (?) Weight", 0f),
            Slider("eliteWeight", "Elite Weight", 2f),
            Slider("shopWeight", "Shop Weight", 1f),
            Slider("restWeight", "Campsite Weight", 2f),
            Slider("treasureWeight", "Treasure Weight", 1f),

            Header("Safety"),
            Toggle("turnGuardEnabled", "Turn Guard", true),
            Toggle("potionGuardEnabled", "Potion Guard", true),

            Header("Information"),
            Toggle("relicTrackerEnabled", "Relic Progress", true),
            Toggle("synergyHintsEnabled", "Synergy Hints", true),
            Toggle("wishlistEnabled", "Build Wishlist", true),

            Header("Multiplayer"),
            Toggle("multiplayerPingsEnabled", "Multiplayer Pings", true)
        };

        var result = Array.CreateInstance(_entryType!, entries.Count);
        for (int i = 0; i < entries.Count; i++) result.SetValue(entries[i], i);
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

    private static object Dropdown(string key, string label, string defaultValue, params string[] options) => Entry(e =>
    {
        Set(e, "Key", key);
        Set(e, "Label", label);
        Set(e, "Type", Enum.Parse(_configType!, "Dropdown"));
        Set(e, "DefaultValue", defaultValue);
        Set(e, "Options", options);
        Set(e, "OnChanged", new Action<object>(v => ApplyAndRefresh(key, v)));
    });

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
        Load("routePlannerEnabled", true);
        Load("routePanelEnabled", true);
        Load("highlightSelectedRoute", true);
        Load("fadeUnselectedRoutes", false);
        Load("defaultRouteSort", "Preferred");
        Load("sortDescending", true);
        Load("preferredRouteEnabled", true);
        Load("autoHighlightPreferredRoute", true);
        Load("monsterWeight", -1f);
        Load("unknownWeight", 0f);
        Load("eliteWeight", 2f);
        Load("shopWeight", 1f);
        Load("restWeight", 2f);
        Load("treasureWeight", 1f);
        Load("turnGuardEnabled", true);
        Load("potionGuardEnabled", true);
        Load("relicTrackerEnabled", true);
        Load("synergyHintsEnabled", true);
        Load("wishlistEnabled", true);
        Load("multiplayerPingsEnabled", true);

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
