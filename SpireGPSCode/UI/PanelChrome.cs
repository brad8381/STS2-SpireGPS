using Godot;
using SpireGPS.Config;

namespace SpireGPS.UI;

internal static class PanelChrome
{
    private const string Section = "panel_layout";

    internal static void Attach(
        string id,
        Control panel,
        HBoxContainer header,
        Container contentContainer,
        Action? onMoved = null)
    {
        bool locked = LocalPreferences.GetBool(Section, id + ".locked", false);
        bool minimized = LocalPreferences.GetBool(Section, id + ".minimized", false);
        // v2 resets the first layout experiment, which stored anchored local
        // positions and could leave panels pinned under the top bar.
        bool hasPosition = LocalPreferences.GetBool(Section, id + ".has_position_v2", false);
        Vector2 savedPosition = LocalPreferences.GetVector2(
            Section,
            id + ".position_v2",
            panel.GlobalPosition);

        var moveHandle = new Label
        {
            Text = "::",
            TooltipText = "Drag to move this panel.",
            MouseFilter = Control.MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(26f, 24f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        moveHandle.AddThemeFontSizeOverride("font_size", 16);
        header.AddChild(moveHandle);
        header.MoveChild(moveHandle, 0);

        var lockButton = new Button
        {
            Text = locked ? "U" : "L",
            TooltipText = locked
                ? "Unlock panel position."
                : "Lock panel position.",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(30f, 24f)
        };
        header.AddChild(lockButton);

        var minimizeButton = new Button
        {
            Text = minimized ? "+" : "−",
            TooltipText = minimized ? "Expand panel." : "Minimize panel.",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(30f, 24f)
        };
        header.AddChild(minimizeButton);

        var bodyItems = contentContainer
            .GetChildren()
            .OfType<CanvasItem>()
            .Where(item => !ReferenceEquals(item, header))
            .ToArray();

        var previousVisibility = new Dictionary<CanvasItem, bool>();
        bool minimizedApplied = false;

        void ApplyMinimized()
        {
            if (minimized && !minimizedApplied)
            {
                previousVisibility.Clear();
                foreach (var item in bodyItems)
                {
                    previousVisibility[item] = item.Visible;
                    item.Visible = false;
                }

                minimizedApplied = true;
            }
            else if (!minimized && minimizedApplied)
            {
                foreach (var item in bodyItems)
                {
                    if (previousVisibility.TryGetValue(item, out bool wasVisible))
                        item.Visible = wasVisible;
                }

                minimizedApplied = false;
            }

            minimizeButton.Text = minimized ? "+" : "−";
            minimizeButton.TooltipText = minimized ? "Expand panel." : "Minimize panel.";
            Callable.From(panel.ResetSize).CallDeferred();
        }

        lockButton.Pressed += () =>
        {
            locked = !locked;
            lockButton.Text = locked ? "U" : "L";
            lockButton.TooltipText = locked
                ? "Unlock panel position."
                : "Lock panel position.";
            LocalPreferences.Set(Section, id + ".locked", locked);
        };

        minimizeButton.Pressed += () =>
        {
            minimized = !minimized;
            LocalPreferences.Set(Section, id + ".minimized", minimized);
            ApplyMinimized();
        };

        PanelDrag.Attach(
            moveHandle,
            panel,
            () =>
            {
                LocalPreferences.Set(Section, id + ".has_position_v2", true);
                LocalPreferences.Set(Section, id + ".position_v2", panel.GlobalPosition);
                onMoved?.Invoke();
            },
            () => !locked);

        Callable.From(() =>
        {
            if (hasPosition)
            {
                panel.GlobalPosition = savedPosition;
                PanelDrag.ClampToViewport(panel);
                onMoved?.Invoke();
            }

            ApplyMinimized();
        }).CallDeferred();
    }
}
