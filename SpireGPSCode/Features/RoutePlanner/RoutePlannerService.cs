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
    internal static RouteInfo? SelectedRoute { get; private set; }
    internal static RouteSortMode CurrentSort { get; private set; } = RouteSortMode.Preferred;
    internal static bool SortDescending { get; private set; } = true;

    internal static void Clear()
    {
        _routes.Clear();
        _sortedRoutes.Clear();
        PreferredRoute = null;
        SelectedRoute = null;
    }

    internal static void Refresh(RunState runState)
    {
        Clear();

        var startPoint = runState.CurrentMapPoint ?? runState.Map?.StartingMapPoint;
        if (startPoint is null) return;

        var paths = new List<List<MapPoint>>();
        Enumerate(startPoint, new List<MapPoint>(), paths);

        // Match RouteSuggest's deterministic path ordering so Route N stays stable.
        paths.Sort((a, b) =>
        {
            int minLen = Math.Min(a.Count, b.Count);
            for (int i = 0; i < minLen; i++)
            {
                int cmp = a[i].coord.CompareTo(b[i].coord);
                if (cmp != 0) return cmp;
            }
            return a.Count.CompareTo(b.Count);
        });

        for (int i = 0; i < paths.Count; i++)
            _routes.Add(new RouteInfo(i + 1, paths[i]));

        RecalculateSuggestion();

        var mode = ParseSortMode(SpireGpsSettings.DefaultRouteSort);
        Sort(mode, SpireGpsSettings.SortDescending);
    }

    internal static void RecalculateSuggestion()
    {
        PreferredRoute = SpireGpsSettings.PreferredRouteEnabled
            ? GetSuggestedRoutes().FirstOrDefault()
            : null;

        if (SpireGpsSettings.AutoHighlightPreferredRoute)
            SelectedRoute = PreferredRoute;
    }

    internal static void SelectPreferredRoute()
    {
        SelectedRoute = PreferredRoute;
    }

    internal static void SelectRoute(RouteInfo? route)
    {
        if (route is not null && SelectedRoute?.Index == route.Index)
            SelectedRoute = null;
        else
            SelectedRoute = route;
    }

    internal static void SetSelectedRoute(RouteInfo? route)
        => SelectedRoute = route;

    internal static RouteInfo? FindRoute(int index)
        => _routes.FirstOrDefault(route => route.Index == index);

    internal static void Sort(RouteSortMode mode, bool descending)
    {
        CurrentSort = mode;
        SortDescending = descending;

        IEnumerable<RouteInfo> query = mode switch
        {
            RouteSortMode.Preferred => descending
                ? GetSuggestedRoutes()
                : GetSuggestedRoutes().Reverse(),
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

    private static IEnumerable<RouteInfo> GetSuggestedRoutes()
    {
        var comparer = Comparer<RouteInfo>.Create(CompareSuggested);
        return _routes.OrderBy(r => r, comparer);
    }

    private static int CompareSuggested(RouteInfo a, RouteInfo b)
    {
        if (SpireGpsSettings.RoutePrioritiesEnabled)
        {
            int cmp = RoutePriority.Compare(a, b, SpireGpsSettings.RoutePriority1);
            if (cmp != 0) return cmp;

            cmp = RoutePriority.Compare(a, b, SpireGpsSettings.RoutePriority2);
            if (cmp != 0) return cmp;

            cmp = RoutePriority.Compare(a, b, SpireGpsSettings.RoutePriority3);
            if (cmp != 0) return cmp;
        }

        int scoreCmp = RoutePreferenceScorer.Score(b).CompareTo(RoutePreferenceScorer.Score(a));
        return scoreCmp != 0 ? scoreCmp : a.Index.CompareTo(b.Index);
    }

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
