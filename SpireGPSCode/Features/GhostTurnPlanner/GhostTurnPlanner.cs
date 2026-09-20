using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using SpireGPS.Config;
using SpireGPS.UI;

namespace SpireGPS.Features.GhostTurnPlanner;

internal sealed record GhostPlanStep(
    CardModel Card,
    uint? TargetCombatId,
    string TargetLabel);

internal static class GhostTurnPlannerService
{
    private const string PanelName = "GhostTurnPlannerPanel";
    private const string RemoteLayerName = "GhostRemotePlans";

    private static readonly List<GhostPlanStep> Steps = new();

    private static INetGameService? _netService;
    private static bool _handlerRegistered;

    private static CardModel? _pendingCard;
    private static bool _active;
    private static bool _sharePlan = true;

    internal static event Action? Changed;

    internal static bool IsActive => SpireGpsSettings.GhostTurnPlannerEnabled && _active;
    internal static bool IsAwaitingTarget => IsActive && _pendingCard is not null;
    internal static IReadOnlyList<GhostPlanStep> CurrentSteps => Steps;
    internal static CardModel? PendingCard => _pendingCard;
    internal static bool SharePlan => _sharePlan;

    internal static void Attach(NCombatUi ui)
    {
        if (!SpireGpsSettings.GhostTurnPlannerEnabled)
            return;

        EnsureNetwork();

        if (ui.GetNodeOrNull<GhostTurnPlannerPanel>(PanelName) is not null)
            return;

        ui.AddChild(new GhostTurnPlannerPanel
        {
            Name = PanelName
        });
    }

    internal static void ResetForCombat()
    {
        Steps.Clear();
        _pendingCard = null;
        _active = false;
        BroadcastPlan();
        Changed?.Invoke();
    }

    internal static void SetActive(bool active)
    {
        _active = active;
        if (!active)
            _pendingCard = null;

        Changed?.Invoke();
    }

    internal static void SetShare(bool share)
    {
        _sharePlan = share;
        LocalPreferences.Set("ghost_turn_planner", "share", share);
        BroadcastPlan();
        Changed?.Invoke();
    }

    internal static void Clear()
    {
        Steps.Clear();
        _pendingCard = null;
        BroadcastPlan();
        Changed?.Invoke();
    }

    internal static void RemoveAt(int index)
    {
        if (index < 0 || index >= Steps.Count)
            return;

        Steps.RemoveAt(index);
        BroadcastPlan();
        Changed?.Invoke();
    }

    internal static bool TryQueueCard(CardModel? card)
    {
        if (!IsActive || card is null)
            return false;

        if (card.Pile?.Type != PileType.Hand)
            return false;

        if (card.Keywords.Contains(CardKeyword.Unplayable))
        {
            SpireGpsToast.Show("Ghost Planner: that card is currently Unplayable.");
            return true;
        }

        switch (card.TargetType)
        {
            case TargetType.AnyEnemy:
            case TargetType.AnyPlayer:
            case TargetType.AnyAlly:
                _pendingCard = card;
                SpireGpsToast.Show($"Ghost Planner: right-click a target for {card.Title}.");
                Changed?.Invoke();
                return true;

            case TargetType.AllEnemies:
                AddStep(card, null, "All enemies");
                return true;

            case TargetType.RandomEnemy:
                AddStep(card, null, "Random enemy ?");
                return true;

            case TargetType.AllAllies:
                AddStep(card, null, "All allies");
                return true;

            case TargetType.Self:
                AddStep(card, card.Owner?.Creature.CombatId, "Self");
                return true;

            case TargetType.Osty:
                AddStep(card, null, "Osty");
                return true;

            case TargetType.TargetedNoCreature:
                AddStep(card, null, "Special target ?");
                return true;

            case TargetType.None:
            default:
                AddStep(card, null, "No target");
                return true;
        }
    }

    internal static bool TryAssignTarget(NCreature creature)
    {
        if (!IsAwaitingTarget || _pendingCard is null)
            return false;

        if (creature.Entity.CombatId is not uint combatId)
            return false;

        if (!TargetAllowed(_pendingCard, creature.Entity))
        {
            SpireGpsToast.Show($"Ghost Planner: invalid target for {_pendingCard.Title}.");
            return true;
        }

        string label = creature.Entity.IsEnemy
            ? creature.Entity.Monster?.Title.GetFormattedText() ?? "Enemy"
            : creature.Entity.Player?.Character.Title.GetFormattedText() ?? "Player";

        var card = _pendingCard;
        _pendingCard = null;
        AddStep(card, combatId, label);
        return true;
    }

