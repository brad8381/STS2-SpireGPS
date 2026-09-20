using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using SpireGPS.Config;
using SpireGPS.Features.ChoiceCompare;
using SpireGPS.UI;

namespace SpireGPS.Features.Wishlist;

internal static class WishlistService
{
    private const string CardSection = "wishlist_cards";
    private const string RelicSection = "wishlist_relics";
    private const string StarNodeName = "WishlistStar";
    private const string LibraryFilterNodeName = "BantersWishlistOnly";

    internal static bool CardLibraryWishlistOnly { get; private set; }

    private static readonly FieldInfo? CurrentPressedActionField =
        AccessTools.Field(typeof(NCardHolder), "_currentPressedAction");

    private static readonly FieldInfo? MerchantCardNodeField =
        AccessTools.Field(typeof(NMerchantCard), "_cardNode");

    private static readonly FieldInfo? MerchantRelicNodeField =
        AccessTools.Field(typeof(NMerchantRelic), "_relicNode");

    private static readonly MethodInfo? CardLibraryUpdateFilterMethod =
        AccessTools.Method(typeof(NCardLibrary), "UpdateFilter");

    internal static bool IsCardWishlisted(CardModel? card)
        => card is not null &&
           LocalPreferences.GetBool(CardSection, card.Id.ToString(), false);

    internal static bool IsRelicWishlisted(RelicModel? relic)
        => relic is not null &&
           LocalPreferences.GetBool(RelicSection, relic.Id.ToString(), false);

    internal static void ToggleCard(CardModel card)
    {
        bool next = !IsCardWishlisted(card);
        LocalPreferences.Set(CardSection, card.Id.ToString(), next);
        SpireGpsToast.Show($"Wishlist: {(next ? "added" : "removed")} {card.Title}.");
        RefreshAllStars();
        RefreshOpenCardLibraries();
    }

    internal static void ToggleRelic(RelicModel relic)
    {
        bool next = !IsRelicWishlisted(relic);
        LocalPreferences.Set(RelicSection, relic.Id.ToString(), next);
        SpireGpsToast.Show($"Wishlist: {(next ? "added" : "removed")} {relic.Title.GetFormattedText()}.");
        RefreshAllStars();
    }

    internal static void RefreshCardHolder(NGridCardHolder holder)
    {
        var legacy = holder.GetNodeOrNull<Label>(StarNodeName);
        Control owner = holder.CardNode is Control cardNode ? cardNode : holder;

        if (!ReferenceEquals(owner, holder) && legacy is not null)
            legacy.QueueFree();

        SetCardStar(
            owner,
            SpireGpsSettings.WishlistEnabled && IsCardWishlisted(holder.CardModel));
    }

    internal static void RefreshRelicCollectionEntry(NRelicCollectionEntry entry)
    {
        if (!SpireGpsSettings.WishlistEnabled)
        {
            SetStar(entry, false, new Vector2(48f, -8f), 28);
            return;
        }

        SetStar(
            entry,
            entry.ModelVisibility == ModelVisibility.Visible && IsRelicWishlisted(entry.relic),
            new Vector2(48f, -8f),
            28);
    }

    internal static void RefreshBasicRelicHolder(NRelicBasicHolder holder)
    {
        RelicModel? relic = null;
        try { relic = holder.Relic?.Model; }
        catch { }

        SetStar(
            holder,
            SpireGpsSettings.WishlistEnabled && IsRelicWishlisted(relic),
            new Vector2(52f, -10f),
            30);
    }

    internal static void RefreshMerchantCard(NMerchantCard merchant)
    {
        var node = MerchantCardNodeField?.GetValue(merchant) as MegaCrit.Sts2.Core.Nodes.Cards.NCard;
        SetStar(
            merchant,
            SpireGpsSettings.WishlistEnabled && IsCardWishlisted(node?.Model),
            new Vector2(205f, -4f),
            32);
    }

    internal static void RefreshMerchantRelic(NMerchantRelic merchant)
    {
        var relicNode = MerchantRelicNodeField?.GetValue(merchant) as NRelic;
        RelicModel? relic = null;
        try { relic = relicNode?.Model; }
        catch { }

        SetStar(
            merchant,
            SpireGpsSettings.WishlistEnabled && IsRelicWishlisted(relic),
            new Vector2(88f, -12f),
            30);
    }

    internal static bool IsInsideCardLibrary(NGridCardHolder holder)
        => HasAncestor<NCardLibraryGrid>(holder);

