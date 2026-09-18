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

    internal static bool RoutePrioritiesEnabled { get; set; } = true;
    internal static string RoutePriority1 { get; set; } = "Elites ↑";
    internal static string RoutePriority2 { get; set; } = "Treasure ↑";
    internal static string RoutePriority3 { get; set; } = "None";

    // Positive values favour a room type; negative values avoid it.
    // Weights are used after route priorities as the final tie-break.
    internal static float MonsterWeight { get; set; } = -1f;
    internal static float UnknownWeight { get; set; } = 0f;
    internal static float EliteWeight { get; set; } = 2f;
    internal static float ShopWeight { get; set; } = 1f;
    internal static float RestWeight { get; set; } = 2f;
    internal static float TreasureWeight { get; set; } = 1f;

    internal static bool TurnGuardEnabled { get; set; } = true;
    internal static bool TurnGuardWarnPlayableCards { get; set; } = true;
    internal static bool TurnGuardWarnEnergy { get; set; } = true;
    internal static bool TurnGuardWarnLethal { get; set; } = true;
    internal static bool TurnGuardAutoUnready { get; set; } = true;
    internal static float TurnGuardConfirmSeconds { get; set; } = 2.5f;

    internal static bool PotionGuardEnabled { get; set; } = true;
    internal static float PotionGuardConfirmSeconds { get; set; } = 2.5f;

    internal static bool RelicTrackerEnabled { get; set; } = true;
    internal static bool RelicTrackerShowReady { get; set; } = true;
    internal static bool RelicTrackerShowProgressFraction { get; set; } = true;

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

            case "routePrioritiesEnabled": RoutePrioritiesEnabled = Convert.ToBoolean(value); break;
            case "routePriority1": RoutePriority1 = Convert.ToString(value) ?? "Elites ↑"; break;
            case "routePriority2": RoutePriority2 = Convert.ToString(value) ?? "Treasure ↑"; break;
            case "routePriority3": RoutePriority3 = Convert.ToString(value) ?? "None"; break;

            case "monsterWeight": MonsterWeight = Convert.ToSingle(value); break;
            case "unknownWeight": UnknownWeight = Convert.ToSingle(value); break;
            case "eliteWeight": EliteWeight = Convert.ToSingle(value); break;
            case "shopWeight": ShopWeight = Convert.ToSingle(value); break;
            case "restWeight": RestWeight = Convert.ToSingle(value); break;
            case "treasureWeight": TreasureWeight = Convert.ToSingle(value); break;

            case "turnGuardEnabled": TurnGuardEnabled = Convert.ToBoolean(value); break;
            case "turnGuardWarnPlayableCards": TurnGuardWarnPlayableCards = Convert.ToBoolean(value); break;
            case "turnGuardWarnEnergy": TurnGuardWarnEnergy = Convert.ToBoolean(value); break;
            case "turnGuardWarnLethal": TurnGuardWarnLethal = Convert.ToBoolean(value); break;
            case "turnGuardAutoUnready": TurnGuardAutoUnready = Convert.ToBoolean(value); break;
            case "turnGuardConfirmSeconds": TurnGuardConfirmSeconds = Convert.ToSingle(value); break;

            case "potionGuardEnabled": PotionGuardEnabled = Convert.ToBoolean(value); break;
            case "potionGuardConfirmSeconds": PotionGuardConfirmSeconds = Convert.ToSingle(value); break;

            case "relicTrackerEnabled": RelicTrackerEnabled = Convert.ToBoolean(value); break;
            case "relicTrackerShowReady": RelicTrackerShowReady = Convert.ToBoolean(value); break;
            case "relicTrackerShowProgressFraction": RelicTrackerShowProgressFraction = Convert.ToBoolean(value); break;

            case "synergyHintsEnabled": SynergyHintsEnabled = Convert.ToBoolean(value); break;
            case "wishlistEnabled": WishlistEnabled = Convert.ToBoolean(value); break;
            case "multiplayerPingsEnabled": MultiplayerPingsEnabled = Convert.ToBoolean(value); break;
        }
    }
}
