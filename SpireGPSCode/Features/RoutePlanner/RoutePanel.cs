using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using SpireGPS.Config;

namespace SpireGPS.Features.RoutePlanner;

internal static class RoutePanelController
{
    private const string LayerName = "SpireGPSRoutePanel";

    internal static void EnsureInstalled()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        var root = tree.Root;
        if (root.GetNodeOrNull<RoutePanelLayer>(LayerName) is not null)
            return;

        root.CallDeferred(Node.MethodName.AddChild, new RoutePanelLayer { Name = LayerName });
    }

    internal static void Refresh()
    {
        EnsureInstalled();

        if (Engine.GetMainLoop() is SceneTree tree &&
            tree.Root.GetNodeOrNull<RoutePanelLayer>(LayerName) is { } layer)
        {
            layer.RefreshRoutes();
        }
    }
}

internal partial class RoutePanelLayer : CanvasLayer
{
    private const float PanelWidth = 540f;
    private const float CellWidth = 44f;
    private const float RouteWidth = 118f;
    private const float ScoreWidth = 58f;
    private const float RowHeight = 32f;
    private const int VisibleRows = 5;

    private PanelContainer _panel = null!;
    private Label _status = null!;
    private CheckBox _autoSelectSuggested = null!;
    private HBoxContainer _headers = null!;
    private VBoxContainer _rows = null!;
    private bool _mapWasOpen;

    public RoutePanelLayer()
    {
        Layer = 100;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        BuildUi();
        RefreshRoutes();
        _panel.Visible = false;
    }

    public override void _Process(double delta)
    {
        var map = NMapScreen.Instance;
        bool mapOpen = map is not null && map.IsOpen && map.IsVisibleInTree();
        bool shouldShow = mapOpen &&
                          SpireGpsSettings.RoutePlannerEnabled &&
                          SpireGpsSettings.RoutePanelEnabled;

        _panel.Visible = shouldShow;

        if (shouldShow)
            PositionPanel();

        if (mapOpen && !_mapWasOpen)
        {
            RefreshRoutes();
            RouteHighlighter.Refresh();
        }
        else if (!mapOpen && _mapWasOpen)
        {
            RouteHighlighter.Clear();
        }

        _mapWasOpen = mapOpen;
    }

    internal void RefreshRoutes()
    {
        if (!IsInsideTree())
            return;

        if (_autoSelectSuggested is not null)
            _autoSelectSuggested.SetPressedNoSignal(SpireGpsSettings.AutoHighlightPreferredRoute);

        RebuildHeaders();
        RebuildRows();
    }

