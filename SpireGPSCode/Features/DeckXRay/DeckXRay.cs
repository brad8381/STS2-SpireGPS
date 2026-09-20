using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens;
using SpireGPS.Config;

namespace SpireGPS.Features.DeckXRay;

internal static class DeckXRayService
{
    private static readonly FieldInfo? PlayerField =
        AccessTools.Field(typeof(NDeckViewScreen), "_player");

    internal static void Attach(NDeckViewScreen screen)
    {
        if (screen.GetNodeOrNull<DeckXRayPanel>("DeckXRayPanel") is not null)
            return;

        var player = PlayerField?.GetValue(screen) as Player;
        if (player is null)
            return;

        screen.AddChild(new DeckXRayPanel(screen, player)
        {
            Name = "DeckXRayPanel"
        });
    }

    internal static bool Matches(CardModel card, string key)
    {
        return key switch
        {
            "Attack" => card.Type == CardType.Attack,
            "Skill" => card.Type == CardType.Skill,
            "Power" => card.Type == CardType.Power,
            "Cost0" => card.EnergyCost.Canonical == 0,
            "Cost1" => card.EnergyCost.Canonical == 1,
            "Cost2" => card.EnergyCost.Canonical == 2,
            "Cost3Plus" => card.EnergyCost.Canonical >= 3,
            "Upgraded" => card.IsUpgraded,
            "Unupgraded" => !card.IsUpgraded && card.IsUpgradable,
            "Exhaust" => card.Keywords.Contains(CardKeyword.Exhaust),
            "Ethereal" => card.Keywords.Contains(CardKeyword.Ethereal),
            "Retain" => card.Keywords.Contains(CardKeyword.Retain),
            "Innate" => card.Keywords.Contains(CardKeyword.Innate),
            "Unplayable" => card.Keywords.Contains(CardKeyword.Unplayable),
            "Block" => card.DynamicVars.ContainsKey("Block") || card.DynamicVars.ContainsKey("CalculatedBlock"),
            "AoE" => card.TargetType == TargetType.AllEnemies,
            "Draw" => CardTextContains(card, "draw"),
            "EnergySource" => card.DynamicVars.ContainsKey("Energy") && CardTextContains(card, "gain"),
            _ => true
        };
    }

    internal static int Count(IEnumerable<CardModel> cards, string key)
        => cards.Count(card => Matches(card, key));

    private static bool CardTextContains(CardModel card, string text)
    {
        try
        {
            return card.GetDescriptionForPile(PileType.Deck)
                .Contains(text, StringComparison.CurrentCultureIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

internal partial class DeckXRayPanel : PanelContainer
{
    private readonly NDeckViewScreen _screen;
    private readonly Player _player;
    private readonly Dictionary<string, Button> _filterButtons = new();

    private VBoxContainer _content = null!;
    private Label _summary = null!;
    private string? _activeFilter;
    private int _lastDeckCount = -1;
    private ulong _nextRefreshAt;

    internal DeckXRayPanel(NDeckViewScreen screen, Player player)
    {
        _screen = screen;
        _player = player;

        MouseFilter = Control.MouseFilterEnum.Stop;
        ZIndex = 80;
        AnchorLeft = 1f;
        AnchorRight = 1f;
        AnchorTop = 0f;
        AnchorBottom = 0f;
        OffsetLeft = -318f;
        OffsetRight = -18f;
        OffsetTop = 90f;
        OffsetBottom = 670f;
        CustomMinimumSize = new Vector2(300f, 420f);
    }

    public override void _Ready()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.035f, 0.04f, 0.05f, 0.94f),
            BorderColor = new Color(0.55f, 0.48f, 0.32f, 0.85f),
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
        AddThemeStyleboxOverride("panel", style);

        _content = new VBoxContainer();
        _content.AddThemeConstantOverride("separation", 5);
        AddChild(_content);

        var title = new Label
        {
            Text = "Deck X-Ray",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 20);
        _content.AddChild(title);

        _summary = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _summary.AddThemeFontSizeOverride("font_size", 14);
        _content.AddChild(_summary);

        AddSection("TYPE", new[]
        {
            ("Attack", "Attack"),
            ("Skill", "Skill"),
            ("Power", "Power")
        });

        AddSection("COST", new[]
        {
            ("Cost0", "0"),
            ("Cost1", "1"),
            ("Cost2", "2"),
            ("Cost3Plus", "3+")
        });

        AddSection("STATE", new[]
        {
            ("Upgraded", "Upgraded"),
            ("Unupgraded", "Unupgraded")
        });

        AddSection("KEYWORDS", new[]
        {
            ("Exhaust", "Exhaust"),
            ("Ethereal", "Ethereal"),
            ("Retain", "Retain"),
            ("Innate", "Innate"),
            ("Unplayable", "Unplayable")
        });

        AddSection("UTILITY", new[]
        {
            ("Block", "Block"),
            ("AoE", "AoE"),
            ("Draw", "Draw"),
            ("EnergySource", "Energy")
        });

        var clear = new Button
        {
            Text = "Clear Highlight",
            FocusMode = Control.FocusModeEnum.None
        };
        clear.Pressed += () => SetFilter(null);
        _content.AddChild(clear);

        RefreshCounts();
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_screen))
        {
            QueueFree();
            return;
        }

        if (!SpireGpsSettings.DeckXRayEnabled)
        {
            Visible = false;
            ClearHighlight();
            return;
        }

        Visible = true;

        ulong now = Time.GetTicksMsec();
        if (_lastDeckCount != _player.Deck.Cards.Count || now >= _nextRefreshAt)
        {
            _nextRefreshAt = now + 500;
            RefreshCounts();
        }

        ApplyHighlight();
    }