    internal static (int startingEnergy, int remainingEnergy, bool overBudget) CalculateEnergy()
    {
        var player = GetLocalPlayer();
        int starting = player?.PlayerCombatState?.Energy ?? 0;
        int remaining = starting;
        bool over = false;

        foreach (var step in Steps)
        {
            int cost = GetPlannedCost(step.Card, remaining);
            if (cost > remaining)
                over = true;
            remaining = Math.Max(0, remaining - cost);
        }

        return (starting, remaining, over);
    }

    internal static string DescribeStep(GhostPlanStep step, int energyBefore)
    {
        int cost = GetPlannedCost(step.Card, energyBefore);
        var pieces = new List<string>
        {
            $"{step.Card.Title} -> {step.TargetLabel}",
            step.Card.EnergyCost.CostsX ? $"X={cost}" : $"{cost}E"
        };

        try
        {
            if (step.Card.DynamicVars.TryGetValue("Damage", out var damage))
            {
                int repeat = 1;
                if (step.Card.DynamicVars.TryGetValue("Repeat", out var repeatVar))
                    repeat = Math.Max(1, repeatVar.IntValue);

                pieces.Add(repeat > 1
                    ? $"DMG {damage.IntValue}x{repeat}"
                    : $"DMG {damage.IntValue}");
            }

            if (step.Card.DynamicVars.TryGetValue("Block", out var block))
                pieces.Add($"BLK {block.IntValue}");

            if (step.Card.DynamicVars.TryGetValue("PoisonPower", out var poison))
                pieces.Add($"Poison {poison.IntValue}");
        }
        catch
        {
            // Dynamic vars are optional and modded cards may expose unusual data.
        }

        return string.Join(" | ", pieces);
    }

    internal static string BuildSummary()
    {
        if (Steps.Count == 0)
            return string.Empty;

        int remaining = GetLocalPlayer()?.PlayerCombatState?.Energy ?? 0;
        var lines = new List<string>();

        for (int i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];
            lines.Add($"{i + 1}. {DescribeStep(step, remaining)}");
            remaining = Math.Max(0, remaining - GetPlannedCost(step.Card, remaining));
        }

        return string.Join("\n", lines);
    }

    private static void AddStep(CardModel card, uint? targetCombatId, string targetLabel)
    {
        Steps.Add(new GhostPlanStep(card, targetCombatId, targetLabel));
        BroadcastPlan();
        Changed?.Invoke();
    }

    private static bool TargetAllowed(CardModel card, Creature creature)
    {
        return card.TargetType switch
        {
            TargetType.AnyEnemy => creature.IsEnemy && creature.IsAlive,
            TargetType.AnyPlayer => creature.IsPlayer && creature.IsAlive,
            TargetType.AnyAlly => creature.IsPlayer && creature.IsAlive && creature != card.Owner?.Creature,
            _ => true
        };
    }

    private static int GetPlannedCost(CardModel card, int remainingEnergy)
    {
        try
        {
            if (card.EnergyCost.CostsX)
                return Math.Max(0, remainingEnergy);

            return Math.Max(0, card.EnergyCost.GetWithModifiers(CostModifiers.All));
        }
        catch
        {
            return Math.Max(0, card.EnergyCost.Canonical);
        }
    }

    private static Player? GetLocalPlayer()
    {
        if (MainFile.RunState is null)
            return null;

        return LocalContext.GetMe(MainFile.RunState);
    }

    private static void EnsureNetwork()
    {
        var service = RunManager.Instance?.NetService;
        if (service is null)
            return;

        if (ReferenceEquals(_netService, service) && _handlerRegistered)
            return;

        if (_handlerRegistered && _netService is not null)
        {
            try { _netService.UnregisterMessageHandler<GhostPlanMessage>(HandleRemotePlan); }
            catch { }
        }

        _netService = service;
        _netService.RegisterMessageHandler<GhostPlanMessage>(HandleRemotePlan);
        _handlerRegistered = true;

        _sharePlan = LocalPreferences.GetBool("ghost_turn_planner", "share", true);
    }

    private static void BroadcastPlan()
    {
        EnsureNetwork();

        if (_netService is null ||
            !_sharePlan ||
            _netService.Type is not (NetGameType.Host or NetGameType.Client))
        {
            return;
        }

        _netService.SendMessage(new GhostPlanMessage
        {
            Summary = BuildSummary()
        });
    }

    private static void HandleRemotePlan(GhostPlanMessage message, ulong senderId)
    {
        if (_netService is null || senderId == _netService.NetId)
            return;

        var layer = EnsureRemoteLayer();
        layer?.SetPlan(senderId, message.Summary);
    }

    private static GhostRemotePlanLayer? EnsureRemoteLayer()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return null;

        var existing = tree.Root.GetNodeOrNull<GhostRemotePlanLayer>(RemoteLayerName);
        if (existing is not null)
            return existing;

        var layer = new GhostRemotePlanLayer { Name = RemoteLayerName };
        tree.Root.AddChild(layer);
        return layer;
    }
}