    internal static bool IsInsideDeckView(NGridCardHolder holder)
        => HasAncestor<NDeckViewScreen>(holder);

    internal static void AttachCardLibraryFilter(NCardLibrary library)
    {
        if (library.GetNodeOrNull<CheckBox>(LibraryFilterNodeName) is not null)
            return;

        var anchor = library.GetNodeOrNull<Control>("%MultiplayerCards");
        var parent = anchor?.GetParent();
        if (parent is null)
            return;

        var filter = new CheckBox
        {
            Name = LibraryFilterNodeName,
            Text = "★ Wishlisted only",
            ButtonPressed = CardLibraryWishlistOnly,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Show only cards on your Build Wishlist."
        };

        filter.Toggled += enabled =>
        {
            CardLibraryWishlistOnly = enabled;
            RefreshCardLibrary(library);
        };

        parent.AddChild(filter);
    }

    internal static void ClearCardPressState(NCardHolder holder)
        => CurrentPressedActionField?.SetValue(holder, null);

    internal static void RefreshAllStars()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        foreach (var holder in FindNodes<NGridCardHolder>(tree.Root))
            RefreshCardHolder(holder);

        foreach (var entry in FindNodes<NRelicCollectionEntry>(tree.Root))
            RefreshRelicCollectionEntry(entry);

        foreach (var holder in FindNodes<NRelicBasicHolder>(tree.Root))
            RefreshBasicRelicHolder(holder);

        foreach (var merchant in FindNodes<NMerchantCard>(tree.Root))
            RefreshMerchantCard(merchant);

        foreach (var merchant in FindNodes<NMerchantRelic>(tree.Root))
            RefreshMerchantRelic(merchant);
    }

    private static void RefreshOpenCardLibraries()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        foreach (var library in FindNodes<NCardLibrary>(tree.Root))
            RefreshCardLibrary(library);
    }

    private static void RefreshCardLibrary(NCardLibrary library)
    {
        try
        {
            CardLibraryUpdateFilterMethod?.Invoke(library, new object[] { false });
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Wishlist card-library refresh failed: {ex.Message}");
        }
    }

    private static bool HasAncestor<T>(Node node) where T : Node
    {
        Node? parent = node.GetParent();
        while (parent is not null)
        {
            if (parent is T)
                return true;
            parent = parent.GetParent();
        }

        return false;
    }

    private static IEnumerable<T> FindNodes<T>(Node root) where T : Node
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is T match)
                yield return match;

            foreach (T nested in FindNodes<T>(child))
                yield return nested;
        }
    }

    private static void SetCardStar(Control owner, bool visible)
    {
        var star = owner.GetNodeOrNull<Label>(StarNodeName);
        if (star is null)
        {
            star = new Label
            {
                Name = StarNodeName,
                Text = "★",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZIndex = 100,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            star.AddThemeFontSizeOverride("font_size", 30);
            star.AddThemeColorOverride("font_color", new Color("#F6C744"));
            star.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
            star.AddThemeConstantOverride("shadow_offset_x", 2);
            star.AddThemeConstantOverride("shadow_offset_y", 2);
            owner.AddChild(star);
        }

        star.AnchorLeft = 1f;
        star.AnchorRight = 1f;
        star.AnchorTop = 0f;
        star.AnchorBottom = 0f;
        star.OffsetLeft = -50f;
        star.OffsetRight = -6f;
        star.OffsetTop = 8f;
        star.OffsetBottom = 48f;
        star.Visible = visible;
    }

    private static void SetStar(Control owner, bool visible, Vector2 position, int fontSize)
    {
        var star = owner.GetNodeOrNull<Label>(StarNodeName);
        if (star is null)
        {
            star = new Label
            {
                Name = StarNodeName,
                Text = "★",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZIndex = 100,
                Position = position
            };
            star.AddThemeFontSizeOverride("font_size", fontSize);
            star.AddThemeColorOverride("font_color", new Color("#F6C744"));
            star.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
            star.AddThemeConstantOverride("shadow_offset_x", 2);
            star.AddThemeConstantOverride("shadow_offset_y", 2);
            owner.AddChild(star);
        }

        star.Position = position;
        star.Visible = visible;
    }
}

