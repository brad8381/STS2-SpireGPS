using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using SpireGPS.Config;

namespace SpireGPS.Features.MapLegend;

internal static class MapLegendService
{
    private const float SafeTop = 96f;
    private const float LegendSafeTop = 138f;
    private const string ControlsName = "BanterMapLegendControls";
    private static readonly FieldInfo? MapLegendField =
        AccessTools.Field(typeof(NMapScreen), "_mapLegend");

    private static NMapScreen? _screen;
    private static Control? _legend;
    private static MapLegendControls? _controls;

    private static float _vanillaY;
    private static bool _hasCustomPosition;
    private static Vector2 _savedPosition;
    private static bool _dragging;
    private static ulong _openedAt;

    internal static bool IsDragging => _dragging;

    internal static void Attach(NMapScreen screen)
    {
        var legend = MapLegendField?.GetValue(screen) as Control;
        if (legend is null)
        {
            MainFile.Logger.Warn("Map Legend: vanilla MapLegend control was not found.");
            return;
        }

        _screen = screen;
        _legend = legend;
        _vanillaY = legend.Position.Y;

        if (!ModConfigBridge.IsRegistered)
        {
            if (LocalPreferences.HasValue("map_legend", "visible"))
                SpireGpsSettings.MapLegendVisible =
                    LocalPreferences.GetBool("map_legend", "visible", true);

            if (LocalPreferences.HasValue("map_legend", "movable"))
                SpireGpsSettings.MapLegendMovable =
                    LocalPreferences.GetBool("map_legend", "movable", true);
        }

        _hasCustomPosition =
            LocalPreferences.GetBool("map_legend", "has_custom_position", false);

        if (_hasCustomPosition)
        {
            _savedPosition = LocalPreferences.GetVector2(
                "map_legend",
                "position",
                GetVanillaPosition());
        }

        _controls = screen.GetNodeOrNull<MapLegendControls>(ControlsName);
        if (_controls is null)
        {
            _controls = new MapLegendControls { Name = ControlsName };
            screen.AddChild(_controls);
        }

        _controls.Refresh();
        ApplyVisibility();
    }

    internal static void OnMapOpened()
    {
        _openedAt = Time.GetTicksMsec();
        ApplyVisibility();
        _controls?.Refresh();
    }

    internal static void OnMapClosed()
    {
        _dragging = false;
    }

    internal static void Tick()
    {
        if (_screen is null || _legend is null || !GodotObject.IsInstanceValid(_screen) ||
            !GodotObject.IsInstanceValid(_legend))
        {
            return;
        }

        bool open = _screen.IsOpen && _screen.IsVisibleInTree();
        if (_controls is not null)
            _controls.Visible = open && SpireGpsSettings.MapLegendEnabled;

        if (!open)
            return;

        ApplyVisibility();

        // Vanilla animates the legend into its normal X position when the map
        // opens. Let that finish before applying a saved custom position.
        if (SpireGpsSettings.MapLegendEnabled &&
            _hasCustomPosition &&
            !_dragging &&
            Time.GetTicksMsec() >= _openedAt + 450)
        {
            _legend.Position = ClampPosition(_savedPosition);
        }
        else if (!SpireGpsSettings.MapLegendEnabled && !_dragging)
        {
            _legend.Position = GetVanillaPosition();
        }

        _controls?.FollowLegend();
    }

    internal static void SetVisible(bool visible)
    {
        SpireGpsSettings.MapLegendVisible = visible;
        LocalPreferences.Set("map_legend", "visible", visible);
        ModConfigBridge.SetValue("mapLegendVisible", visible);
        ApplyVisibility();
        _controls?.Refresh();
    }

    internal static void ToggleVisible() => SetVisible(!SpireGpsSettings.MapLegendVisible);

    internal static void SetMovable(bool movable)
    {
        SpireGpsSettings.MapLegendMovable = movable;
        LocalPreferences.Set("map_legend", "movable", movable);
        ModConfigBridge.SetValue("mapLegendMovable", movable);
        _controls?.Refresh();
    }

