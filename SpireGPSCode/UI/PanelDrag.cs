using Godot;

namespace SpireGPS.UI;

internal static class PanelDrag
{
    // STS2's top HUD overlaps the raw viewport. Keep every Banter panel's
    // draggable chrome below it so a panel can never become unrecoverable.
    private const float SafeTopMargin = 96f;
    private const float SafeSideMargin = 12f;
    private const float SafeBottomMargin = 12f;
    internal static void Attach(
        Control handle,
        Control panel,
        Action? onMoved = null,
        Func<bool>? canDrag = null)
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
                    if (button.Pressed && canDrag is not null && !canDrag())
                    {
                        dragging = false;
                        handle.AcceptEvent();
                        return;
                    }

                    dragging = button.Pressed;
                    handle.AcceptEvent();
                    return;
                }

                if (!dragging || inputEvent is not InputEventMouseMotion motion)
                    return;

                panel.GlobalPosition += motion.Relative;
                ClampToViewport(panel);
                onMoved?.Invoke();
                handle.AcceptEvent();
            }));
    }

    internal static void ClampToViewport(Control panel)
    {
        Rect2 visible = panel.GetViewport().GetVisibleRect();
        Vector2 size = panel.Size;
        Vector2 position = panel.GlobalPosition;

        float minX = visible.Position.X + SafeSideMargin;
        float minY = visible.Position.Y + SafeTopMargin;
        Vector2 visibleEnd = visible.Position + visible.Size;
        float maxX = visibleEnd.X - SafeSideMargin - Math.Max(40f, size.X);
        float maxY = visibleEnd.Y - SafeBottomMargin - Math.Max(30f, size.Y);

        panel.GlobalPosition = new Vector2(
            Math.Clamp(position.X, minX, Math.Max(minX, maxX)),
            Math.Clamp(position.Y, minY, Math.Max(minY, maxY)));
    }
}
