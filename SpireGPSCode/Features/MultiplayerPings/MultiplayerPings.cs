using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireGPS.Config;
using SpireGPS.Features.GhostTurnPlanner;

namespace SpireGPS.Features.MultiplayerPings;

internal enum PingKind
{
    Attack = 0,
    Focus = 1,
    Wait = 2,
    IllHandle = 3,
    Ready = 4,
    NeedBlock = 5,
    NeedDamage = 6
}

internal static class MultiplayerPingService
{
    private const string OverlayName = "BanterPingOverlay";
    private static INetGameService? _netService;
    private static bool _registered;

    internal static void AttachToCreature(NCreature creature)
    {
        // Always attach the lightweight input hook so ModConfig can enable or
        // disable pings live during an existing combat.
        EnsureNetwork();

        if (!IsMultiplayer() || creature.Entity.CombatId is null)
            return;

        var hitbox = creature.Hitbox;
        if (hitbox.HasMeta("banter_ping_hook"))
            return;

        hitbox.SetMeta("banter_ping_hook", true);
        hitbox.Connect(
            Control.SignalName.GuiInput,
            Callable.From<InputEvent>(evt => OnCreatureInput(creature, evt)));
    }

    private static void EnsureNetwork()
    {
        var service = RunManager.Instance?.NetService;
        if (service is null)
            return;

        if (ReferenceEquals(_netService, service) && _registered)
            return;

        if (_registered && _netService is not null)
        {
            try { _netService.UnregisterMessageHandler<MultiplayerPingMessage>(OnMessage); }
            catch { }
        }

        _netService = service;
        _netService.RegisterMessageHandler<MultiplayerPingMessage>(OnMessage);
        _registered = true;
    }

    private static bool IsMultiplayer()
        => _netService is not null &&
           _netService.Type is NetGameType.Host or NetGameType.Client;

    private static void OnCreatureInput(NCreature creature, InputEvent evt)
    {
        if (!SpireGpsSettings.MultiplayerPingsEnabled ||
            !IsMultiplayer() ||
            GhostTurnPlannerService.IsAwaitingTarget ||
            evt is not InputEventMouseButton mouse ||
            mouse.Pressed ||
            mouse.ButtonIndex != MouseButton.Right)
        {
            return;
        }

        if (creature.Entity.CombatId is not uint combatId)
            return;

        ShowMenu(creature.Entity.IsEnemy, combatId, mouse.GlobalPosition);
        creature.Hitbox.AcceptEvent();
    }

    internal static void AttachToPlayerState(NMultiplayerPlayerState playerState)
    {
        // As with creature hitboxes, keep the hook installed and gate the
        // actual behavior in the input handler.
        EnsureNetwork();

        if (!IsMultiplayer() || playerState.Hitbox.HasMeta("banter_ping_hook"))
            return;

        playerState.Hitbox.SetMeta("banter_ping_hook", true);
        playerState.Hitbox.Connect(
            Control.SignalName.GuiInput,
            Callable.From<InputEvent>(evt => OnPlayerStateInput(playerState, evt)));
    }

    private static void OnPlayerStateInput(NMultiplayerPlayerState playerState, InputEvent evt)
    {
        if (!IsMultiplayer() ||
            evt is not InputEventMouseButton mouse ||
            mouse.Pressed ||
            mouse.ButtonIndex != MouseButton.Right)
        {
            return;
        }

        if (GhostTurnPlannerService.IsAwaitingTarget)
        {
            if (GhostTurnPlannerService.TryAssignTarget(playerState.Player.Creature))
                playerState.Hitbox.AcceptEvent();
            return;
        }

        if (!SpireGpsSettings.MultiplayerPingsEnabled ||
            playerState.Player.Creature.CombatId is not uint combatId)
        {
            return;
        }

        ShowMenu(false, combatId, mouse.GlobalPosition);
        playerState.Hitbox.AcceptEvent();
    }

