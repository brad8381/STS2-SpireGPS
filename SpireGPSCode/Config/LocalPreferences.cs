using Godot;

namespace SpireGPS.Config;

internal static class LocalPreferences
{
    private const string Path = "user://banters_tweaks.cfg";
    private static readonly ConfigFile Config = new();
    private static bool _loaded;

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;
        var error = Config.Load(Path);
        if (error != Error.Ok && error != Error.FileNotFound)
            MainFile.Logger.Warn($"Local preferences could not be loaded: {error}");
    }

    internal static bool GetBool(string section, string key, bool fallback)
    {
        EnsureLoaded();
        Variant value = Config.GetValue(section, key, fallback);
        return value.VariantType == Variant.Type.Bool ? value.AsBool() : fallback;
    }

    internal static int GetInt(string section, string key, int fallback)
    {
        EnsureLoaded();
        Variant value = Config.GetValue(section, key, fallback);
        return value.VariantType == Variant.Type.Int ? (int)value.AsInt64() : fallback;
    }

    internal static Vector2 GetVector2(string section, string key, Vector2 fallback)
    {
        EnsureLoaded();
        Variant value = Config.GetValue(section, key, fallback);
        return value.VariantType == Variant.Type.Vector2 ? value.AsVector2() : fallback;
    }

    internal static bool HasValue(string section, string key)
    {
        EnsureLoaded();
        return Config.HasSectionKey(section, key);
    }

    internal static void Set(string section, string key, Variant value)
    {
        EnsureLoaded();
        Config.SetValue(section, key, value);
        var error = Config.Save(Path);
        if (error != Error.Ok)
            MainFile.Logger.Warn($"Local preferences could not be saved: {error}");
    }
}
