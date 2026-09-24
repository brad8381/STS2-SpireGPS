using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;
using SpireGPS.Config;

namespace SpireGPS.Features.TriggerInspector;

internal static class TriggerInspectorService
{
    private const string LayerName = "TriggerInspector";

    private static Control? _focusedOwner;
    private static CardModel? _focusedCard;
    private static RelicModel? _focusedRelic;

    internal static void FocusCard(CardModel? card, Control owner)
    {
        if (!SpireGpsSettings.TriggerInspectorEnabled)
            return;

        _focusedOwner = owner;
        _focusedCard = card;
        _focusedRelic = null;
        EnsureLayer();
    }

    internal static void FocusRelic(RelicModel? relic, Control owner)
    {
        if (!SpireGpsSettings.TriggerInspectorEnabled)
            return;

        _focusedOwner = owner;
        _focusedRelic = relic;
        _focusedCard = null;
        EnsureLayer();
    }

    internal static void Unfocus(Control owner)
    {
        if (ReferenceEquals(_focusedOwner, owner))
        {
            _focusedOwner = null;
            _focusedCard = null;
            _focusedRelic = null;
        }
    }

    internal static bool TryGetCurrent(out string title, out IReadOnlyList<string> lines)
    {
        title = string.Empty;
        lines = Array.Empty<string>();

        if (!SpireGpsSettings.TriggerInspectorEnabled ||
            _focusedOwner is null ||
            !GodotObject.IsInstanceValid(_focusedOwner) ||
            MainFile.RunState is null)
        {
            return false;
        }

        var player = LocalContext.GetMe(MainFile.RunState);
        if (player is null)
            return false;

        if (_focusedCard is not null)
        {
            title = _focusedCard.Title;
            lines = GetCardLinks(_focusedCard, player);
            return lines.Count > 0;
        }

        if (_focusedRelic is not null)
        {
            title = _focusedRelic.Title.GetFormattedText();
            lines = GetRelicLinks(_focusedRelic, player);
            return lines.Count > 0;
        }

        return false;
    }

    private static IReadOnlyList<string> GetCardLinks(CardModel card, Player player)
    {
        var lines = new List<string>();
        bool exhaust = card.Keywords.Contains(CardKeyword.Exhaust) ||
                       card.Keywords.Contains(CardKeyword.Ethereal);

        if (exhaust)
        {
            AddOwnedCard(player, "FeelNoPain", lines, "Feel No Pain reacts when this Exhausts.");
            AddOwnedCard(player, "DarkEmbrace", lines, "Dark Embrace reacts when this Exhausts.");
            AddOwnedRelic(player, "JossPaper", lines, "Joss Paper advances when this Exhausts.");

            if (card.Type == CardType.Skill)
                AddOwnedRelic(player, "BurningSticks", lines, "Burning Sticks can react to this Exhausted Skill.");
        }

        if (card.Type == CardType.Skill)
        {
            AddOwnedCard(player, "Corruption", lines, "Corruption can make this Skill free and Exhaust it.");
            AddOwnedRelic(player, "LetterOpener", lines, "Letter Opener advances when this Skill is played.");
        }

        if (card.Type == CardType.Power)
            AddOwnedRelic(player, "MummifiedHand", lines, "Mummified Hand reacts when this Power is played.");

        if (card.Type == CardType.Attack)
        {
            AddOwnedRelic(player, "PenNib", lines, "Pen Nib advances when this Attack is played.");
            AddOwnedRelic(player, "Nunchaku", lines, "Nunchaku advances when this Attack is played.");
            AddOwnedRelic(player, "Kunai", lines, "Kunai can advance when this Attack is played.");
            AddOwnedRelic(player, "Shuriken", lines, "Shuriken can advance when this Attack is played.");
            AddOwnedRelic(player, "OrnamentalFan", lines, "Ornamental Fan can advance when this Attack is played.");
        }

        return lines;
    }

    private static IReadOnlyList<string> GetRelicLinks(RelicModel relic, Player player)
    {
        IEnumerable<CardModel> cards = player.Deck.Cards;
        IEnumerable<CardModel> matching = Array.Empty<CardModel>();
        string type = relic.GetType().Name;

        switch (type)
        {
            case "MummifiedHand":
                matching = cards.Where(card => card.Type == CardType.Power);
                break;

            case "LetterOpener":
                matching = cards.Where(card => card.Type == CardType.Skill);
                break;

            case "PenNib":
            case "Nunchaku":
            case "Kunai":
            case "Shuriken":
            case "OrnamentalFan":
                matching = cards.Where(card => card.Type == CardType.Attack);
                break;

            case "JossPaper":
                matching = cards.Where(card =>
                    card.Keywords.Contains(CardKeyword.Exhaust) ||
                    card.Keywords.Contains(CardKeyword.Ethereal));
                break;

            case "BurningSticks":
                matching = cards.Where(card =>
                    card.Type == CardType.Skill &&
                    (card.Keywords.Contains(CardKeyword.Exhaust) ||
                     card.Keywords.Contains(CardKeyword.Ethereal)));
                break;

            default:
                return Array.Empty<string>();
        }

        var list = matching
            .Select(card => card.Title)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (list.Count == 0)
            return new[] { "No cards in your current deck trigger this known interaction." };

        const int maxNames = 12;
        var lines = list.Take(maxNames).Select(name => $"• {name}").ToList();

        if (list.Count > maxNames)
            lines.Add($"• +{list.Count - maxNames} more");

        return lines;
    }

