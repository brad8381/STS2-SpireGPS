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
        bool hasPosition = LocalPreferences.GetBool(Section, id + ".has_position", false);
        Vector2 savedPosition = LocalPreferences.GetVector2(
            Section,
            id + ".position",
            panel.Position);

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
        Vector2 normalMinimum = panel.CustomMinimumSize;
        Vector2 normalSize = panel.Size;

        void ApplyMinimized()
        {
            if (minimized)
            {
                previousVisibility.Clear();
                foreach (var item in bodyItems)
                {
                    previousVisibility[item] = item.Visible;
                    item.Visible = false;
                }

                normalSize = panel.Size;
                panel.CustomMinimumSize = new Vector2(normalMinimum.X, 0f);
                panel.Size = new Vector2(
                    Math.Max(panel.Size.X, Math.Max(120f, normalMinimum.X)),
                    Math.Max(34f, header.Size.Y + 8f));
            }
            else
            {
                foreach (var item in bodyItems)
                {
                    item.Visible = previousVisibility.TryGetValue(item, out bool wasVisible)
                        ? wasVisible
                        : true;
                }

                panel.CustomMinimumSize = normalMinimum;
                if (normalSize.Y > 0f)
                    panel.Size = normalSize;
            }

            minimizeButton.Text = minimized ? "+" : "−";
            minimizeButton.TooltipText = minimized ? "Expand panel." : "Minimize panel.";
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
                LocalPreferences.Set(Section, id + ".has_position", true);
                LocalPreferences.Set(Section, id + ".position", panel.Position);
                onMoved?.Invoke();
            },
            () => !locked);

        Callable.From(() =>
        {
            normalMinimum = panel.CustomMinimumSize;
            normalSize = panel.Size;

            if (hasPosition)
            {
                panel.Position = savedPosition;
                onMoved?.Invoke();
            }

            ApplyMinimized();
        }).CallDeferred();
    }
}
