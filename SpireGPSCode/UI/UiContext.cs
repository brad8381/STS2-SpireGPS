using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace SpireGPS.UI;

internal static class UiContext
{
    internal static bool DeckViewOpen { get; private set; }

    internal static void SetDeckViewOpen(bool open)
        => DeckViewOpen = open;

    internal static bool IsCombatScreenCurrent()
    {
        try
        {
            return NCombatRoom.Instance is { } room &&
                   room.IsInsideTree() &&
                   room.IsVisibleInTree() &&
                   ActiveScreenContext.Instance.IsCurrent(room);
        }
        catch
        {
            return false;
        }
    }
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
