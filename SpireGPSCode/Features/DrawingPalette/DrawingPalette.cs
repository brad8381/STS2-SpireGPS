using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using SpireGPS.Compatibility;
using SpireGPS.Config;
using SpireGPS.UI;

namespace SpireGPS.Features.DrawingPalette;

internal readonly record struct PaletteColor(string Name, Color Color);
internal readonly record struct TrackedDrawingLine(ulong PlayerId, Line2D Line, Color OriginalColor);

internal static class DrawingPaletteService
{
    internal static readonly PaletteColor[] Palette =
    {
        new("Red", new Color("#FF4B4B")),
        new("Orange", new Color("#FF9F43")),
        new("Yellow", new Color("#FFE66D")),
        new("Lime", new Color("#A8E95A")),
        new("Green", new Color("#4CD964")),
        new("Cyan", new Color("#35D7E8")),
        new("Sky", new Color("#72C7FF")),
        new("Blue", new Color("#4D7CFF")),
        new("Purple", new Color("#9B6DFF")),
        new("Magenta", new Color("#D95CFF")),
        new("Pink", new Color("#FF66C4")),
        new("White", new Color("#F2F2F2"))
    };

    private static readonly Dictionary<ulong, int> Claims = new();
    private static readonly Dictionary<ulong, bool> RecolorModes = new();
    private static readonly List<TrackedDrawingLine> TrackedLines = new();

    private static INetGameService? _netService;
    private static bool _handlersRegistered;

    private static bool _hostForceActive;
    private static bool _hostForcedRecolor;

    internal static event Action? Changed;

    internal static bool IsActive =>
        SpireGpsSettings.DrawingPaletteEnabled &&
        !CompatibilityManager.ShouldYieldDrawingPalette();

    internal static bool IsLocalHost => _netService?.Type == NetGameType.Host;
    internal static bool IsLocalRecolorForced => !IsLocalHost && _hostForceActive;
    internal static bool HostForceEnabled => IsLocalHost && SpireGpsSettings.DrawingPaletteHostForceRecolor;

    internal static bool EffectiveLocalRecolor
    {
        get
        {
            if (_netService is null)
                return SpireGpsSettings.DrawingPaletteRecolorExisting;

            return GetEffectiveRecolor(_netService.NetId);
        }
    }

    internal static void ResetForRun()
    {
        Claims.Clear();
        RecolorModes.Clear();
        TrackedLines.Clear();
        _hostForceActive = false;
        _hostForcedRecolor = false;
        Changed?.Invoke();
    }

    internal static void Initialize(
        INetGameService netService,
        IPlayerCollection players,
        NMapDrawings mapDrawings)
    {
        if (!IsActive)
            return;

        if (!ReferenceEquals(_netService, netService))
        {
            UnregisterHandlers();
            _netService = netService;
            _netService.RegisterMessageHandler<DrawingPaletteMessage>(HandlePaletteMessage);
            _netService.RegisterMessageHandler<DrawingPalettePolicyMessage>(HandlePolicyMessage);
            _handlersRegistered = true;
        }

        if (!ModConfigBridge.IsRegistered)
        {
            if (LocalPreferences.HasValue("drawing_palette", "recolor_existing"))
                SpireGpsSettings.DrawingPaletteRecolorExisting =
                    LocalPreferences.GetBool("drawing_palette", "recolor_existing", false);

            if (LocalPreferences.HasValue("drawing_palette", "host_force_recolor"))
                SpireGpsSettings.DrawingPaletteHostForceRecolor =
                    LocalPreferences.GetBool("drawing_palette", "host_force_recolor", false);
        }

        ulong localId = netService.NetId;
        RecolorModes[localId] = SpireGpsSettings.DrawingPaletteRecolorExisting;

        if (!Claims.ContainsKey(localId))
        {
            var player = players.GetPlayer(localId);
            if (player is null)
            {
                MainFile.Logger.Warn($"Drawing Palette: local player {localId} was not available during map initialization.");
                return;
            }

            int saved = LocalPreferences.GetInt("drawing_palette", "selected_index", -1);
            int preferred = IsValidIndex(saved)
                ? saved
                : FindClosestPaletteIndex(player.Character.MapDrawingColor);

            int claim = FindFreeIndexStartingAt(preferred, localId) ?? preferred;
            Claims[localId] = claim;
        }

        if (IsLocalHost)
        {
            _hostForceActive = SpireGpsSettings.DrawingPaletteHostForceRecolor;
            _hostForcedRecolor = SpireGpsSettings.DrawingPaletteRecolorExisting;
            BroadcastHostPolicy();
        }

        BroadcastLocalClaim();
        AttachUi(mapDrawings);
        ApplyAllVisualStates();
        Changed?.Invoke();
    }

