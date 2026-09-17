using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;

namespace SpireGPS.Features.RoutePlanner;

internal static class RoutePlannerService
{
    private static readonly List<RouteInfo> _routes = new();
    internal static IReadOnlyList<RouteInfo> Routes => _routes;

    internal static void Clear() => _routes.Clear();

    internal static void Refresh(RunState runState)
    {
        _routes.Clear();

        var startPoint = runState.CurrentMapPoint ?? runState.Map?.StartingMapPoint;
        if (startPoint is null) return;

        var paths = new List<List<MapPoint>>();
        Enumerate(startPoint, new List<MapPoint>(), paths);

        for (int i = 0; i < paths.Count; i++)
            _routes.Add(new RouteInfo(i + 1, paths[i]));
    }

    private static void Enumerate(MapPoint current, List<MapPoint> currentPath, List<List<MapPoint>> allPaths)
    {
        currentPath.Add(current);

        if (current.PointType == MapPointType.Boss)
        {
            allPaths.Add(new List<MapPoint>(currentPath));
        }
        else if (current.Children is { Count: > 0 })
        {
            foreach (var child in current.Children)
                Enumerate(child, currentPath, allPaths);
        }

        currentPath.RemoveAt(currentPath.Count - 1);
    }
}
