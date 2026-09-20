using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rewards;
using SpireGPS.Config;
using SpireGPS.Features.SynergyHints;
using SpireGPS.Features.Wishlist;
using SpireGPS.UI;

namespace SpireGPS.Features.ChoiceCompare;

internal sealed record CompareEntry(
    string Key,
    string Kind,
    string Title,
    string Body);

internal static class ChoiceCompareService
{
    private const string LayerName = "ChoiceCompareLayer";

    private static readonly List<CompareEntry> Entries = new();

    private static readonly FieldInfo? MerchantCardNodeField =
        AccessTools.Field(typeof(NMerchantCard), "_cardNode");

    private static readonly FieldInfo? MerchantRelicNodeField =
        AccessTools.Field(typeof(NMerchantRelic), "_relicNode");

    private static readonly FieldInfo? MerchantPotionNodeField =
        AccessTools.Field(typeof(NMerchantPotion), "_potionNode");

    private static readonly FieldInfo? RelicRewardField =
        AccessTools.Field(typeof(RelicReward), "_relic");

    internal static event Action? Changed;

    internal static IReadOnlyList<CompareEntry> Current => Entries;

    internal static bool TryPinGridCard(NGridCardHolder holder)
    {
        if (!SpireGpsSettings.ChoiceCompareEnabled || holder.CardModel is null)
            return false;

        if (!HasAncestor<NCardRewardSelectionScreen>(holder))
            return false;

        PinCard(holder.CardModel);
        return true;
    }

    internal static bool TryPinMerchantSlot(NMerchantSlot slot)
    {
        if (!SpireGpsSettings.ChoiceCompareEnabled)
            return false;

        switch (slot)
        {
            case NMerchantCard cardSlot:
            {
                var node = MerchantCardNodeField?.GetValue(cardSlot) as NCard;
                if (node?.Model is null)
                    return false;

                PinCard(node.Model);
                return true;
            }

            case NMerchantRelic relicSlot:
            {
                var node = MerchantRelicNodeField?.GetValue(relicSlot) as MegaCrit.Sts2.Core.Nodes.Relics.NRelic;
                if (node?.Model is null)
                    return false;

                PinRelic(node.Model);
                return true;
            }

            case NMerchantPotion potionSlot:
            {
                var node = MerchantPotionNodeField?.GetValue(potionSlot) as MegaCrit.Sts2.Core.Nodes.Potions.NPotion;
                if (node?.Model is null)
                    return false;

                PinPotion(node.Model);
                return true;
            }
        }

        return false;
    }

    internal static bool TryPinRewardButton(NRewardButton button)
    {
        if (!SpireGpsSettings.ChoiceCompareEnabled || button.Reward is null)
            return false;

        switch (button.Reward)
        {
            case RelicReward relicReward:
            {
                var relic = RelicRewardField?.GetValue(relicReward) as RelicModel;
                if (relic is null)
                    return false;

                PinRelic(relic);
                return true;
            }

            case PotionReward potionReward when potionReward.Potion is not null:
                PinPotion(potionReward.Potion);
                return true;

            default:
                return false;
        }
    }

    internal static void PinTreasureRelic(NTreasureRoomRelicHolder holder)
    {
        if (!SpireGpsSettings.ChoiceCompareEnabled || holder.Relic?.Model is null)
            return;

        PinRelic(holder.Relic.Model);
    }

    internal static void Remove(string key)
    {
        Entries.RemoveAll(entry => entry.Key == key);
        Changed?.Invoke();
    }

    internal static void Clear()
    {
        Entries.Clear();
        Changed?.Invoke();
    }

    private static void PinCard(CardModel card)
    {
        string key = "card:" + card.Id;
        Pin(new CompareEntry(key, "Card", card.Title, BuildCardBody(card)));
    }

    private static void PinRelic(RelicModel relic)
    {
        string key = "relic:" + relic.Id;
        Pin(new CompareEntry(key, "Relic", relic.Title.GetFormattedText(), BuildRelicBody(relic)));
    }

    private static void PinPotion(PotionModel potion)
    {
        string key = "potion:" + potion.Id;
        Pin(new CompareEntry(key, "Potion", potion.Title.GetFormattedText(), BuildPotionBody(potion)));
    }

