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

    internal static void InitializeForRun()
    {
        EnsureNetwork();
        Steps.Clear();
        _pendingCard = null;
        _active = false;
        ClearRemotePlans();
    }

    internal static void Attach(NCombatUi ui)
    {
        EnsureNetwork();

        if (ui.GetNodeOrNull<GhostTurnPlannerPanel>(PanelName) is null)
        {
            ui.AddChild(new GhostTurnPlannerPanel
            {
                Name = PanelName
            });
        }

        // A player can enter combat slightly before another client has built
        // its UI/handler. Ask everybody to resend their current plan so plans
        // are not permanently one-way because of scene timing.
        RequestPlans();
    }

    internal static void ResetForCombat()
    {
        Steps.Clear();
        _pendingCard = null;
        _active = false;
        ClearRemotePlans();
        BroadcastPlan();
        Changed?.Invoke();
    }

    internal static void SetActive(bool active)
    {
        if (_active == active && (active || _pendingCard is null))
            return;

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

        Creature entity = creature.Entity;
        if (entity.CombatId is not uint combatId)
            return false;

        if (!TargetAllowed(_pendingCard, entity))
        {
            SpireGpsToast.Show($"Ghost Planner: invalid target for {_pendingCard.Title}.");
            return true;
        }

        string label = BuildTargetLabel(creature);

        var card = _pendingCard;
        _pendingCard = null;
        AddStep(card, combatId, label);
        return true;
    }

    internal static bool TryAssignTarget(Creature creature)
    {
        if (!IsAwaitingTarget || _pendingCard is null)
            return false;

        if (creature.CombatId is not uint combatId)
            return false;

        if (!TargetAllowed(_pendingCard, creature))
        {
            SpireGpsToast.Show($"Ghost Planner: invalid target for {_pendingCard.Title}.");
            return true;
        }

        string label = creature.IsEnemy
            ? $"Enemy · {creature.Monster?.Title.GetFormattedText() ?? "Unknown"}"
            : creature.Player?.Character.Title.GetFormattedText() ?? "Player";

        var card = _pendingCard;
        _pendingCard = null;
        AddStep(card, combatId, label);
        return true;
    }

    private static string BuildTargetLabel(NCreature target)
    {
        Creature entity = target.Entity;

        if (!entity.IsEnemy)
            return entity.Player?.Character.Title.GetFormattedText() ?? "Player";

        string name = entity.Monster?.Title.GetFormattedText() ?? "Enemy";
        int position = GetEnemyScreenPosition(target);

        return position > 0
            ? $"[{position}] {name}"
            : $"Enemy · {name}";
    }

    private static int GetEnemyScreenPosition(NCreature target)
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return 0;

        var enemies = new List<NCreature>();
        CollectEnemyNodes(tree.Root, enemies);

        var ordered = enemies
            .Where(node =>
                GodotObject.IsInstanceValid(node) &&
                node.IsVisibleInTree() &&
                node.Entity.IsEnemy &&
                node.Entity.IsAlive)
            .OrderBy(node => node.GlobalPosition.X)
            .ThenBy(node => node.GlobalPosition.Y)
            .ToArray();

        int index = Array.IndexOf(ordered, target);
        return index >= 0 ? index + 1 : 0;
    }

    private static void CollectEnemyNodes(Node root, List<NCreature> results)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is NCreature creature && creature.Entity.IsEnemy)
                results.Add(creature);

            CollectEnemyNodes(child, results);
        }
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

        if (TryGetKnownDamage(step.Card, out int damage, out int hits))
            pieces.Add(hits > 1 ? $"DMG {damage}x{hits}" : $"DMG {damage}");

        if (TryGetKnownBlock(step.Card, out int block))
            pieces.Add($"BLK {block}");

        try
        {
            if (step.Card.DynamicVars.TryGetValue("PoisonPower", out var poison))
                pieces.Add($"Poison {poison.IntValue}");
        }
        catch
        {
            // Optional dynamic vars can be absent on modded cards.
        }

        return string.Join(" | ", pieces);
    }

    internal static (int damage, int block, int unknownSteps) CalculateKnownProjection()
    {
        int damage = 0;
        int block = 0;
        int unknown = 0;

        foreach (var step in Steps)
        {
            bool knewSomething = false;

            if (TryGetKnownDamage(step.Card, out int hitDamage, out int hits))
            {
                damage += Math.Max(0, hitDamage) * Math.Max(1, hits);
                knewSomething = true;
            }

            if (TryGetKnownBlock(step.Card, out int stepBlock))
            {
                block += Math.Max(0, stepBlock);
                knewSomething = true;
            }

            if (!knewSomething &&
                step.Card.Type is CardType.Attack or CardType.Skill)
            {
                unknown++;
            }
        }

        return (damage, block, unknown);
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

    private static bool TryGetKnownDamage(CardModel card, out int damage, out int hits)
    {
        damage = 0;
        hits = 1;

        try
        {
            if (card.DynamicVars.TryGetValue("CalculatedDamage", out var calculated))
                damage = calculated.IntValue;
            else if (card.DynamicVars.TryGetValue("Damage", out var normal))
                damage = normal.IntValue;
            else
                return false;

            if (card.DynamicVars.TryGetValue("Repeat", out var repeat))
                hits = Math.Max(1, repeat.IntValue);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetKnownBlock(CardModel card, out int block)
    {
        block = 0;

        try
        {
            if (card.DynamicVars.TryGetValue("CalculatedBlock", out var calculated))
            {
                block = calculated.IntValue;
                return true;
            }

            if (card.DynamicVars.TryGetValue("Block", out var normal))
            {
                block = normal.IntValue;
                return true;
            }
        }
        catch
        {
        }

        return false;
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
            try { _netService.UnregisterMessageHandler<GhostPlanRequestMessage>(HandlePlanRequest); }
            catch { }
        }

        _netService = service;
        _netService.RegisterMessageHandler<GhostPlanMessage>(HandleRemotePlan);
        _netService.RegisterMessageHandler<GhostPlanRequestMessage>(HandlePlanRequest);
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

    private static void RequestPlans()
    {
        EnsureNetwork();

        if (_netService is null ||
            _netService.Type is not (NetGameType.Host or NetGameType.Client))
        {
            return;
        }

        _netService.SendMessage(new GhostPlanRequestMessage());
    }

    private static void HandlePlanRequest(GhostPlanRequestMessage message, ulong senderId)
    {
        if (_netService is null || senderId == _netService.NetId)
            return;

        BroadcastPlan();
    }

    private static void HandleRemotePlan(GhostPlanMessage message, ulong senderId)
    {
        if (_netService is null || senderId == _netService.NetId)
            return;

        var layer = EnsureRemoteLayer();
        layer?.SetPlan(senderId, message.Summary);
    }

    private static void ClearRemotePlans()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        tree.Root.GetNodeOrNull<GhostRemotePlanLayer>(RemoteLayerName)?.ClearAll();
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

public sealed class GhostPlanRequestMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => true;
    public bool ShouldBuffer => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;

    public void Serialize(PacketWriter writer)
    {
    }

    public void Deserialize(PacketReader reader)
    {
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
        OffsetLeft = -410f;
        OffsetRight = -18f;
        OffsetTop = -65f;
        OffsetBottom = 195f;
        CustomMinimumSize = new Vector2(380f, 180f);
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

        var title = new Label
        {
            Text = "Ghost Planner",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 15);
        top.AddChild(title);

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
            CustomMinimumSize = new Vector2(360f, 110f),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        outer.AddChild(scroll);

        _steps = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _steps.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_steps);

        PanelChrome.Attach("ghost_turn_planner", this, top, outer);

        GhostTurnPlannerService.Changed += Refresh;
        Refresh();
    }

    public override void _ExitTree()
    {
        GhostTurnPlannerService.Changed -= Refresh;
    }

    public override void _Process(double delta)
    {
        bool shouldShow =
            SpireGpsSettings.GhostTurnPlannerEnabled &&
            UiContext.IsCombatScreenCurrent();

        Visible = shouldShow;
        if (!shouldShow)
            GhostTurnPlannerService.SetActive(false);
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
            var projection = GhostTurnPlannerService.CalculateKnownProjection();
            string known =
                $"Known: {projection.damage} damage | {projection.block} Block" +
                (projection.unknownSteps > 0 ? $" | {projection.unknownSteps} effect(s) ?" : string.Empty);

            _status.Text =
                $"{energy.startingEnergy} energy -> {energy.remainingEnergy} remaining" +
                (energy.overBudget ? "  ⚠ current plan exceeds available energy" : string.Empty) +
                $"\n{known}" +
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
    private PanelContainer _panel = null!;
    private VBoxContainer _box = null!;

    public GhostRemotePlanLayer()
    {
        Layer = 124;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        _panel = new PanelContainer
        {
            Position = new Vector2(18f, 145f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(340f, 0f),
            Visible = false
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
        _panel.AddThemeStyleboxOverride("panel", style);

        _box = new VBoxContainer();
        _panel.AddChild(_box);
        AddChild(_panel);
    }

    public override void _Process(double delta)
    {
        _panel.Visible = _labels.Count > 0 && UiContext.IsCombatScreenCurrent();
    }

    internal void SetPlan(ulong senderId, string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            if (_labels.Remove(senderId, out var old) && GodotObject.IsInstanceValid(old))
                old.QueueFree();

            _panel.Visible = _labels.Count > 0 && UiContext.IsCombatScreenCurrent();
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
        _panel.Visible = UiContext.IsCombatScreenCurrent();
    }

    internal void ClearAll()
    {
        foreach (var label in _labels.Values)
        {
            if (GodotObject.IsInstanceValid(label))
                label.QueueFree();
        }

        _labels.Clear();
        if (GodotObject.IsInstanceValid(_panel))
            _panel.Visible = false;
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