    internal static void ApplySettingsChanged(string key)
    {
        if (key is "drawingPaletteEnabled" or "drawingPaletteExclusiveColors")
        {
            ApplyAllVisualStates();
            Changed?.Invoke();
        }

        if (_netService is null)
            return;

        if (key == "drawingPaletteRecolorExisting")
        {
            ulong localId = _netService.NetId;
            RecolorModes[localId] = SpireGpsSettings.DrawingPaletteRecolorExisting;
            LocalPreferences.Set(
                "drawing_palette",
                "recolor_existing",
                SpireGpsSettings.DrawingPaletteRecolorExisting);

            if (IsLocalHost && SpireGpsSettings.DrawingPaletteHostForceRecolor)
            {
                _hostForceActive = true;
                _hostForcedRecolor = SpireGpsSettings.DrawingPaletteRecolorExisting;
                BroadcastHostPolicy();
                ApplyAllVisualStates();
            }
            else
            {
                BroadcastLocalClaim();
                ApplyPlayerVisualState(localId);
            }

            Changed?.Invoke();
        }
        else if (key == "drawingPaletteHostForceRecolor" && IsLocalHost)
        {
            LocalPreferences.Set(
                "drawing_palette",
                "host_force_recolor",
                SpireGpsSettings.DrawingPaletteHostForceRecolor);

            _hostForceActive = SpireGpsSettings.DrawingPaletteHostForceRecolor;
            _hostForcedRecolor = SpireGpsSettings.DrawingPaletteRecolorExisting;
            BroadcastHostPolicy();
            ApplyAllVisualStates();
            Changed?.Invoke();
        }
    }

    internal static Color GetColorForPlayer(Player player)
    {
        if (!IsActive)
            return player.Character.MapDrawingColor;

        return Claims.TryGetValue(player.NetId, out int index) && IsValidIndex(index)
            ? Palette[index].Color
            : player.Character.MapDrawingColor;
    }

    internal static int? GetLocalIndex()
    {
        if (_netService is null)
            return null;

        return Claims.TryGetValue(_netService.NetId, out int index) ? index : null;
    }

    internal static bool IsClaimedByOther(int index)
    {
        if (!SpireGpsSettings.DrawingPaletteExclusiveMultiplayerColors || _netService is null)
            return false;

        ulong localId = _netService.NetId;
        return Claims.Any(kv => kv.Key != localId && kv.Value == index);
    }

    internal static bool TrySelectLocal(int index)
    {
        if (!IsActive || _netService is null || !IsValidIndex(index))
            return false;

        if (IsClaimedByOther(index))
        {
            SpireGpsToast.Show(
                $"Drawing Palette: {Palette[index].Name} is already used by another player.");
            return false;
        }

        ulong localId = _netService.NetId;
        Claims[localId] = index;
        LocalPreferences.Set("drawing_palette", "selected_index", index);

        BroadcastLocalClaim();
        ApplyPlayerVisualState(localId);
        Changed?.Invoke();
        return true;
    }

    internal static void SetLocalRecolorExisting(bool enabled)
    {
        SpireGpsSettings.DrawingPaletteRecolorExisting = enabled;
        LocalPreferences.Set("drawing_palette", "recolor_existing", enabled);
        ModConfigBridge.SetValue("drawingPaletteRecolorExisting", enabled);

        if (_netService is null)
            return;

        RecolorModes[_netService.NetId] = enabled;

        if (IsLocalHost && SpireGpsSettings.DrawingPaletteHostForceRecolor)
        {
            _hostForceActive = true;
            _hostForcedRecolor = enabled;
            BroadcastHostPolicy();
            ApplyAllVisualStates();
        }
        else
        {
            BroadcastLocalClaim();
            ApplyPlayerVisualState(_netService.NetId);
        }

        Changed?.Invoke();
    }