    private static void Pin(CompareEntry entry)
    {
        int existing = Entries.FindIndex(item => item.Key == entry.Key);
        if (existing >= 0)
        {
            Entries[existing] = entry;
            EnsureLayer();
            Changed?.Invoke();
            return;
        }

        if (Entries.Count >= 3)
        {
            SpireGpsToast.Show("Choice Compare: remove one of the 3 pinned items first.");
            return;
        }

        Entries.Add(entry);
        EnsureLayer();
        Changed?.Invoke();
    }

    private static string BuildCardBody(CardModel card)
    {
        var lines = new List<string>
        {
            $"Type: {card.Type}",
            $"Rarity: {card.Rarity}",
            $"Cost: {(card.EnergyCost.CostsX ? "X" : card.EnergyCost.Canonical.ToString())}",
            $"Upgrade: {(card.IsUpgraded ? "Upgraded" : card.IsUpgradable ? "Available" : "None")}"
        };

        if (card.Keywords.Count > 0)
            lines.Add("Keywords: " + string.Join(", ", card.Keywords));

        var player = GetLocalPlayer();
        if (player is not null)
        {
            int copies = player.Deck.Cards.Count(owned => owned.Id == card.Id);
            lines.Add($"Owned copies: {copies}");
        }

        lines.Add($"Wishlist: {(WishlistService.IsCardWishlisted(card) ? "Yes" : "No")}");

        var hints = SynergyHintService.GetHints(card);
        if (hints.Count > 0)
        {
            lines.Add("");
            lines.Add("Interactions:");
            lines.AddRange(hints.Select(hint => "• " + hint));
        }

        return string.Join("\n", lines);
    }

    private static string BuildRelicBody(RelicModel relic)
    {
        var lines = new List<string>
        {
            $"Rarity: {relic.Rarity}"
        };

        var player = GetLocalPlayer();
        if (player is not null)
            lines.Add($"Already owned: {(player.Relics.Any(owned => owned.Id == relic.Id && !owned.IsMelted) ? "Yes" : "No")}");

        lines.Add($"Wishlist: {(WishlistService.IsRelicWishlisted(relic) ? "Yes" : "No")}");

        var hints = SynergyHintService.GetHints(relic);
        if (hints.Count > 0)
        {
            lines.Add("");
            lines.Add("Interactions:");
            lines.AddRange(hints.Select(hint => "• " + hint));
        }

        return string.Join("\n", lines);
    }

    private static string BuildPotionBody(PotionModel potion)
    {
        return string.Join("\n", new[]
        {
            $"Rarity: {potion.Rarity}",
            $"Target: {potion.TargetType}",
            $"Usage: {potion.Usage}"
        });
    }

    private static MegaCrit.Sts2.Core.Entities.Players.Player? GetLocalPlayer()
    {
        if (MainFile.RunState is null)
            return null;

        return LocalContext.GetMe(MainFile.RunState);
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

    private static void EnsureLayer()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        if (tree.Root.GetNodeOrNull<ChoiceCompareLayer>(LayerName) is not null)
            return;

        tree.Root.AddChild(new ChoiceCompareLayer { Name = LayerName });
    }
}

internal partial class ChoiceCompareLayer : CanvasLayer
{
    private PanelContainer _panel = null!;
    private HBoxContainer _columns = null!;