public sealed class GhostPlanMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => true;
    public bool ShouldBuffer => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;

    public string Summary = string.Empty;

    public void Serialize(PacketWriter writer)
        => writer.WriteString(Summary ?? string.Empty);

    public void Deserialize(PacketReader reader)
        => Summary = reader.ReadString();
}

internal partial class GhostTurnPlannerPanel : PanelContainer
{
    private VBoxContainer _steps = null!;
    private Button _toggle = null!;
    private Label _status = null!;
    private CheckBox _share = null!;

    public GhostTurnPlannerPanel()
    {
        MouseFilter = Control.MouseFilterEnum.Stop;
        ZIndex = 95;
        AnchorLeft = 1f;
        AnchorRight = 1f;
        AnchorTop = 0.36f;
        AnchorBottom = 0.36f;
        OffsetLeft = -430f;
        OffsetRight = -18f;
        OffsetTop = -90f;
        OffsetBottom = 250f;
        CustomMinimumSize = new Vector2(400f, 220f);
    }

    public override void _Ready()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.035f, 0.04f, 0.055f, 0.94f),
            BorderColor = new Color(0.38f, 0.75f, 0.95f, 0.9f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 7,
            CornerRadiusTopRight = 7,
            CornerRadiusBottomLeft = 7,
            CornerRadiusBottomRight = 7,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 8,
            ContentMarginBottom = 8
        };
        AddThemeStyleboxOverride("panel", style);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 5);
        AddChild(outer);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 6);
        outer.AddChild(top);

        _toggle = new Button
        {
            Text = "Plan Turn",
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Enable Ghost Turn Planner. Right-click cards in your hand to add them without playing them."
        };
        _toggle.Pressed += () => GhostTurnPlannerService.SetActive(_toggle.ButtonPressed);
        top.AddChild(_toggle);

        var clear = new Button
        {
            Text = "Clear",
            FocusMode = Control.FocusModeEnum.None
        };
        clear.Pressed += GhostTurnPlannerService.Clear;
        top.AddChild(clear);

        _share = new CheckBox
        {
            Text = "Share",
            ButtonPressed = GhostTurnPlannerService.SharePlan,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Share this plan with co-op teammates."
        };
        _share.Toggled += GhostTurnPlannerService.SetShare;
        top.AddChild(_share);

        _status = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _status.AddThemeFontSizeOverride("font_size", 13);
        outer.AddChild(_status);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(380f, 150f),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        outer.AddChild(scroll);

        _steps = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _steps.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_steps);

        GhostTurnPlannerService.Changed += Refresh;
        Refresh();
    }

    public override void _ExitTree()
    {
        GhostTurnPlannerService.Changed -= Refresh;
    }

    private void Refresh()
    {
        _toggle.SetPressedNoSignal(GhostTurnPlannerService.IsActive);
        _share.SetPressedNoSignal(GhostTurnPlannerService.SharePlan);

        foreach (Node child in _steps.GetChildren())
            child.QueueFree();

        var energy = GhostTurnPlannerService.CalculateEnergy();

        if (GhostTurnPlannerService.PendingCard is not null)
        {
            _status.Text = $"Target needed: {GhostTurnPlannerService.PendingCard.Title} - right-click a valid creature.";
        }
        else
        {
            _status.Text =
                $"{energy.startingEnergy} energy -> {energy.remainingEnergy} remaining" +
                (energy.overBudget ? "  ⚠ current plan exceeds available energy" : string.Empty) +
                "\nRight-click hand cards to plan. Click a planned row to remove it.";
        }

        int remaining = energy.startingEnergy;
        var steps = GhostTurnPlannerService.CurrentSteps;

        for (int i = 0; i < steps.Count; i++)
        {
            int index = i;
            var step = steps[i];
            int cost = step.Card.EnergyCost.CostsX
                ? Math.Max(0, remaining)
                : Math.Max(0, step.Card.EnergyCost.GetWithModifiers(CostModifiers.All));

            var row = new Button
            {
                Text = $"{i + 1}. {GhostTurnPlannerService.DescribeStep(step, remaining)}",
                FocusMode = Control.FocusModeEnum.None,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                Alignment = HorizontalAlignment.Left,
                TooltipText = "Remove this step from the Ghost Plan."
            };
            row.Pressed += () => GhostTurnPlannerService.RemoveAt(index);
            _steps.AddChild(row);

            remaining = Math.Max(0, remaining - cost);
        }
    }
}