    private void AddSection(string title, IEnumerable<(string key, string label)> entries)
    {
        var header = new Label { Text = title };
        header.AddThemeFontSizeOverride("font_size", 13);
        _content.AddChild(header);

        var wrap = new HFlowContainer();
        wrap.AddThemeConstantOverride("h_separation", 4);
        wrap.AddThemeConstantOverride("v_separation", 4);
        _content.AddChild(wrap);

        foreach (var entry in entries)
        {
            var button = new Button
            {
                FocusMode = Control.FocusModeEnum.None,
                ToggleMode = true,
                CustomMinimumSize = new Vector2(82f, 28f)
            };

            string key = entry.key;
            button.Pressed += () => SetFilter(_activeFilter == key ? null : key);
            _filterButtons[key] = button;
            wrap.AddChild(button);
        }
    }

    private void SetFilter(string? key)
    {
        _activeFilter = key;

        foreach (var pair in _filterButtons)
            pair.Value.SetPressedNoSignal(pair.Key == key);

        ApplyHighlight();
    }

    private void RefreshCounts()
    {
        var cards = _player.Deck.Cards;
        _lastDeckCount = cards.Count;

        int upgraded = cards.Count(card => card.IsUpgraded);
        _summary.Text = $"{cards.Count} cards - {upgraded}/{cards.Count} upgraded";

        var labels = new Dictionary<string, string>
        {
            ["Attack"] = "Attack",
            ["Skill"] = "Skill",
            ["Power"] = "Power",
            ["Cost0"] = "0 cost",
            ["Cost1"] = "1 cost",
            ["Cost2"] = "2 cost",
            ["Cost3Plus"] = "3+ cost",
            ["Upgraded"] = "Upgraded",
            ["Unupgraded"] = "Unupgraded",
            ["Exhaust"] = "Exhaust",
            ["Ethereal"] = "Ethereal",
            ["Retain"] = "Retain",
            ["Innate"] = "Innate",
            ["Unplayable"] = "Unplayable",
            ["Block"] = "Block",
            ["AoE"] = "AoE",
            ["Draw"] = "Draw",
            ["EnergySource"] = "Energy"
        };

        foreach (var pair in _filterButtons)
        {
            int count = DeckXRayService.Count(cards, pair.Key);
            pair.Value.Text = $"{labels[pair.Key]} {count}";
            pair.Value.Disabled = count == 0;
            pair.Value.SetPressedNoSignal(pair.Key == _activeFilter);
        }
    }

    private void ClearHighlight()
    {
        var grid = _screen.GetNodeOrNull<NCardGrid>("CardGrid");
        if (grid is null)
            return;

        foreach (NGridCardHolder holder in grid.CurrentlyDisplayedCardHolders)
            holder.Modulate = Colors.White;
    }

    private void ApplyHighlight()
    {
        var grid = _screen.GetNodeOrNull<NCardGrid>("CardGrid");
        if (grid is null)
            return;

        foreach (NGridCardHolder holder in grid.CurrentlyDisplayedCardHolders)
        {
            var card = holder.CardModel;
            bool match = _activeFilter is null || (card is not null && DeckXRayService.Matches(card, _activeFilter));
            holder.Modulate = match ? Colors.White : new Color(1f, 1f, 1f, 0.18f);
        }
    }
}

[HarmonyPatch(typeof(NDeckViewScreen), nameof(NDeckViewScreen._Ready))]
internal static class DeckXRayDeckScreenPatch
{
    private static void Postfix(NDeckViewScreen __instance)
    {
        try
        {
            DeckXRayService.Attach(__instance);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Deck X-Ray setup failed open: {ex}");
        }
    }
}