[HarmonyPatch(typeof(NCardHolder), "OnMouseReleased")]
internal static class WishlistCardRightClickPatch
{
    private static bool Prefix(NCardHolder __instance, InputEvent inputEvent)
    {
        if (__instance is not NGridCardHolder holder ||
            inputEvent is not InputEventMouseButton mouse ||
            mouse.Pressed ||
            mouse.ButtonIndex != MouseButton.Right)
        {
            return true;
        }

        var card = holder.CardModel;
        if (card is null || holder.CardNode?.Visibility != ModelVisibility.Visible)
            return true;

        bool directWishlistView =
            WishlistService.IsInsideCardLibrary(holder) ||
            WishlistService.IsInsideDeckView(holder);

        bool wishlistGesture =
            SpireGpsSettings.WishlistEnabled &&
            (directWishlistView || Input.IsKeyPressed(Key.Shift));

        if (wishlistGesture)
        {
            WishlistService.ToggleCard(card);

            // Right-click normally emits AltPressed and opens the same card
            // detail screen as left-click in the library.
            WishlistService.ClearCardPressState(holder);
            holder.AcceptEvent();
            return false;
        }

        if (ChoiceCompareService.TryPinGridCard(holder))
        {
            WishlistService.ClearCardPressState(holder);
            holder.AcceptEvent();
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(NGridCardHolder), nameof(NGridCardHolder._Ready))]
internal static class WishlistGridCardReadyPatch
{
    private static void Postfix(NGridCardHolder __instance)
        => WishlistService.RefreshCardHolder(__instance);
}

[HarmonyPatch(typeof(NGridCardHolder), "UpdateCardModel")]
internal static class WishlistGridCardReassignPatch
{
    private static void Postfix(NGridCardHolder __instance)
        => WishlistService.RefreshCardHolder(__instance);
}

[HarmonyPatch(typeof(NClickableControl), nameof(NClickableControl._GuiInput))]
internal static class WishlistRelicRightClickPatch
{
    private static bool Prefix(NClickableControl __instance, InputEvent inputEvent)
    {
        if (!SpireGpsSettings.WishlistEnabled ||
            __instance is not NRelicCollectionEntry entry ||
            entry.ModelVisibility != ModelVisibility.Visible ||
            inputEvent is not InputEventMouseButton mouse ||
            mouse.Pressed ||
            mouse.ButtonIndex != MouseButton.Right)
        {
            return true;
        }

        WishlistService.ToggleRelic(entry.relic);
        entry.AcceptEvent();
        return false;
    }
}

[HarmonyPatch(typeof(NRelicCollectionEntry), nameof(NRelicCollectionEntry._Ready))]
internal static class WishlistRelicCollectionReadyPatch
{
    private static void Postfix(NRelicCollectionEntry __instance)
        => WishlistService.RefreshRelicCollectionEntry(__instance);
}

[HarmonyPatch(typeof(NRelicBasicHolder), nameof(NRelicBasicHolder._Ready))]
internal static class WishlistBasicRelicReadyPatch
{
    private static void Postfix(NRelicBasicHolder __instance)
        => WishlistService.RefreshBasicRelicHolder(__instance);
}

[HarmonyPatch(typeof(NMerchantCard), "UpdateVisual")]
internal static class WishlistMerchantCardPatch
{
    private static void Postfix(NMerchantCard __instance)
        => WishlistService.RefreshMerchantCard(__instance);
}

[HarmonyPatch(typeof(NMerchantRelic), "UpdateVisual")]
internal static class WishlistMerchantRelicPatch
{
    private static void Postfix(NMerchantRelic __instance)
        => WishlistService.RefreshMerchantRelic(__instance);
}


[HarmonyPatch(typeof(NCardLibrary), nameof(NCardLibrary._Ready))]
internal static class WishlistCardLibraryReadyPatch
{
    private static void Postfix(NCardLibrary __instance)
        => WishlistService.AttachCardLibraryFilter(__instance);
}

[HarmonyPatch]
internal static class WishlistCardLibraryFilterPatch
{
    private static MethodBase TargetMethod()
        => typeof(NCardLibraryGrid)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method =>
                method.Name == nameof(NCardLibraryGrid.FilterCards) &&
                method.GetParameters().Length == 2);

    private static void Prefix(ref Func<CardModel, bool> __0)
    {
        if (!SpireGpsSettings.WishlistEnabled || !WishlistService.CardLibraryWishlistOnly)
            return;

        var original = __0;
        __0 = card => original(card) && WishlistService.IsCardWishlisted(card);
    }
}
