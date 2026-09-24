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
    // v2 intentionally starts clean. Earlier QA builds could visually/toggle
    // the wrong grid items, leaving polluted favourites in wishlist_cards.
    private const string CardSection = "wishlist_cards_v2";
    private const string RelicSection = "wishlist_relics";
    private const string StarNodeName = "WishlistStar";
    private const string LegacyCardStarNodeName = "WishlistCardBadge";
    private const string CardStarNodeName = "WishlistCardStarV2";
    private const string CardStarFallbackNodeName = "WishlistCardStarFallbackV2";
    private const string LibraryFilterNodeName = "BantersWishlistOnly";
    private const string WishlistIconRelativePath = "Assets/UI/Wishlist/wishlist_star_32.png";

    private static Texture2D? _wishlistIcon;

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
        RefreshCardInstances(card);

        // Only rebuild the library when the active filter actually depends on
        // wishlist membership. Normal wishlist toggles should not refresh the
        // whole card screen or disturb hover/sort state.
        if (CardLibraryWishlistOnly)
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
        // Remove the old free-floating label implementation if it exists.
        holder.GetNodeOrNull<Label>(StarNodeName)?.QueueFree();
        holder.GetNodeOrNull<Label>(LegacyCardStarNodeName)?.QueueFree();
        holder.GetNodeOrNull<PanelContainer>(LegacyCardStarNodeName)?.QueueFree();

        if (holder.CardNode is { } cardNode)
        {
            cardNode.GetNodeOrNull<Label>(StarNodeName)?.QueueFree();
            cardNode.GetNodeOrNull<Label>(LegacyCardStarNodeName)?.QueueFree();
            cardNode.GetNodeOrNull<PanelContainer>(LegacyCardStarNodeName)?.QueueFree();

            Control body = cardNode.Body;
            body.GetNodeOrNull<Label>(StarNodeName)?.QueueFree();
            body.GetNodeOrNull<Label>(LegacyCardStarNodeName)?.QueueFree();
            body.GetNodeOrNull<PanelContainer>(LegacyCardStarNodeName)?.QueueFree();

            SetCardStar(
                body,
                SpireGpsSettings.WishlistEnabled && IsCardWishlisted(holder.CardModel));
            return;
        }

        SetCardStar(
            holder,
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

    internal static bool TryToggleMerchantSlot(NMerchantSlot slot)
    {
        switch (slot)
        {
            case NMerchantCard cardSlot:
            {
                var node = MerchantCardNodeField?.GetValue(cardSlot) as MegaCrit.Sts2.Core.Nodes.Cards.NCard;
                if (node?.Model is null)
                    return false;

                ToggleCard(node.Model);
                return true;
            }

            case NMerchantRelic relicSlot:
            {
                var node = MerchantRelicNodeField?.GetValue(relicSlot) as NRelic;
                RelicModel? relic = null;
                try { relic = node?.Model; }
                catch { }

                if (relic is null)
                    return false;

                ToggleRelic(relic);
                return true;
            }

            default:
                return false;
        }
    }

    internal static bool IsInsideCardLibrary(NGridCardHolder holder)
        => HasAncestor<NCardLibraryGrid>(holder);

    internal static bool IsActualCardLibraryGrid(NCardLibraryGrid grid)
        => HasAncestor<NCardLibrary>(grid);

    internal static bool IsInsideDeckView(NGridCardHolder holder)
        => HasAncestor<NDeckViewScreen>(holder);

    internal static void AttachCardLibraryFilter(NCardLibrary library)
    {
        var anchor = library.GetNodeOrNull<Control>("%MultiplayerCards");
        var parent = anchor?.GetParent();
        if (parent is null || parent.GetNodeOrNull<CheckBox>(LibraryFilterNodeName) is not null)
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

    private static void RefreshCardInstances(CardModel card)
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        foreach (var holder in FindNodes<NGridCardHolder>(tree.Root))
        {
            if (holder.CardModel?.Id == card.Id)
                RefreshCardHolder(holder);
        }

        foreach (var merchant in FindNodes<NMerchantCard>(tree.Root))
        {
            var node = MerchantCardNodeField?.GetValue(merchant) as MegaCrit.Sts2.Core.Nodes.Cards.NCard;
            if (node?.Model?.Id == card.Id)
                RefreshMerchantCard(merchant);
        }
    }

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
        // Clean up the previous text-glyph implementation when upgrading from
        // a QA build in the same scene.
        owner.GetNodeOrNull<Label>(CardStarNodeName)?.QueueFree();

        Texture2D? texture = GetWishlistIcon();
        var star = owner.GetNodeOrNull<TextureRect>(CardStarNodeName);
        if (star is null)
        {
            star = new TextureRect
            {
                Name = CardStarNodeName,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZIndex = 140,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
            };
            owner.AddChild(star);
        }

        star.Texture = texture;

        // Parent is normally NCard.Body (%CardContainer), so this follows the
        // card's native hover/scale transform. Keep the badge just inside the
        // lower-right frame.
        star.AnchorLeft = 1f;
        star.AnchorRight = 1f;
        star.AnchorTop = 1f;
        star.AnchorBottom = 1f;
        star.OffsetLeft = -42f;
        star.OffsetRight = -10f;
        star.OffsetTop = -44f;
        star.OffsetBottom = -12f;
        star.Visible = visible && texture is not null;

        // Stable/Beta builds can differ in how mod assets are mounted. Never
        // make the wishlist silently disappear just because the PNG could not
        // be loaded: fall back to a normal glyph.
        var fallback = owner.GetNodeOrNull<Label>(CardStarFallbackNodeName);
        if (fallback is null)
        {
            fallback = new Label
            {
                Name = CardStarFallbackNodeName,
                Text = "★",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZIndex = 141,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            fallback.AddThemeFontSizeOverride("font_size", 25);
            fallback.AddThemeColorOverride("font_color", new Color("#F6C744"));
            fallback.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
            fallback.AddThemeConstantOverride("shadow_offset_x", 2);
            fallback.AddThemeConstantOverride("shadow_offset_y", 2);
            owner.AddChild(fallback);
        }

        fallback.AnchorLeft = 1f;
        fallback.AnchorRight = 1f;
        fallback.AnchorTop = 1f;
        fallback.AnchorBottom = 1f;
        fallback.OffsetLeft = -42f;
        fallback.OffsetRight = -10f;
        fallback.OffsetTop = -44f;
        fallback.OffsetBottom = -12f;
        fallback.Visible = visible && texture is null;
    }

    private static Texture2D? GetWishlistIcon()
    {
        if (_wishlistIcon is not null)
            return _wishlistIcon;

        try
        {
            string? assemblyDirectory = Path.GetDirectoryName(typeof(WishlistService).Assembly.Location);
            if (string.IsNullOrWhiteSpace(assemblyDirectory))
                return null;

            string path = Path.Combine(
                assemblyDirectory,
                WishlistIconRelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(path))
                return null;

            var image = new Image();
            Error error = image.Load(path);
            if (error != Error.Ok)
            {
                MainFile.Logger.Warn($"Wishlist icon could not be loaded ({error}): {path}");
                return null;
            }

            _wishlistIcon = ImageTexture.CreateFromImage(image);
            return _wishlistIcon;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Wishlist icon load failed: {ex.Message}");
            return null;
        }
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
    {
        // Card reward / event screens populate holders inside their own
        // _Ready call. A decoration failure must never abort the vanilla loop
        // and leave only 1-2 cards (or remove Skip/alternate choices).
        Callable.From(() =>
        {
            try
            {
                if (GodotObject.IsInstanceValid(__instance))
                    WishlistService.RefreshCardHolder(__instance);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"Wishlist card decoration skipped: {ex.Message}");
            }
        }).CallDeferred();
    }
}

[HarmonyPatch(typeof(NGridCardHolder), "UpdateCardModel")]
internal static class WishlistGridCardReassignPatch
{
    private static void Postfix(NGridCardHolder __instance)
    {
        try
        {
            WishlistService.RefreshCardHolder(__instance);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Wishlist card refresh skipped: {ex.Message}");
        }
    }
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

    private static void Prefix(NCardLibraryGrid __instance, ref Func<CardModel, bool> __0)
    {
        // NCardLibraryGrid can be reused by other screens across game
        // branches/mods. Never let the wishlist-only filter leak into deck,
        // draw/discard or card-selection screens.
        if (!SpireGpsSettings.WishlistEnabled ||
            !WishlistService.CardLibraryWishlistOnly ||
            !WishlistService.IsActualCardLibraryGrid(__instance))
        {
            return;
        }

        var original = __0;
        __0 = card => original(card) && WishlistService.IsCardWishlisted(card);
    }
}
