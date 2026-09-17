using System.Collections;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using SpireGPS.Config;

namespace SpireGPS.Features.RoutePlanner;

internal static class RouteHighlighter
{
    private static readonly Dictionary<TextureRect, (Color color, Vector2 scale)> OriginalTicks = new();
    private static FieldInfo? _pathsField;

    internal static void Initialize()
    {
        _pathsField = typeof(NMapScreen).GetField("_paths", BindingFlags.NonPublic | BindingFlags.Instance);
        if (_pathsField is null)
            MainFile.Logger.Error("Route Planner: could not find NMapScreen._paths; route highlighting will be unavailable.");
    }

    internal static void Refresh()
    {
        Clear();

        if (!SpireGpsSettings.RoutePlannerEnabled ||
            !SpireGpsSettings.HighlightSelectedRoute ||
            RoutePlannerService.SelectedRoute is null ||
            _pathsField is null)
            return;

        var mapScreen = NMapScreen.Instance;
        if (mapScreen is null || !mapScreen.IsOpen)
            return;

        try
        {
            if (_pathsField.GetValue(mapScreen) is not IDictionary paths)
                return;

            if (SpireGpsSettings.FadeUnselectedRoutes)
                FadeAllPathTicks(paths);

            HighlightRoute(paths, RoutePlannerService.SelectedRoute);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Route Planner highlight failed: {ex}");
        }
    }

    internal static void Clear()
    {
        foreach (var (tick, original) in OriginalTicks.ToArray())
        {
            if (tick is not null && GodotObject.IsInstanceValid(tick))
            {
                tick.Modulate = original.color;
                tick.Scale = original.scale;
            }
            else
            {
                OriginalTicks.Remove(tick);
            }
        }
    }

    private static void FadeAllPathTicks(IDictionary paths)
    {
        foreach (var value in paths.Values)
        {
            if (value is not IReadOnlyList<TextureRect> ticks)
                continue;

            foreach (var tick in ticks)
            {
                if (tick is null || !GodotObject.IsInstanceValid(tick))
                    continue;

                SaveOriginal(tick);
                var original = OriginalTicks[tick].color;
                tick.Modulate = new Color(original.R, original.G, original.B, original.A * 0.35f);
            }
        }
    }

    private static void HighlightRoute(IDictionary paths, RouteInfo route)
    {
        var highlight = new Color(0.30f, 0.88f, 1.00f, 1.00f);

        for (int i = 0; i < route.Points.Count - 1; i++)
        {
            MapCoord from = route.Points[i].coord;
            MapCoord to = route.Points[i + 1].coord;
            var keyForward = (from, to);
            var keyReverse = (to, from);

            object? value = null;
            if (paths.Contains(keyForward)) value = paths[keyForward];
            else if (paths.Contains(keyReverse)) value = paths[keyReverse];

            if (value is not IReadOnlyList<TextureRect> ticks)
                continue;

            foreach (var tick in ticks)
            {
                if (tick is null || !GodotObject.IsInstanceValid(tick))
                    continue;

                SaveOriginal(tick);
                tick.Modulate = highlight;
                tick.Scale = new Vector2(1.35f, 1.35f);
            }
        }
    }

    private static void SaveOriginal(TextureRect tick)
    {
        if (!OriginalTicks.ContainsKey(tick))
            OriginalTicks[tick] = (tick.Modulate, tick.Scale);
    }
}
