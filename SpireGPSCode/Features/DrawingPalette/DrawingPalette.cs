using Godot;
using HarmonyLib;
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

internal static class DrawingPaletteService
{
    internal static readonly PaletteColor[] Palette =
    {
        new("Red", new Color("#FF4B4B")),
        new("Orange", new Color("#FF9F43")),
        new("Yellow", new Color("#FFE66D")),
        new("Green", new Color("#4CD964")),
        new("Cyan", new Color("#35D7E8")),
        new("Blue", new Color("#4DA3FF")),
        new("Purple", new Color("#9B6DFF")),
        new("Pink", new Color("#FF66C4"))
    };

    private static readonly Dictionary<ulong, int> Claims = new();
    private static INetGameService? _netService;
    private static IPlayerCollection? _players;
    private static bool _handlerRegistered;

    internal static event Action? Changed;

    internal static void ResetForRun()
    {
        Claims.Clear();
        Changed?.Invoke();
    }

    internal static bool IsActive =>
        SpireGpsSettings.DrawingPaletteEnabled &&
        !CompatibilityManager.ShouldYieldDrawingPalette();

    internal static void Initialize(
        INetGameService netService,
        IPlayerCollection players,
        NMapDrawings mapDrawings)
    {
        if (!IsActive)
            return;

        if (!ReferenceEquals(_netService, netService))
        {
            if (_handlerRegistered && _netService is not null)
            {
                try { _netService.UnregisterMessageHandler<DrawingPaletteMessage>(HandleMessage); }
                catch { }
            }

            _netService = netService;
            _players = players;
            _netService.RegisterMessageHandler<DrawingPaletteMessage>(HandleMessage);
            _handlerRegistered = true;
        }
        else
        {
            _players = players;
        }

        ulong localId = netService.NetId;
        if (!Claims.ContainsKey(localId))
        {
            var player = players.GetPlayer(localId);
            int preferred = FindClosestPaletteIndex(player.Character.MapDrawingColor);
            int claim = FindFreeIndexStartingAt(preferred, localId) ?? preferred;
            Claims[localId] = claim;
        }

        BroadcastLocalClaim();
        AttachUi(mapDrawings);
        Changed?.Invoke();
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
                $"Banter's Tweak's - Drawing Palette: {Palette[index].Name} is already used by another player.");
            return false;
        }

        Claims[_netService.NetId] = index;
        BroadcastLocalClaim();
        Changed?.Invoke();
        return true;
    }

    private static void HandleMessage(DrawingPaletteMessage message, ulong senderId)
    {
        if (!IsActive || !IsValidIndex(message.PaletteIndex))
            return;

        Claims[senderId] = message.PaletteIndex;
        ResolveLocalConflict();
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
        MainFile.Logger.Info(
            $"Drawing Palette: color conflict resolved; local player moved to {Palette[free.Value].Name}.");
        BroadcastLocalClaim();
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

        _netService.SendMessage(new DrawingPaletteMessage { PaletteIndex = index });
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
        tools.Size += new Vector2(250f, 0f);
    }
}

public sealed class DrawingPaletteMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => true;
    public bool ShouldBuffer => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;

    public int PaletteIndex;

    public void Serialize(PacketWriter writer) => writer.WriteInt(PaletteIndex);

    public void Deserialize(PacketReader reader) => PaletteIndex = reader.ReadInt();
}

internal partial class DrawingPaletteControl : HBoxContainer
{
    private readonly List<Button> _buttons = new();

    public DrawingPaletteControl()
    {
        CustomMinimumSize = new Vector2(244f, 46f);
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

        DrawingPaletteService.Changed += Refresh;
        Refresh();
    }

    public override void _ExitTree()
    {
        DrawingPaletteService.Changed -= Refresh;
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

        __result.DefaultColor = DrawingPaletteService.GetColorForPlayer(player);
    }
}