    private static void ShowMenu(bool isEnemy, uint combatId, Vector2 screenPosition)
    {
        var menu = new PopupMenu
        {
            Name = "PingMenu"
        };

        if (isEnemy)
        {
            Add(menu, "Attack", PingKind.Attack);
            Add(menu, "Focus", PingKind.Focus);
            Add(menu, "Wait", PingKind.Wait);
            Add(menu, "I'll handle", PingKind.IllHandle);
        }
        else
        {
            Add(menu, "Ready", PingKind.Ready);
            Add(menu, "Need Block", PingKind.NeedBlock);
            Add(menu, "Need Damage", PingKind.NeedDamage);
            Add(menu, "Wait", PingKind.Wait);
        }

        menu.Connect(
            new StringName("id_pressed"),
            Callable.From<long>(id =>
            {
                SendPing(combatId, (PingKind)id);
                menu.QueueFree();
            }));

        menu.Connect(
            new StringName("popup_hide"),
            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(menu))
                    menu.QueueFree();
            }));

        NGame.Instance.AddChild(menu);
        menu.Position = new Vector2I((int)screenPosition.X, (int)screenPosition.Y);
        menu.Popup();
    }

    private static void Add(PopupMenu menu, string text, PingKind kind)
        => menu.AddItem(text, (int)kind);

    private static void SendPing(uint targetCombatId, PingKind kind)
    {
        if (_netService is null)
            return;

        ShowPing(_netService.NetId, targetCombatId, kind);

        _netService.SendMessage(new MultiplayerPingMessage
        {
            TargetCombatId = targetCombatId,
            Kind = (int)kind
        });
    }

    private static void OnMessage(MultiplayerPingMessage message, ulong senderId)
    {
        if (_netService is null || senderId == _netService.NetId)
            return;

        if (!Enum.IsDefined(typeof(PingKind), message.Kind))
            return;

        ShowPing(senderId, message.TargetCombatId, (PingKind)message.Kind);
    }

    private static void ShowPing(ulong senderId, uint targetCombatId, PingKind kind)
    {
        var target = NCombatRoom.Instance?.CreatureNodes
            .FirstOrDefault(node => node.Entity.CombatId == targetCombatId);

        if (target is null)
            return;

        var overlay = EnsureOverlay();
        overlay?.ShowPing(senderId, target, kind);
    }

    private static PingOverlay? EnsureOverlay()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return null;

        var existing = tree.Root.GetNodeOrNull<PingOverlay>(OverlayName);
        if (existing is not null)
            return existing;

        var overlay = new PingOverlay { Name = OverlayName };
        tree.Root.AddChild(overlay);
        return overlay;
    }
}

public sealed class MultiplayerPingMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => true;
    public bool ShouldBuffer => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;

    public uint TargetCombatId;
    public int Kind;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt(TargetCombatId);
        writer.WriteUInt((uint)Kind, 4);
    }

    public void Deserialize(PacketReader reader)
    {
        TargetCombatId = reader.ReadUInt();
        Kind = (int)reader.ReadUInt(4);
    }
}

internal partial class PingOverlay : CanvasLayer
{
    private readonly Dictionary<(ulong sender, uint target), PingBubble> _active = new();

    public PingOverlay()
    {
        Layer = 125;
        ProcessMode = ProcessModeEnum.Always;
    }

    internal void ShowPing(ulong senderId, NCreature target, PingKind kind)
    {
        if (target.Entity.CombatId is not uint targetId)
            return;

        var key = (senderId, targetId);
        if (_active.TryGetValue(key, out var existing) && GodotObject.IsInstanceValid(existing))
            existing.QueueFree();

        var bubble = new PingBubble(target, kind);
        bubble.Connect(
            Node.SignalName.TreeExiting,
            Callable.From(() =>
            {
                if (_active.TryGetValue(key, out var current) && ReferenceEquals(current, bubble))
                    _active.Remove(key);
            }));
        _active[key] = bubble;
        AddChild(bubble);
    }
}

internal partial class PingBubble : PanelContainer
{
    private readonly NCreature _target;
    private readonly double _expiresAt;

    internal PingBubble(NCreature target, PingKind kind)
    {
        _target = target;
        _expiresAt = Time.GetTicksMsec() / 1000.0 + 4.0;

        MouseFilter = Control.MouseFilterEnum.Ignore;
        ProcessMode = ProcessModeEnum.Always;
        ZIndex = 200;

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.05f, 0.07f, 0.92f),
            BorderColor = new Color(0.95f, 0.72f, 0.22f, 0.95f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 7,
            CornerRadiusTopRight = 7,
            CornerRadiusBottomLeft = 7,
            CornerRadiusBottomRight = 7,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 5,
            ContentMarginBottom = 5
        };
        AddThemeStyleboxOverride("panel", style);

        var label = new Label
        {
            Text = GetText(kind),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontSizeOverride("font_size", 16);
        AddChild(label);
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_target) ||
            Time.GetTicksMsec() / 1000.0 >= _expiresAt)
        {
            QueueFree();
            return;
        }

        Vector2 top = _target.GetTopOfHitbox();
        GlobalPosition = new Vector2(
            top.X - Size.X * 0.5f,
            top.Y - Size.Y - 18f);
    }

    private static string GetText(PingKind kind)
        => kind switch
        {
            PingKind.Attack => "Attack",
            PingKind.Focus => "Focus",
            PingKind.Wait => "Wait",
            PingKind.IllHandle => "I'll handle",
            PingKind.Ready => "Ready",
            PingKind.NeedBlock => "Need Block",
            PingKind.NeedDamage => "Need Damage",
            _ => "Ping"
        };
}

[HarmonyPatch(typeof(NCreature), nameof(NCreature._Ready))]
internal static class MultiplayerPingCreaturePatch
{
    private static void Postfix(NCreature __instance)
    {
        try
        {
            MultiplayerPingService.AttachToCreature(__instance);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Multiplayer Pings setup failed open: {ex}");
        }
    }
}


[HarmonyPatch(typeof(NMultiplayerPlayerState), nameof(NMultiplayerPlayerState._Ready))]
internal static class MultiplayerPingPlayerStatePatch
{
    private static void Postfix(NMultiplayerPlayerState __instance)
    {
        try
        {
            MultiplayerPingService.AttachToPlayerState(__instance);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Multiplayer Pings player-state setup failed open: {ex}");
        }
    }
}
