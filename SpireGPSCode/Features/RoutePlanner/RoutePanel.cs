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
    private CheckBox _autoSelectNextNode = null!;
    private CheckBox _confirmAutoTravel = null!;
    private OptionButton _priority1 = null!;
    private OptionButton _priority2 = null!;
    private OptionButton _priority3 = null!;
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
            RouteAutoSelector.OnMapOpened();
        }
        else if (!mapOpen && _mapWasOpen)
        {
            RouteHighlighter.Clear();
            RouteAutoSelector.OnMapClosed();
        }

        _mapWasOpen = mapOpen;
    }

    internal void RefreshRoutes()
    {
        if (!IsInsideTree())
            return;

        _autoSelectSuggested.SetPressedNoSignal(SpireGpsSettings.AutoHighlightPreferredRoute);
        _autoSelectNextNode.SetPressedNoSignal(SpireGpsSettings.AutoSelectNextNode);
        _confirmAutoTravel.SetPressedNoSignal(SpireGpsSettings.ConfirmAutoTravel);
        SetPrioritySelection(_priority1, SpireGpsSettings.RoutePriority1);
        SetPrioritySelection(_priority2, SpireGpsSettings.RoutePriority2);
        SetPrioritySelection(_priority3, SpireGpsSettings.RoutePriority3);

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
            Text = "Route Planner",
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
            Text = "Auto-highlight suggested route",
            ButtonPressed = SpireGpsSettings.AutoHighlightPreferredRoute,
            TooltipText = "Automatically make the ★ suggested route the highlighted route whenever routes are recalculated.",
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
        };
        _autoSelectSuggested.Toggled += OnAutoSelectSuggestedToggled;
        outer.AddChild(_autoSelectSuggested);

        var travelRow = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center
        };
        travelRow.AddThemeConstantOverride("separation", 12);

        _autoSelectNextNode = new CheckBox
        {
            Text = "Auto-select next suggested node",
            ButtonPressed = SpireGpsSettings.AutoSelectNextNode,
            TooltipText = "Select/vote for the next node on the currently highlighted route when the map opens.",
        };
        _autoSelectNextNode.Toggled += OnAutoSelectNextNodeToggled;
        travelRow.AddChild(_autoSelectNextNode);

        _confirmAutoTravel = new CheckBox
        {
            Text = "Confirm before auto-travel",
            ButtonPressed = SpireGpsSettings.ConfirmAutoTravel,
            TooltipText = "Show a confirmation before Route Planner selects the next map node.",
        };
        _confirmAutoTravel.Toggled += OnConfirmAutoTravelToggled;
        travelRow.AddChild(_confirmAutoTravel);
        outer.AddChild(travelRow);

        var priorityRow = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center
        };
        priorityRow.AddThemeConstantOverride("separation", 5);
        priorityRow.AddChild(new Label
        {
            Text = "Priority:",
            VerticalAlignment = VerticalAlignment.Center
        });

        _priority1 = CreatePriorityDropdown(SpireGpsSettings.RoutePriority1, 1);
        _priority2 = CreatePriorityDropdown(SpireGpsSettings.RoutePriority2, 2);
        _priority3 = CreatePriorityDropdown(SpireGpsSettings.RoutePriority3, 3);

        priorityRow.AddChild(_priority1);
        priorityRow.AddChild(new Label { Text = ">", VerticalAlignment = VerticalAlignment.Center });
        priorityRow.AddChild(_priority2);
        priorityRow.AddChild(new Label { Text = ">", VerticalAlignment = VerticalAlignment.Center });
        priorityRow.AddChild(_priority3);
        outer.AddChild(priorityRow);

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
            Text = "★ follows priority order, then weighted score. Click a route to highlight it.",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        help.AddThemeFontSizeOverride("font_size", 11);
        outer.AddChild(help);
    }

    private OptionButton CreatePriorityDropdown(string selected, int slot)
    {
        var dropdown = new OptionButton
        {
            CustomMinimumSize = new Vector2(120f, RowHeight),
            TooltipText = $"Suggested-route priority {slot}. Earlier priorities are compared first."
        };

        foreach (string option in RoutePriority.Options)
            dropdown.AddItem(option);

        dropdown.Select(RoutePriority.IndexOf(selected));
        dropdown.ItemSelected += index => OnPriorityChanged(slot, RoutePriority.Options[(int)index]);
        return dropdown;
    }

    private static void SetPrioritySelection(OptionButton dropdown, string value)
    {
        dropdown.Select(RoutePriority.IndexOf(value));
    }

    private void OnPriorityChanged(int slot, string value)
    {
        switch (slot)
        {
            case 1: SpireGpsSettings.RoutePriority1 = value; break;
            case 2: SpireGpsSettings.RoutePriority2 = value; break;
            case 3: SpireGpsSettings.RoutePriority3 = value; break;
        }

        ModConfigBridge.SetValue($"routePriority{slot}", value);
        RoutePlannerService.RecalculateSuggestion();

        if (RoutePlannerService.CurrentSort == RouteSortMode.Preferred)
            RoutePlannerService.Sort(RouteSortMode.Preferred, RoutePlannerService.SortDescending);

        RebuildRows();
        RouteHighlighter.Refresh();
        RouteAutoSelector.TrySelectNext(force: true);
    }

    private void OnAutoSelectSuggestedToggled(bool enabled)
    {
        SpireGpsSettings.AutoHighlightPreferredRoute = enabled;
        ModConfigBridge.SetValue("autoHighlightPreferredRoute", enabled);

        if (enabled)
            RoutePlannerService.SelectPreferredRoute();

        RebuildRows();
        RouteHighlighter.Refresh();
        if (enabled)
            RouteAutoSelector.TrySelectNext(force: true);
    }

    private void OnAutoSelectNextNodeToggled(bool enabled)
    {
        SpireGpsSettings.AutoSelectNextNode = enabled;
        ModConfigBridge.SetValue("routeAutoSelectNextNode", enabled);

        if (enabled)
            RouteAutoSelector.TrySelectNext(force: true);
        else
            RouteAutoSelector.OnMapClosed();
    }

    private void OnConfirmAutoTravelToggled(bool enabled)
    {
        SpireGpsSettings.ConfirmAutoTravel = enabled;
        ModConfigBridge.SetValue("routeConfirmAutoTravel", enabled);
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
        AddHeader("Score", RouteSortMode.Preferred, ScoreWidth, "Suggested route: priorities first, weighted score as tie-break");
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
                RouteAutoSelector.TrySelectNext(force: true);
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