    internal static void SetHostForceRecolor(bool enabled)
    {
        if (!IsLocalHost)
            return;

        SpireGpsSettings.DrawingPaletteHostForceRecolor = enabled;
        LocalPreferences.Set("drawing_palette", "host_force_recolor", enabled);
        ModConfigBridge.SetValue("drawingPaletteHostForceRecolor", enabled);

        _hostForceActive = enabled;
        _hostForcedRecolor = SpireGpsSettings.DrawingPaletteRecolorExisting;
        BroadcastHostPolicy();
        ApplyAllVisualStates();
        Changed?.Invoke();
    }

    internal static void TrackLine(Player player, Line2D line, Color originalColor)
    {
        if (!IsActive)
            return;

        TrackedLines.RemoveAll(t => !GodotObject.IsInstanceValid(t.Line));
        TrackedLines.Add(new TrackedDrawingLine(player.NetId, line, originalColor));

        line.DefaultColor = GetEffectiveRecolor(player.NetId)
            ? GetColorForPlayer(player)
            : originalColor;
    }

    private static void HandlePaletteMessage(DrawingPaletteMessage message, ulong senderId)
    {
        if (!IsActive || !IsValidIndex(message.PaletteIndex))
            return;

        Claims[senderId] = message.PaletteIndex;
        RecolorModes[senderId] = message.RecolorExisting != 0;

        ResolveLocalConflict();
        ApplyPlayerVisualState(senderId);
        Changed?.Invoke();
    }

    private static void HandlePolicyMessage(DrawingPalettePolicyMessage message, ulong senderId)
    {
        if (!IsActive || _netService is null || IsLocalHost)
            return;

        if (_netService is not NetClientGameService client || senderId != client.HostNetId)
            return;

        _hostForceActive = message.ForceEnabled != 0;
        _hostForcedRecolor = message.RecolorExisting != 0;

        ApplyAllVisualStates();
        Changed?.Invoke();
    }

    private static void ResolveLocalConflict()
    {
        if (!SpireGpsSettings.DrawingPaletteExclusiveMultiplayerColors || _netService is null)
            return;

        ulong localId = _netService.NetId;
        if (!Claims.TryGetValue(localId, out int localIndex))
            return;

        var conflicts = Claims
            .Where(kv => kv.Value == localIndex)
            .Select(kv => kv.Key)
            .OrderBy(id => id)
            .ToArray();

        if (conflicts.Length <= 1 || conflicts[0] == localId)
            return;

        int? free = FindFreeIndexStartingAt((localIndex + 1) % Palette.Length, localId);
        if (!free.HasValue)
        {
            MainFile.Logger.Warn("Drawing Palette: no free multiplayer color remains.");
            return;
        }

        Claims[localId] = free.Value;
        LocalPreferences.Set("drawing_palette", "selected_index", free.Value);

        MainFile.Logger.Info(
            $"Drawing Palette: color conflict resolved; local player moved to {Palette[free.Value].Name}.");

        BroadcastLocalClaim();
        ApplyPlayerVisualState(localId);
    }

    private static bool GetEffectiveRecolor(ulong playerId)
    {
        if (_hostForceActive)
            return _hostForcedRecolor;

        return RecolorModes.TryGetValue(playerId, out bool enabled) && enabled;
    }

    private static void ApplyPlayerVisualState(ulong playerId)
    {
        TrackedLines.RemoveAll(t => !GodotObject.IsInstanceValid(t.Line));

        if (!IsActive)
        {
            foreach (var tracked in TrackedLines.Where(t => t.PlayerId == playerId))
                tracked.Line.DefaultColor = tracked.OriginalColor;
            return;
        }

        bool recolor = GetEffectiveRecolor(playerId);
        Color current = Claims.TryGetValue(playerId, out int index) && IsValidIndex(index)
            ? Palette[index].Color
            : Colors.White;

        foreach (var tracked in TrackedLines.Where(t => t.PlayerId == playerId))
            tracked.Line.DefaultColor = recolor ? current : tracked.OriginalColor;
    }

    private static void ApplyAllVisualStates()
    {
        foreach (ulong playerId in TrackedLines.Select(t => t.PlayerId).Distinct().ToArray())
            ApplyPlayerVisualState(playerId);
    }

