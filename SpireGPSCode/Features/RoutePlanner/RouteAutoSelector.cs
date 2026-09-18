using System.Collections;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using SpireGPS.Config;

namespace SpireGPS.Features.RoutePlanner;

internal static class RouteAutoSelector
{
    private static readonly FieldInfo? MapPointDictionaryField =
        typeof(NMapScreen).GetField("_mapPointDictionary", BindingFlags.NonPublic | BindingFlags.Instance);

    private static string? _lastAttemptKey;
    private static ConfirmationDialog? _dialog;

    internal static void OnMapOpened()
    {
        _lastAttemptKey = null;
        TrySelectNext();
    }

    internal static void OnMapClosed()
    {
        _lastAttemptKey = null;
        CloseDialog();
    }

    internal static void TrySelectNext(bool force = false)
    {
        if (!SpireGpsSettings.AutoSelectNextNode)
            return;

        var screen = NMapScreen.Instance;
        var runState = MainFile.RunState;
        var route = RoutePlannerService.SelectedRoute;

        if (screen is null || !screen.IsOpen || runState is null || route is null)
            return;

        var nextPoint = GetNextPoint(route, runState.CurrentMapPoint, runState.Map?.StartingMapPoint);
        if (nextPoint is null)
            return;

        var node = FindMapNode(screen, nextPoint.coord);
        if (node is null || node.State != MapPointState.Travelable)
            return;

        string key = $"{runState.CurrentActIndex}:{runState.CurrentMapCoord}:{route.Index}:{nextPoint.coord}";
        if (!force && key == _lastAttemptKey)
            return;

        _lastAttemptKey = key;

        if (SpireGpsSettings.ConfirmAutoTravel)
            ShowConfirmation(screen, node, route);
        else
            SelectNode(screen, node);
    }

    private static MapPoint? GetNextPoint(RouteInfo route, MapPoint? current, MapPoint? startingPoint)
    {
        if (route.Points.Count == 0)
            return null;

        if (current is not null)
        {
            for (int i = 0; i < route.Points.Count - 1; i++)
            {
                if (route.Points[i].coord == current.coord)
                    return route.Points[i + 1];
            }
        }

        if (startingPoint is not null && route.Points[0].coord == startingPoint.coord)
            return route.Points.Count > 1 ? route.Points[1] : route.Points[0];

        return route.Points[0];
    }

    private static NMapPoint? FindMapNode(NMapScreen screen, MapCoord coord)
    {
        if (MapPointDictionaryField?.GetValue(screen) is not IDictionary dictionary)
            return null;

        return dictionary.Contains(coord) ? dictionary[coord] as NMapPoint : null;
    }

    private static void ShowConfirmation(NMapScreen screen, NMapPoint node, RouteInfo route)
    {
        CloseDialog();

        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        _dialog = new ConfirmationDialog
        {
            Title = "Banter's Tweak's - Route Planner",
            DialogText = $"Travel to the next node on Route {route.Index}: {Describe(node.Point.PointType)}?",
            OkButtonText = "Travel",
            CancelButtonText = "Cancel",
            Exclusive = true
        };

        _dialog.Confirmed += () =>
        {
            var dialog = _dialog;
            _dialog = null;
            if (dialog is not null && GodotObject.IsInstanceValid(dialog))
                dialog.QueueFree();

            if (GodotObject.IsInstanceValid(screen) && GodotObject.IsInstanceValid(node) &&
                screen.IsOpen && node.State == MapPointState.Travelable)
            {
                SelectNode(screen, node);
            }
        };

        _dialog.Canceled += () =>
        {
            var dialog = _dialog;
            _dialog = null;
            if (dialog is not null && GodotObject.IsInstanceValid(dialog))
                dialog.QueueFree();
        };

        tree.Root.AddChild(_dialog);
        _dialog.PopupCentered(new Vector2I(520, 180));
    }

    private static void SelectNode(NMapScreen screen, NMapPoint node)
    {
        MainFile.Logger.Info($"Route Planner: auto-selecting next node {node.Point.coord} ({node.Point.PointType}).");
        screen.OnMapPointSelectedLocally(node);
    }

    private static string Describe(MapPointType type)
    {
        return type switch
        {
            MapPointType.Monster => "Monster",
            MapPointType.Elite => "Elite",
            MapPointType.Shop => "Shop",
            MapPointType.RestSite => "Rest Site",
            MapPointType.Treasure => "Treasure",
            MapPointType.Unknown => "Unknown",
            MapPointType.Boss => "Boss",
            _ => type.ToString()
        };
    }

    private static void CloseDialog()
    {
        if (_dialog is not null && GodotObject.IsInstanceValid(_dialog))
            _dialog.QueueFree();

        _dialog = null;
    }
}