    private void BuildUi()
    {
        _panel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(PanelWidth, 0)
        };
        AddChild(_panel);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 6);
        _panel.AddChild(outer);

        var title = new Label
        {
            Text = "SpireGPS — Routes",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 18);
        outer.AddChild(title);

        _status = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _status.AddThemeFontSizeOverride("font_size", 12);
        outer.AddChild(_status);

        _autoSelectSuggested = new CheckBox
        {
            Text = "Auto-select suggested route",
            ButtonPressed = SpireGpsSettings.AutoHighlightPreferredRoute,
            TooltipText = "Automatically select and highlight the ★ suggested route whenever routes are recalculated.",
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
        };
        _autoSelectSuggested.Toggled += OnAutoSelectSuggestedToggled;
        outer.AddChild(_autoSelectSuggested);

        _headers = new HBoxContainer();
        _headers.AddThemeConstantOverride("separation", 2);
        outer.AddChild(_headers);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, RowHeight * VisibleRows + 10),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto
        };
        outer.AddChild(scroll);

        _rows = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _rows.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(_rows);

        var help = new Label
        {
            Text = "Click a Route to highlight it. Click a column heading to sort.",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        help.AddThemeFontSizeOverride("font_size", 11);
        outer.AddChild(help);
    }

    private void OnAutoSelectSuggestedToggled(bool enabled)
    {
        SpireGpsSettings.AutoHighlightPreferredRoute = enabled;
        ModConfigBridge.SetValue("autoHighlightPreferredRoute", enabled);

        if (enabled)
            RoutePlannerService.SelectPreferredRoute();

        RebuildRows();
        RouteHighlighter.Refresh();
    }

    private void RebuildHeaders()
    {
        foreach (var child in _headers.GetChildren())
            child.QueueFree();

        AddHeader("Route", RouteSortMode.RouteOrder, RouteWidth, "Original left-to-right map route order");
        AddHeader("M", RouteSortMode.Monsters, CellWidth, "Monster rooms");
        AddHeader("?", RouteSortMode.Unknowns, CellWidth, "Unknown rooms");
        AddHeader("E", RouteSortMode.Elites, CellWidth, "Elite rooms");
        AddHeader("$", RouteSortMode.Shops, CellWidth, "Shops");
        AddHeader("R", RouteSortMode.RestSites, CellWidth, "Campsites / rest sites");
        AddHeader("T", RouteSortMode.Treasures, CellWidth, "Treasure rooms");
        AddHeader("Score", RouteSortMode.Preferred, ScoreWidth, "Suggested-route score from your configured weights");
    }

    private void AddHeader(string text, RouteSortMode mode, float width, string tooltip)
    {
        string indicator = RoutePlannerService.CurrentSort == mode
            ? (RoutePlannerService.SortDescending ? " ↓" : " ↑")
            : string.Empty;

        var button = new Button
        {
            Text = text + indicator,
            TooltipText = tooltip,
            CustomMinimumSize = new Vector2(width, RowHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
        };

        button.Pressed += () =>
        {
            RoutePlannerService.ToggleColumnSort(mode);
            RebuildHeaders();
            RebuildRows();
        };

        _headers.AddChild(button);
    }

    private void RebuildRows()
    {
        foreach (var child in _rows.GetChildren())
            child.QueueFree();

        var routes = RoutePlannerService.SortedRoutes;
        var preferred = RoutePlannerService.PreferredRoute;
        var selected = RoutePlannerService.SelectedRoute;

        _status.Text = routes.Count == 0
            ? "No complete route to the boss found"
            : $"{routes.Count} route{(routes.Count == 1 ? string.Empty : "s")} • ★ suggested";

        foreach (var route in routes)
        {
            var row = new HBoxContainer
            {
                CustomMinimumSize = new Vector2(0, RowHeight)
            };
            row.AddThemeConstantOverride("separation", 2);

            bool isPreferred = preferred?.Index == route.Index;
            bool isSelected = selected?.Index == route.Index;

            string prefix = (isSelected ? "▶ " : string.Empty) + (isPreferred ? "★ " : string.Empty);
            var routeButton = new Button
            {
                Text = $"{prefix}Route {route.Index}",
                TooltipText = route.Signature,
                CustomMinimumSize = new Vector2(RouteWidth, RowHeight)
            };
            routeButton.Pressed += () =>
            {
                RoutePlannerService.SelectRoute(route);
                RebuildRows();
                RouteHighlighter.Refresh();
            };
            row.AddChild(routeButton);

            AddCell(row, route.Monsters.ToString(), CellWidth);
            AddCell(row, route.Unknowns.ToString(), CellWidth);
            AddCell(row, route.Elites.ToString(), CellWidth);
            AddCell(row, route.Shops.ToString(), CellWidth);
            AddCell(row, route.RestSites.ToString(), CellWidth);
            AddCell(row, route.Treasures.ToString(), CellWidth);
            AddCell(row, RoutePlannerService.GetPreferredScore(route).ToString("0.##"), ScoreWidth);

            _rows.AddChild(row);
        }
    }

    private static void AddCell(HBoxContainer row, string text, float width)
    {
        row.AddChild(new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(width, RowHeight)
        });
    }

    private void PositionPanel()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float x = Math.Max(12f, viewport.X - PanelWidth - 24f);
        float y = Math.Max(72f, viewport.Y * 0.12f);
        _panel.Position = new Vector2(x, y);
    }
}
