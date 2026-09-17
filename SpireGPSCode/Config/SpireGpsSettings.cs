namespace SpireGPS.Config;

internal static class SpireGpsSettings
{
    internal static bool RoutePlannerEnabled { get; set; } = true;
    internal static bool RoutePanelEnabled { get; set; } = true;
    internal static bool HighlightSelectedRoute { get; set; } = true;
    internal static bool FadeUnselectedRoutes { get; set; } = false;

    internal static bool TurnGuardEnabled { get; set; } = true;
    internal static bool PotionGuardEnabled { get; set; } = true;
    internal static bool RelicTrackerEnabled { get; set; } = true;
    internal static bool SynergyHintsEnabled { get; set; } = true;
    internal static bool WishlistEnabled { get; set; } = true;
    internal static bool MultiplayerPingsEnabled { get; set; } = true;

    internal static void Apply(string key, object value)
    {
        bool enabled = Convert.ToBoolean(value);
        switch (key)
        {
            case "routePlannerEnabled": RoutePlannerEnabled = enabled; break;
            case "routePanelEnabled": RoutePanelEnabled = enabled; break;
            case "highlightSelectedRoute": HighlightSelectedRoute = enabled; break;
            case "fadeUnselectedRoutes": FadeUnselectedRoutes = enabled; break;
            case "turnGuardEnabled": TurnGuardEnabled = enabled; break;
            case "potionGuardEnabled": PotionGuardEnabled = enabled; break;
            case "relicTrackerEnabled": RelicTrackerEnabled = enabled; break;
            case "synergyHintsEnabled": SynergyHintsEnabled = enabled; break;
            case "wishlistEnabled": WishlistEnabled = enabled; break;
            case "multiplayerPingsEnabled": MultiplayerPingsEnabled = enabled; break;
        }
    }
}
