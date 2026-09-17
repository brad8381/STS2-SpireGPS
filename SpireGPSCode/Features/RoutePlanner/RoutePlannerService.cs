using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;
using SpireGPS.Config;

namespace SpireGPS.Features.RoutePlanner;

internal static class RoutePlannerService
{
    private static readonly List<RouteInfo> _routes = new();
    private static readonly List<RouteInfo> _sortedRoutes = new();

    internal static IReadOnlyList<RouteInfo> Routes => _routes;
    internal static IReadOnlyList<RouteInfo> SortedRoutes => _sortedRoutes;
    internal static RouteInfo? PreferredRoute { get; private set; }
    internal static RouteSortMode CurrentSort { get; private set; } = RouteSortMode.Preferred;
    internal static bool SortDescending { get; private set; } = true;

    internal static void Clear()
    {
        _routes.Clear();
        _sortedRoutes.Clear();
        PreferredRoute = null;
    }

    internal static void Refresh(RunState runState)
    {
        Clear();

        var startPoint = runState.CurrentMapPoint ?? runState.Map?.StartingMapPoint;
        if (startPoint is null) return;

        var paths = new List<List<MapPoint>>();
        Enumerate(startPoint, new List<MapPoint>(), paths);

        for (int i = 0; i < paths.Count; i++)
            _routes.Add(new RouteInfo(i + 1, paths[i]));

        PreferredRoute = SpireGpsSettings.PreferredRouteEnabled
            ? _routes
                .OrderByDescending(RoutePreferenceScorer.Score)
                .ThenBy(r => r.Index)
                .FirstOrDefault()
            : null;

        var mode = ParseSortMode(SpireGpsSettings.DefaultRouteSort);
        Sort(mode, SpireGpsSettings.SortDescending);
    }

    internal static void Sort(RouteSortMode mode, bool descending)
    {
        CurrentSort = mode;
        SortDescending = descending;

        IEnumerable<RouteInfo> query = mode switch
        {
            RouteSortMode.Preferred => descending
                ? _routes.OrderByDescending(RoutePreferenceScorer.Score).ThenBy(r => r.Index)
                : _routes.OrderBy(RoutePreferenceScorer.Score).ThenBy(r => r.Index),
            RouteSortMode.Monsters => SortBy(r => r.Monsters, descending),
            RouteSortMode.Unknowns => SortBy(r => r.Unknowns, descending),
            RouteSortMode.Elites => SortBy(r => r.Elites, descending),
            RouteSortMode.Shops => SortBy(r => r.Shops, descending),
            RouteSortMode.RestSites => SortBy(r => r.RestSites, descending),
            RouteSortMode.Treasures => SortBy(r => r.Treasures, descending),
            _ => descending ? _routes.OrderByDescending(r => r.Index) : _routes.OrderBy(r => r.Index)
        };

        _sortedRoutes.Clear();
        _sortedRoutes.AddRange(query);
    }

    internal static void ToggleColumnSort(RouteSortMode mode)
    {
        bool descending = mode == CurrentSort ? !SortDescending : true;
        Sort(mode, descending);
    }

    internal static float GetPreferredScore(RouteInfo route) => RoutePreferenceScorer.Score(route);

    private static IEnumerable<RouteInfo> SortBy(Func<RouteInfo, int> selector, bool descending)
        => descending
            ? _routes.OrderByDescending(selector).ThenBy(r => r.Index)
            : _routes.OrderBy(selector).ThenBy(r => r.Index);

    private static RouteSortMode ParseSortMode(string value)
        => Enum.TryParse<RouteSortMode>(value.Replace(" ", string.Empty), true, out var mode)
            ? mode
            : RouteSortMode.Preferred;

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
