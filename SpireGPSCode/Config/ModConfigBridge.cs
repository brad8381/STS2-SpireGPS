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
            MainFile.Logger.Info("ModConfig not detected; using SpireGPS defaults (all modules enabled).");
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
        Set(e, "OnChanged", new Action<object>(v =>
        {
            SpireGpsSettings.Apply(key, v);
            if (key.StartsWith("route", StringComparison.OrdinalIgnoreCase) || key == "highlightSelectedRoute" || key == "fadeUnselectedRoutes")
                MainFile.RefreshRoutes();
        }));
    });

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
        foreach (var (key, fallback) in new (string, bool)[]
        {
            ("routePlannerEnabled", true),
            ("routePanelEnabled", true),
            ("highlightSelectedRoute", true),
            ("fadeUnselectedRoutes", false),
            ("turnGuardEnabled", true),
            ("potionGuardEnabled", true),
            ("relicTrackerEnabled", true),
            ("synergyHintsEnabled", true),
            ("wishlistEnabled", true),
            ("multiplayerPingsEnabled", true)
        })
        {
            try
            {
                var getValue = _apiType!.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .First(m => m.Name == "GetValue" && m.IsGenericMethodDefinition)
                    .MakeGenericMethod(typeof(bool));
                var value = (bool)(getValue.Invoke(null, new object[] { MainFile.ModId, key }) ?? fallback);
                SpireGpsSettings.Apply(key, value);
            }
            catch
            {
                SpireGpsSettings.Apply(key, fallback);
            }
        }
    }
}
