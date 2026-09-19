using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using SpireGPS.Config;

namespace SpireGPS.Features.SynergyHints;

internal static class SynergyHintService
{
    private const string HintNodeName = "SynergyHint";

    private static readonly FieldInfo? MerchantCardNodeField =
        AccessTools.Field(typeof(NMerchantCard), "_cardNode");

    internal static IReadOnlyList<string> GetHints(CardModel? offered)
    {
        if (!SpireGpsSettings.SynergyHintsEnabled || offered is null || MainFile.RunState is null)
            return Array.Empty<string>();

        var player = LocalContext.GetMe(MainFile.RunState);
        if (player is null)
            return Array.Empty<string>();

        var ownedCards = player.Deck.Cards
            .Select(card => card.GetType().Name)
            .ToHashSet(StringComparer.Ordinal);

        var ownedRelics = player.Relics
            .Where(relic => !relic.IsMelted)
            .Select(relic => relic.GetType().Name)
            .ToHashSet(StringComparer.Ordinal);

        var hints = new List<string>();
        bool exhausts = offered.Keywords.Contains(CardKeyword.Exhaust) ||
                        offered.Keywords.Contains(CardKeyword.Ethereal);

        if (exhausts)
        {
            if (ownedCards.Contains("FeelNoPain"))
                hints.Add("Feel No Pain - Exhausting this can gain Block.");

            if (ownedCards.Contains("DarkEmbrace"))
                hints.Add("Dark Embrace - Exhausting this can draw a card.");

            if (ownedRelics.Contains("JossPaper"))
                hints.Add("Joss Paper - Exhausting this advances its draw counter.");

            if (offered.Type == CardType.Skill && ownedRelics.Contains("BurningSticks"))
                hints.Add("Burning Sticks - the first Exhausted Skill each combat is copied to your hand.");
        }

        if (offered.Type == CardType.Skill && ownedCards.Contains("Corruption"))
            hints.Add("Corruption - this Skill can cost 0 and Exhaust when played.");

        if (offered.Type == CardType.Power && ownedRelics.Contains("MummifiedHand"))
            hints.Add("Mummified Hand - playing this Power can make a random card in hand free.");

        if (offered.Type == CardType.Skill && ownedRelics.Contains("LetterOpener"))
            hints.Add("Letter Opener - playing this Skill advances its damage counter.");

        if (offered.Type == CardType.Attack)
        {
            string[] attackRelics =
            {
                "PenNib",
                "Nunchaku",
                "Kunai",
                "Shuriken",
                "OrnamentalFan"
            };

            var names = player.Relics
                .Where(relic => attackRelics.Contains(relic.GetType().Name, StringComparer.Ordinal))
                .Select(relic => relic.Title.GetFormattedText())
                .Distinct()
                .ToArray();

            if (names.Length > 0)
                hints.Add("Attack interactions - " + string.Join(", ", names) + ".");
        }

        return hints
            .Distinct(StringComparer.Ordinal)
            .Take(Math.Max(1, SpireGpsSettings.SynergyHintsMax))
            .ToArray();
    }

    internal static void RefreshGridCard(NGridCardHolder holder)
    {
        SetHint(
            holder,
            GetHints(holder.CardModel),
            new Vector2(8f, -28f));
    }

    internal static void RefreshMerchantCard(NMerchantCard merchant)
    {
        var cardNode = MerchantCardNodeField?.GetValue(merchant) as NCard;
        SetHint(
            merchant,
            GetHints(cardNode?.Model),
            new Vector2(8f, -26f));
    }

    internal static void RefreshAll()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        foreach (var holder in FindNodes<NGridCardHolder>(tree.Root))
            RefreshGridCard(holder);

        foreach (var merchant in FindNodes<NMerchantCard>(tree.Root))
            RefreshMerchantCard(merchant);
    }

    private static void SetHint(Control owner, IReadOnlyList<string> hints, Vector2 position)
    {
        var chip = owner.GetNodeOrNull<Label>(HintNodeName);
        if (chip is null)
        {
            chip = new Label
            {
                Name = HintNodeName,
                MouseFilter = Control.MouseFilterEnum.Stop,
                ZIndex = 102,
                Position = position
            };
            chip.AddThemeFontSizeOverride("font_size", 14);
            chip.AddThemeColorOverride("font_color", new Color("#F6C744"));
            chip.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.95f));
            chip.AddThemeConstantOverride("shadow_offset_x", 2);
            chip.AddThemeConstantOverride("shadow_offset_y", 2);
            owner.AddChild(chip);
        }

        chip.Position = position;
        chip.Visible = SpireGpsSettings.SynergyHintsEnabled && hints.Count > 0;

        if (!chip.Visible)
            return;

        chip.Text = hints.Count == 1 ? "Synergy" : $"Synergy {hints.Count}";
        chip.TooltipText = string.Join("\n", hints);
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
}

[HarmonyPatch(typeof(NGridCardHolder), nameof(NGridCardHolder._Ready))]
internal static class SynergyGridCardReadyPatch
{
    private static void Postfix(NGridCardHolder __instance)
        => SynergyHintService.RefreshGridCard(__instance);
}

[HarmonyPatch(typeof(NGridCardHolder), "UpdateCardModel")]
internal static class SynergyGridCardReassignPatch
{
    private static void Postfix(NGridCardHolder __instance)
        => SynergyHintService.RefreshGridCard(__instance);
}

[HarmonyPatch(typeof(NMerchantCard), "UpdateVisual")]
internal static class SynergyMerchantCardPatch
{
    private static void Postfix(NMerchantCard __instance)
        => SynergyHintService.RefreshMerchantCard(__instance);
}