    internal static void BeginDrag()
    {
        if (!SpireGpsSettings.MapLegendEnabled || !SpireGpsSettings.MapLegendMovable ||
            _legend is null || !SpireGpsSettings.MapLegendVisible)
        {
            return;
        }

        _dragging = true;
    }

    internal static void DragBy(Vector2 relative)
    {
        if (!_dragging || _legend is null)
            return;

        _legend.Position = ClampPosition(_legend.Position + relative);
        _savedPosition = _legend.Position;
        _hasCustomPosition = true;
        _controls?.FollowLegend();
    }

    internal static void EndDrag()
    {
        if (!_dragging)
            return;

        _dragging = false;

        if (_legend is null)
            return;

        _savedPosition = ClampPosition(_legend.Position);
        _hasCustomPosition = true;
        LocalPreferences.Set("map_legend", "has_custom_position", true);
        LocalPreferences.Set("map_legend", "position", _savedPosition);
    }

    internal static void ResetPosition()
    {
        _dragging = false;
        _hasCustomPosition = false;
        LocalPreferences.Set("map_legend", "has_custom_position", false);

        if (_legend is not null)
            _legend.Position = GetVanillaPosition();

        _controls?.FollowLegend();
    }

    internal static void ApplySettingsChanged(string key)
    {
        if (key == "mapLegendVisible")
        {
            LocalPreferences.Set("map_legend", "visible", SpireGpsSettings.MapLegendVisible);
            ApplyVisibility();
            _controls?.Refresh();
        }
        else if (key == "mapLegendMovable")
        {
            LocalPreferences.Set("map_legend", "movable", SpireGpsSettings.MapLegendMovable);
            _controls?.Refresh();
        }
        else if (key == "mapLegendEnabled")
        {
            _dragging = false;

            if (_legend is not null)
            {
                _legend.Visible = SpireGpsSettings.MapLegendEnabled
                    ? SpireGpsSettings.MapLegendVisible
                    : true;

                if (!SpireGpsSettings.MapLegendEnabled)
                    _legend.Position = GetVanillaPosition();
                else if (_hasCustomPosition)
                    _legend.Position = ClampPosition(_savedPosition);
            }

            _controls?.Refresh();
        }
    }

    internal static Vector2 GetControlsPosition(Vector2 controlSize)
    {
        if (_screen is null)
            return Vector2.Zero;

        Vector2 basePosition;

        if (_legend is not null && SpireGpsSettings.MapLegendVisible)
        {
            basePosition = _legend.Position + new Vector2(
                Math.Max(0f, _legend.Size.X - controlSize.X),
                -controlSize.Y - 6f);
        }
        else
        {
            Vector2 position = _hasCustomPosition ? _savedPosition : GetVanillaPosition();
            basePosition = position + new Vector2(0f, -controlSize.Y - 6f);
        }

        return new Vector2(
            Math.Clamp(basePosition.X, 8f, Math.Max(8f, _screen.Size.X - controlSize.X - 8f)),
            Math.Clamp(basePosition.Y, SafeTop, Math.Max(SafeTop, _screen.Size.Y - controlSize.Y - 8f)));
    }

    private static void ApplyVisibility()
    {
        if (_legend is null || !GodotObject.IsInstanceValid(_legend))
            return;

        _legend.Visible = !SpireGpsSettings.MapLegendEnabled || SpireGpsSettings.MapLegendVisible;
    }

    private static Vector2 GetVanillaPosition()
    {
        if (_screen is null)
            return Vector2.Zero;

        return new Vector2(_screen.Size.X * 0.8f, _vanillaY);
    }

    private static Vector2 ClampPosition(Vector2 position)
    {
        if (_screen is null || _legend is null)
            return position;

        float maxX = Math.Max(0f, _screen.Size.X - Math.Max(40f, _legend.Size.X));
        float maxY = Math.Max(0f, _screen.Size.Y - Math.Max(40f, _legend.Size.Y));

        return new Vector2(
            Math.Clamp(position.X, 0f, maxX),
            Math.Clamp(position.Y, LegendSafeTop, Math.Max(LegendSafeTop, maxY)));
    }
}

