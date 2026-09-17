using SpireGPS.Config;

namespace SpireGPS.Features.RoutePlanner;

internal static class RoutePreferenceScorer
{
    internal static float Score(RouteInfo route)
    {
        return
            route.Monsters * SpireGpsSettings.MonsterWeight +
            route.Unknowns * SpireGpsSettings.UnknownWeight +
            route.Elites * SpireGpsSettings.EliteWeight +
            route.Shops * SpireGpsSettings.ShopWeight +
            route.RestSites * SpireGpsSettings.RestWeight +
            route.Treasures * SpireGpsSettings.TreasureWeight;
    }
}