    public ChoiceCompareLayer()
    {
        Layer = 128;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        _panel = new PanelContainer
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Position = new Vector2(0f, 0f)
        };

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.035f, 0.045f, 0.97f),
            BorderColor = new Color(0.75f, 0.62f, 0.28f, 0.92f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 8,
            ContentMarginBottom = 8
        };
        _panel.AddThemeStyleboxOverride("panel", style);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 6);
        _panel.AddChild(outer);

        var top = new HBoxContainer();
        outer.AddChild(top);

        var title = new Label
        {
            Text = "Choice Compare",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        title.AddThemeFontSizeOverride("font_size", 19);
        top.AddChild(title);

        var clear = new Button
        {
            Text = "Clear",
            FocusMode = Control.FocusModeEnum.None
        };
        clear.Pressed += ChoiceCompareService.Clear;
        top.AddChild(clear);

        _columns = new HBoxContainer();
        _columns.AddThemeConstantOverride("separation", 8);
        outer.AddChild(_columns);

        AddChild(_panel);

        ChoiceCompareService.Changed += Refresh;
        Refresh();
    }

    public override void _ExitTree()
    {
        ChoiceCompareService.Changed -= Refresh;
    }

    private void Refresh()
    {
        foreach (Node child in _columns.GetChildren())
            child.QueueFree();

        var entries = ChoiceCompareService.Current;
        _panel.Visible = entries.Count > 0;

        if (entries.Count == 0)
            return;

        foreach (var entry in entries)
            _columns.AddChild(BuildColumn(entry));

        _panel.ResetSize();
        Callable.From(PositionPanel).CallDeferred();
    }

    private Control BuildColumn(CompareEntry entry)
    {
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(280f, 250f)
        };

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.075f, 0.085f, 0.96f),
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            ContentMarginLeft = 9,
            ContentMarginRight = 9,
            ContentMarginTop = 8,
            ContentMarginBottom = 8
        };
        panel.AddThemeStyleboxOverride("panel", style);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        panel.AddChild(box);

        var kind = new Label { Text = entry.Kind };
        kind.AddThemeFontSizeOverride("font_size", 12);
        kind.AddThemeColorOverride("font_color", new Color("#F6C744"));
        box.AddChild(kind);

        var title = new Label
        {
            Text = entry.Title,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        title.AddThemeFontSizeOverride("font_size", 18);
        box.AddChild(title);

        var body = new Label
        {
            Text = entry.Body,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        body.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(body);

        var remove = new Button
        {
            Text = "Remove",
            FocusMode = Control.FocusModeEnum.None
        };
        remove.Pressed += () => ChoiceCompareService.Remove(entry.Key);
        box.AddChild(remove);

        return panel;
    }

    private void PositionPanel()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        _panel.Position = new Vector2(
            Math.Max(8f, (viewport.X - _panel.Size.X) * 0.5f),
            90f);
    }
}

[HarmonyPatch(typeof(NMerchantSlot), "OnMouseReleased")]
internal static class ChoiceCompareMerchantPatch
{
    private static bool Prefix(NMerchantSlot __instance, InputEvent inputEvent)
    {
        if (!SpireGpsSettings.ChoiceCompareEnabled ||
            inputEvent is not InputEventMouseButton mouse ||
            mouse.Pressed ||
            mouse.ButtonIndex != MouseButton.Right)
        {
            return true;
        }

        return !ChoiceCompareService.TryPinMerchantSlot(__instance);
    }
}

[HarmonyPatch(typeof(NRewardButton), nameof(NRewardButton._Ready))]
internal static class ChoiceCompareRewardButtonPatch
{
    private static void Postfix(NRewardButton __instance)
    {
        __instance.Connect(
            Control.SignalName.GuiInput,
            Callable.From<InputEvent>(evt =>
            {
                if (!SpireGpsSettings.ChoiceCompareEnabled ||
                    evt is not InputEventMouseButton mouse ||
                    mouse.Pressed ||
                    mouse.ButtonIndex != MouseButton.Right)
                {
                    return;
                }

                if (ChoiceCompareService.TryPinRewardButton(__instance))
                    __instance.AcceptEvent();
            }));
    }
}

[HarmonyPatch(typeof(NTreasureRoomRelicHolder), nameof(NTreasureRoomRelicHolder._Ready))]
internal static class ChoiceCompareTreasureRelicPatch
{
    private static void Postfix(NTreasureRoomRelicHolder __instance)
    {
        __instance.Connect(
            Control.SignalName.GuiInput,
            Callable.From<InputEvent>(evt =>
            {
                if (!SpireGpsSettings.ChoiceCompareEnabled ||
                    evt is not InputEventMouseButton mouse ||
                    mouse.Pressed ||
                    mouse.ButtonIndex != MouseButton.Right)
                {
                    return;
                }

                ChoiceCompareService.PinTreasureRelic(__instance);
                __instance.AcceptEvent();
            }));
    }
}
