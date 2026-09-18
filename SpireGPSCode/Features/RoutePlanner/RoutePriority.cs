namespace SpireGPS.Features.RoutePlanner;

internal static class RoutePriority
{
    internal static readonly string[] Options =
    {
        "None",
        "Elites ↑",
        "Treasure ↑",
        "Rest ↑",
        "Shop ↑",
        "Unknown ↑",
        "Monsters ↓",
        "Monsters ↑"
    };

    internal static int Compare(RouteInfo a, RouteInfo b, string priority)
    {
        return priority switch
        {
            "Elites ↑" => b.Elites.CompareTo(a.Elites),
            "Treasure ↑" => b.Treasures.CompareTo(a.Treasures),
            "Rest ↑" => b.RestSites.CompareTo(a.RestSites),
            "Shop ↑" => b.Shops.CompareTo(a.Shops),
            "Unknown ↑" => b.Unknowns.CompareTo(a.Unknowns),
            "Monsters ↓" => a.Monsters.CompareTo(b.Monsters),
            "Monsters ↑" => b.Monsters.CompareTo(a.Monsters),
            _ => 0
        };
    }

    internal static int IndexOf(string value)
    {
        int index = Array.IndexOf(Options, value);
        return index >= 0 ? index : 0;
    }
}