internal partial class MapLegendControls : PanelContainer
{
    private MapLegendDragButton _move = null!;
    private Button _visibility = null!;
    private Button _reset = null!;

    public MapLegendControls()
    {
        MouseFilter = Control.MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
        CustomMinimumSize = new Vector2(210f, 34f);
    }

    public override void _Ready()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.05f, 0.06f, 0.9f),
            BorderColor = new Color(0.55f, 0.48f, 0.32f, 0.9f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            ContentMarginLeft = 4,
            ContentMarginRight = 4,
            ContentMarginTop = 3,
            ContentMarginBottom = 3
        };
        AddThemeStyleboxOverride("panel", style);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        AddChild(row);

        _move = new MapLegendDragButton
        {
            Text = "Move",
            TooltipText = "Drag to move the map legend.",
            CustomMinimumSize = new Vector2(70f, 28f),
            FocusMode = Control.FocusModeEnum.None
        };
        row.AddChild(_move);

        _visibility = new Button
        {
            CustomMinimumSize = new Vector2(65f, 28f),
            FocusMode = Control.FocusModeEnum.None
        };
        _visibility.Pressed += MapLegendService.ToggleVisible;
        row.AddChild(_visibility);

        _reset = new Button
        {
            Text = "Reset",
            TooltipText = "Reset the map legend to its vanilla position.",
            CustomMinimumSize = new Vector2(65f, 28f),
            FocusMode = Control.FocusModeEnum.None
        };
        _reset.Pressed += MapLegendService.ResetPosition;
        row.AddChild(_reset);

        Refresh();
        FollowLegend();
    }

    public override void _Process(double delta)
    {
        MapLegendService.Tick();
    }

    internal void Refresh()
    {
        if (_visibility is null)
            return;

        _visibility.Text = SpireGpsSettings.MapLegendVisible ? "Hide" : "Show";
        _visibility.TooltipText = SpireGpsSettings.MapLegendVisible
            ? "Hide the map legend. The control strip remains so you can show it again."
            : "Show the map legend.";

        if (_move is not null)
        {
            _move.Disabled = !SpireGpsSettings.MapLegendMovable || !SpireGpsSettings.MapLegendVisible;
            _move.TooltipText = !SpireGpsSettings.MapLegendMovable
                ? "Moving the map legend is disabled in settings."
                : !SpireGpsSettings.MapLegendVisible
                    ? "Show the legend before moving it."
                    : "Drag to move the map legend.";
        }
    }

    internal void FollowLegend()
    {
        Position = MapLegendService.GetControlsPosition(Size);
    }
}

internal partial class MapLegendDragButton : Button
{
    public override void _GuiInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton mouseButton &&
            mouseButton.ButtonIndex == MouseButton.Left)
        {
            if (mouseButton.Pressed)
                MapLegendService.BeginDrag();
            else
                MapLegendService.EndDrag();

            AcceptEvent();
            return;
        }

        if (inputEvent is InputEventMouseMotion motion && MapLegendService.IsDragging)
        {
            MapLegendService.DragBy(motion.Relative);
            AcceptEvent();
        }
    }
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen._Ready))]
internal static class MapLegendReadyPatch
{
    private static void Postfix(NMapScreen __instance)
    {
        try
        {
            MapLegendService.Attach(__instance);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Map Legend setup failed open: {ex}");
        }
    }
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
internal static class MapLegendOpenPatch
{
    private static void Postfix()
    {
        if (SpireGpsSettings.MapLegendEnabled)
            MapLegendService.OnMapOpened();
    }
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Close))]
internal static class MapLegendClosePatch
{
    private static void Prefix()
    {
        MapLegendService.OnMapClosed();
    }
}
