using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens;

namespace SpireGPS.UI;

internal static class UiContext
{
    internal static bool DeckViewOpen { get; private set; }

    internal static void SetDeckViewOpen(bool open)
        => DeckViewOpen = open;
}

[HarmonyPatch(typeof(NDeckViewScreen), nameof(NDeckViewScreen._EnterTree))]
internal static class BanterDeckViewEnterPatch
{
    private static void Postfix()
        => UiContext.SetDeckViewOpen(true);
}

[HarmonyPatch(typeof(NDeckViewScreen), nameof(NDeckViewScreen._ExitTree))]
internal static class BanterDeckViewExitPatch
{
    private static void Postfix()
        => UiContext.SetDeckViewOpen(false);
}