    private static int? FindFreeIndexStartingAt(int start, ulong localId)
    {
        for (int offset = 0; offset < Palette.Length; offset++)
        {
            int index = (start + offset) % Palette.Length;
            bool used = Claims.Any(kv => kv.Key != localId && kv.Value == index);
            if (!used)
                return index;
        }

        return null;
    }

    private static int FindClosestPaletteIndex(Color target)
    {
        int best = 0;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < Palette.Length; i++)
        {
            Color c = Palette[i].Color;
            float dr = c.R - target.R;
            float dg = c.G - target.G;
            float db = c.B - target.B;
            float distance = dr * dr + dg * dg + db * db;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private static bool IsValidIndex(int index) => index >= 0 && index < Palette.Length;

    private static void BroadcastLocalClaim()
    {
        if (_netService is null ||
            !Claims.TryGetValue(_netService.NetId, out int index))
        {
            return;
        }

        bool recolor = RecolorModes.TryGetValue(_netService.NetId, out bool enabled) && enabled;
        _netService.SendMessage(new DrawingPaletteMessage
        {
            PaletteIndex = index,
            RecolorExisting = recolor ? 1 : 0
        });
    }

    private static void BroadcastHostPolicy()
    {
        if (_netService?.Type != NetGameType.Host)
            return;

        _netService.SendMessage(new DrawingPalettePolicyMessage
        {
            ForceEnabled = SpireGpsSettings.DrawingPaletteHostForceRecolor ? 1 : 0,
            RecolorExisting = SpireGpsSettings.DrawingPaletteRecolorExisting ? 1 : 0
        });
    }

    private static void UnregisterHandlers()
    {
        if (!_handlersRegistered || _netService is null)
            return;

        try
        {
            _netService.UnregisterMessageHandler<DrawingPaletteMessage>(HandlePaletteMessage);
            _netService.UnregisterMessageHandler<DrawingPalettePolicyMessage>(HandlePolicyMessage);
        }
        catch
        {
            // The previous network service may already be disposed.
        }

        _handlersRegistered = false;
    }

    private static void AttachUi(NMapDrawings mapDrawings)
    {
        var tools = mapDrawings.GetNodeOrNull<NinePatchRect>("%DrawingTools");
        if (tools is null)
            return;

        var row = tools.GetChildOrNull<HBoxContainer>(0);
        if (row is null || row.GetNodeOrNull<DrawingPaletteControl>("BanterDrawingPalette") is not null)
            return;

        row.AddChild(new DrawingPaletteControl { Name = "BanterDrawingPalette" });
        tools.Size += new Vector2(IsLocalHost ? 590f : 510f, 0f);
    }
}

public sealed class DrawingPaletteMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => true;
    public bool ShouldBuffer => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;

    public int PaletteIndex;
    public int RecolorExisting;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(PaletteIndex);
        writer.WriteInt(RecolorExisting);
    }

    public void Deserialize(PacketReader reader)
    {
        PaletteIndex = reader.ReadInt();
        RecolorExisting = reader.ReadInt();
    }
}

public sealed class DrawingPalettePolicyMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => true;
    public bool ShouldBuffer => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;

    public int ForceEnabled;
    public int RecolorExisting;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(ForceEnabled);
        writer.WriteInt(RecolorExisting);
    }

    public void Deserialize(PacketReader reader)
    {
        ForceEnabled = reader.ReadInt();
        RecolorExisting = reader.ReadInt();
    }
}

internal partial class DrawingPaletteControl : HBoxContainer
{
    private readonly List<Button> _buttons = new();
    private CheckBox _recolorExisting = null!;
    private CheckBox? _hostForce;

    public DrawingPaletteControl()
    {
        CustomMinimumSize = new Vector2(480f, 46f);
        AddThemeConstantOverride("separation", 4);
    }

