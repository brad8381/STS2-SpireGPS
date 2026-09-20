using Godot;

namespace SpireGPS.UI;

internal static class PanelDrag
{
    internal static void Attach(Control handle, Control panel, Action? onMoved = null)
    {
        bool dragging = false;

        handle.MouseFilter = Control.MouseFilterEnum.Stop;
        handle.MouseDefaultCursorShape = Control.CursorShape.Move;
        if (string.IsNullOrWhiteSpace(handle.TooltipText))
            handle.TooltipText = "Drag to move this panel.";

        handle.Connect(
            Control.SignalName.GuiInput,
            Callable.From<InputEvent>(inputEvent =>
            {
                if (inputEvent is InputEventMouseButton button &&
                    button.ButtonIndex == MouseButton.Left)
                {
                    dragging = button.Pressed;
                    handle.AcceptEvent();
                    return;
                }

                if (!dragging || inputEvent is not InputEventMouseMotion motion)
                    return;

                panel.Position += motion.Relative;
                ClampToViewport(panel);
                onMoved?.Invoke();
                handle.AcceptEvent();
            }));
    }

    private static void ClampToViewport(Control panel)
    {
        Vector2 viewport = panel.GetViewport().GetVisibleRect().Size;
        Vector2 size = panel.Size;

        panel.Position = new Vector2(
            Math.Clamp(panel.Position.X, 0f, Math.Max(0f, viewport.X - Math.Max(40f, size.X))),
            Math.Clamp(panel.Position.Y, 0f, Math.Max(0f, viewport.Y - Math.Max(30f, size.Y))));
    }
}
