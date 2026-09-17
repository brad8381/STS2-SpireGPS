namespace SpireGPS.Config;

internal static class SpireGpsSettings
{
    internal static bool RoutePlannerEnabled { get; set; } = true;
    internal static bool RoutePanelEnabled { get; set; } = true;
    internal static bool HighlightSelectedRoute { get; set; } = true;
    internal static bool FadeUnselectedRoutes { get; set; } = false;

    internal static string DefaultRouteSort { get; set; } = "Preferred";
    internal static bool SortDescending { get; set; } = true;
    internal static bool PreferredRouteEnabled { get; set; } = true;
    internal static bool AutoHighlightPreferredRoute { get; set; } = true;

    // Positive values favour a room type; negative values avoid it.
    internal static float MonsterWeight { get; set; } = -1f;
    internal static float UnknownWeight { get; set; } = 0f;
    internal static float EliteWeight { get; set; } = 2f;
    internal static float ShopWeight { get; set; } = 1f;
    internal static float RestWeight { get; set; } = 2f;
    internal static float TreasureWeight { get; set; } = 1f;

    internal static bool TurnGuardEnabled { get; set; } = true;
    internal static bool PotionGuardEnabled { get; set; } = true;
    internal static bool RelicTrackerEnabled { get; set; } = true;
    internal static bool SynergyHintsEnabled { get; set; } = true;
    internal static bool WishlistEnabled { get; set; } = true;
    internal static bool MultiplayerPingsEnabled { get; set; } = true;

    internal static void Apply(string key, object value)
    {
        switch (key)
        {
            case "routePlannerEnabled": RoutePlannerEnabled = Convert.ToBoolean(value); break;
            case "routePanelEnabled": RoutePanelEnabled = Convert.ToBoolean(value); break;
            case "highlightSelectedRoute": HighlightSelectedRoute = Convert.ToBoolean(value); break;
            case "fadeUnselectedRoutes": FadeUnselectedRoutes = Convert.ToBoolean(value); break;
            case "defaultRouteSort": DefaultRouteSort = Convert.ToString(value) ?? "Preferred"; break;
            case "sortDescending": SortDescending = Convert.ToBoolean(value); break;
            case "preferredRouteEnabled": PreferredRouteEnabled = Convert.ToBoolean(value); break;
            case "autoHighlightPreferredRoute": AutoHighlightPreferredRoute = Convert.ToBoolean(value); break;
            case "monsterWeight": MonsterWeight = Convert.ToSingle(value); break;
            case "unknownWeight": UnknownWeight = Convert.ToSingle(value); break;
            case "eliteWeight": EliteWeight = Convert.ToSingle(value); break;
            case "shopWeight": ShopWeight = Convert.ToSingle(value); break;
            case "restWeight": RestWeight = Convert.ToSingle(value); break;
            case "treasureWeight": TreasureWeight = Convert.ToSingle(value); break;
            case "turnGuardEnabled": TurnGuardEnabled = Convert.ToBoolean(value); break;
            case "potionGuardEnabled": PotionGuardEnabled = Convert.ToBoolean(value); break;
            case "relicTrackerEnabled": RelicTrackerEnabled = Convert.ToBoolean(value); break;
            case "synergyHintsEnabled": SynergyHintsEnabled = Convert.ToBoolean(value); break;
            case "wishlistEnabled": WishlistEnabled = Convert.ToBoolean(value); break;
            case "multiplayerPingsEnabled": MultiplayerPingsEnabled = Convert.ToBoolean(value); break;
        }
    }
}