    public override void _Ready()
    {
        for (int i = 0; i < DrawingPaletteService.Palette.Length; i++)
        {
            int index = i;
            var button = new Button
            {
                CustomMinimumSize = new Vector2(26f, 38f),
                TooltipText = DrawingPaletteService.Palette[index].Name,
                FocusMode = Control.FocusModeEnum.None
            };

            button.Pressed += () => DrawingPaletteService.TrySelectLocal(index);
            _buttons.Add(button);
            AddChild(button);
        }

        _recolorExisting = new CheckBox
        {
            Text = "Recolor old",
            TooltipText = "When your color changes, recolor your existing drawings too. Turn this off to restore each line's original saved color.",
            FocusMode = Control.FocusModeEnum.None
        };
        _recolorExisting.Toggled += DrawingPaletteService.SetLocalRecolorExisting;
        AddChild(_recolorExisting);

        if (DrawingPaletteService.IsLocalHost)
        {
            _hostForce = new CheckBox
            {
                Text = "Force",
                TooltipText = "Host only: force your Recolor old setting for every player in multiplayer.",
                FocusMode = Control.FocusModeEnum.None
            };
            _hostForce.Toggled += DrawingPaletteService.SetHostForceRecolor;
            AddChild(_hostForce);
        }

        DrawingPaletteService.Changed += Refresh;
        Refresh();
    }

    public override void _ExitTree()
    {
        DrawingPaletteService.Changed -= Refresh;
    }

    public override void _Process(double delta)
    {
        Visible = DrawingPaletteService.IsActive;
    }

    private void Refresh()
    {
        int? local = DrawingPaletteService.GetLocalIndex();

        for (int i = 0; i < _buttons.Count; i++)
        {
            bool selected = local == i;
            bool usedByOther = DrawingPaletteService.IsClaimedByOther(i);
            var palette = DrawingPaletteService.Palette[i];
            var button = _buttons[i];

            button.Disabled = usedByOther;
            button.Text = selected ? "✓" : string.Empty;
            button.TooltipText = usedByOther
                ? $"{palette.Name} - used by another player"
                : selected
                    ? $"{palette.Name} - your drawing color"
                    : palette.Name;

            button.AddThemeStyleboxOverride("normal", MakeStyle(palette.Color, selected, 1f));
            button.AddThemeStyleboxOverride("hover", MakeStyle(palette.Color.Lightened(0.12f), selected, 1f));
            button.AddThemeStyleboxOverride("pressed", MakeStyle(palette.Color.Darkened(0.12f), true, 1f));
            button.AddThemeStyleboxOverride("disabled", MakeStyle(palette.Color, false, 0.25f));
        }

        _recolorExisting.SetPressedNoSignal(DrawingPaletteService.EffectiveLocalRecolor);
        _recolorExisting.Disabled = DrawingPaletteService.IsLocalRecolorForced;
        _recolorExisting.TooltipText = DrawingPaletteService.IsLocalRecolorForced
            ? "Recolor mode is currently forced by the multiplayer host. Your personal setting is still saved and will return when the force is removed."
            : "When your color changes, recolor your existing drawings too. Turn this off to restore each line's original saved color.";

        if (_hostForce is not null)
            _hostForce.SetPressedNoSignal(DrawingPaletteService.HostForceEnabled);
    }

    private static StyleBoxFlat MakeStyle(Color color, bool selected, float alpha)
    {
        color.A = alpha;

        return new StyleBoxFlat
        {
            BgColor = color,
            BorderColor = selected ? Colors.White : new Color(0f, 0f, 0f, 0.6f),
            BorderWidthLeft = selected ? 3 : 1,
            BorderWidthTop = selected ? 3 : 1,
            BorderWidthRight = selected ? 3 : 1,
            BorderWidthBottom = selected ? 3 : 1,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4
        };
    }
}

[HarmonyPatch(typeof(NMapDrawings), nameof(NMapDrawings.Initialize))]
internal static class DrawingPaletteInitializePatch
{
    private static void Postfix(
        NMapDrawings __instance,
        INetGameService netService,
        IPlayerCollection playerCollection)
    {
        if (!DrawingPaletteService.IsActive)
            return;

        try
        {
            DrawingPaletteService.Initialize(netService, playerCollection, __instance);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Drawing Palette initialization failed open: {ex}");
        }
    }
}

[HarmonyPatch(typeof(NMapDrawings), "CreateLineForPlayer")]
internal static class DrawingPaletteLinePatch
{
    private static void Postfix(Player player, bool isErasing, Line2D __result)
    {
        if (isErasing || !DrawingPaletteService.IsActive)
            return;

        Color selectedColor = DrawingPaletteService.GetColorForPlayer(player);
        __result.DefaultColor = selectedColor;
        DrawingPaletteService.TrackLine(player, __result, selectedColor);
    }
}