    private static void AddOwnedCard(Player player, string typeName, ICollection<string> lines, string message)
    {
        if (player.Deck.Cards.Any(card => card.GetType().Name == typeName))
            lines.Add(message);
    }

    private static void AddOwnedRelic(Player player, string typeName, ICollection<string> lines, string message)
    {
        if (player.Relics.Any(relic => !relic.IsMelted && relic.GetType().Name == typeName))
            lines.Add(message);
    }

    private static void EnsureLayer()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        if (tree.Root.GetNodeOrNull<TriggerInspectorLayer>(LayerName) is not null)
            return;

        tree.Root.AddChild(new TriggerInspectorLayer { Name = LayerName });
    }
}

internal partial class TriggerInspectorLayer : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _title = null!;
    private Label _body = null!;

    public TriggerInspectorLayer()
    {
        Layer = 130;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        _panel = new PanelContainer
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(330f, 0f)
        };

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.035f, 0.04f, 0.05f, 0.96f),
            BorderColor = new Color(0.55f, 0.48f, 0.32f, 0.95f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 7,
            CornerRadiusTopRight = 7,
            CornerRadiusBottomLeft = 7,
            CornerRadiusBottomRight = 7,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 10,
            ContentMarginBottom = 10
        };
        _panel.AddThemeStyleboxOverride("panel", style);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 5);
        _panel.AddChild(box);

        _title = new Label();
        _title.AddThemeFontSizeOverride("font_size", 18);
        box.AddChild(_title);

        var header = new Label { Text = "Trigger Inspector" };
        header.AddThemeFontSizeOverride("font_size", 13);
        header.AddThemeColorOverride("font_color", new Color("#F6C744"));
        box.AddChild(header);

        _body = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(300f, 0f)
        };
        _body.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(_body);

        AddChild(_panel);
    }

    public override void _Process(double delta)
    {
        bool ctrl = Input.IsKeyPressed(Key.Ctrl);

        if (!ctrl ||
            !TriggerInspectorService.TryGetCurrent(out string title, out var lines))
        {
            _panel.Visible = false;
            return;
        }

        _title.Text = title;
        _body.Text = string.Join("\n", lines);
        _panel.Visible = true;
        _panel.ResetSize();

        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        Vector2 mouse = GetViewport().GetMousePosition();
        Vector2 desired = mouse + new Vector2(22f, 18f);

        _panel.Position = new Vector2(
            Math.Clamp(desired.X, 8f, Math.Max(8f, viewport.X - _panel.Size.X - 8f)),
            Math.Clamp(desired.Y, 8f, Math.Max(8f, viewport.Y - _panel.Size.Y - 8f)));
    }
}

[HarmonyPatch(typeof(NGridCardHolder), nameof(NGridCardHolder._Ready))]
internal static class TriggerInspectorCardPatch
{
    private static void Postfix(NGridCardHolder __instance)
    {
        Callable.From(() =>
        {
            try
            {
                if (!GodotObject.IsInstanceValid(__instance) ||
                    !GodotObject.IsInstanceValid(__instance.Hitbox) ||
                    __instance.Hitbox.HasMeta("banter_trigger_inspector"))
                {
                    return;
                }

                __instance.Hitbox.SetMeta("banter_trigger_inspector", true);
                __instance.Hitbox.Connect(
                    NClickableControl.SignalName.Focused,
                    Callable.From<NClickableControl>(_ =>
                        TriggerInspectorService.FocusCard(__instance.CardModel, __instance)));

                __instance.Hitbox.Connect(
                    NClickableControl.SignalName.Unfocused,
                    Callable.From<NClickableControl>(_ =>
                        TriggerInspectorService.Unfocus(__instance)));
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"Trigger Inspector card hook skipped: {ex.Message}");
            }
        }).CallDeferred();
    }
}

[HarmonyPatch(typeof(NRelicBasicHolder), nameof(NRelicBasicHolder._Ready))]
internal static class TriggerInspectorBasicRelicPatch
{
    private static void Postfix(NRelicBasicHolder __instance)
    {
        __instance.Connect(
            NClickableControl.SignalName.Focused,
            Callable.From<NClickableControl>(_ =>
                TriggerInspectorService.FocusRelic(__instance.Relic?.Model, __instance)));

        __instance.Connect(
            NClickableControl.SignalName.Unfocused,
            Callable.From<NClickableControl>(_ =>
                TriggerInspectorService.Unfocus(__instance)));
    }
}

[HarmonyPatch(typeof(NRelicCollectionEntry), nameof(NRelicCollectionEntry._Ready))]
internal static class TriggerInspectorCollectionRelicPatch
{
    private static void Postfix(NRelicCollectionEntry __instance)
    {
        if (__instance.ModelVisibility != ModelVisibility.Visible)
            return;

        __instance.Connect(
            NClickableControl.SignalName.Focused,
            Callable.From<NClickableControl>(_ =>
                TriggerInspectorService.FocusRelic(__instance.relic, __instance)));

        __instance.Connect(
            NClickableControl.SignalName.Unfocused,
            Callable.From<NClickableControl>(_ =>
                TriggerInspectorService.Unfocus(__instance)));
    }
}