internal partial class GhostRemotePlanLayer : CanvasLayer
{
    private readonly Dictionary<ulong, Label> _labels = new();
    private VBoxContainer _box = null!;

    public GhostRemotePlanLayer()
    {
        Layer = 124;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        var panel = new PanelContainer
        {
            Position = new Vector2(18f, 145f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(340f, 0f)
        };

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.04f, 0.055f, 0.88f),
            BorderColor = new Color(0.38f, 0.75f, 0.95f, 0.75f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 8,
            ContentMarginBottom = 8
        };
        panel.AddThemeStyleboxOverride("panel", style);

        _box = new VBoxContainer();
        panel.AddChild(_box);
        AddChild(panel);
    }

    internal void SetPlan(ulong senderId, string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            if (_labels.Remove(senderId, out var old) && GodotObject.IsInstanceValid(old))
                old.QueueFree();
            return;
        }

        if (!_labels.TryGetValue(senderId, out var label) || !GodotObject.IsInstanceValid(label))
        {
            label = new Label
            {
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(320f, 0f)
            };
            label.AddThemeFontSizeOverride("font_size", 13);
            _labels[senderId] = label;
            _box.AddChild(label);
        }

        string name = $"Player {senderId}";
        try
        {
            if (RunManager.Instance?.NetService is { } service)
                name = PlatformUtil.GetPlayerName(service.Platform, senderId);
        }
        catch { }

        label.Text = $"{name} - Ghost Plan\n{summary}";
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi._Ready))]
internal static class GhostTurnPlannerCombatUiPatch
{
    private static void Postfix(NCombatUi __instance)
    {
        try
        {
            GhostTurnPlannerService.ResetForCombat();
            GhostTurnPlannerService.Attach(__instance);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Ghost Turn Planner setup failed open: {ex}");
        }
    }
}

[HarmonyPatch(typeof(NHandCardHolder), nameof(NHandCardHolder._Ready))]
internal static class GhostTurnPlannerHandCardPatch
{
    private static void Postfix(NHandCardHolder __instance)
    {
        __instance.Hitbox.Connect(
            Control.SignalName.GuiInput,
            Callable.From<InputEvent>(evt =>
            {
                if (!GhostTurnPlannerService.IsActive ||
                    evt is not InputEventMouseButton mouse ||
                    mouse.Pressed ||
                    mouse.ButtonIndex != MouseButton.Right)
                {
                    return;
                }

                if (GhostTurnPlannerService.TryQueueCard(__instance.CardModel))
                    __instance.Hitbox.AcceptEvent();
            }));
    }
}

[HarmonyPatch(typeof(NCreature), nameof(NCreature._Ready))]
internal static class GhostTurnPlannerCreaturePatch
{
    private static void Postfix(NCreature __instance)
    {
        __instance.Hitbox.Connect(
            Control.SignalName.GuiInput,
            Callable.From<InputEvent>(evt =>
            {
                if (!GhostTurnPlannerService.IsAwaitingTarget ||
                    evt is not InputEventMouseButton mouse ||
                    mouse.Pressed ||
                    mouse.ButtonIndex != MouseButton.Right)
                {
                    return;
                }

                if (GhostTurnPlannerService.TryAssignTarget(__instance))
                    __instance.Hitbox.AcceptEvent();
            }));
    }
}
