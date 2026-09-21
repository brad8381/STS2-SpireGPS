using Godot;

namespace SpireGPS.Config;

internal static class ModConfigAccordion
{
    private const string ControllerName = "BantersModConfigAccordion";

    internal static void EnsureInstalled()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        if (tree.Root.GetNodeOrNull<BanterAccordionController>(ControllerName) is not null)
            return;

        tree.Root.CallDeferred(
            Node.MethodName.AddChild,
            new BanterAccordionController { Name = ControllerName });
    }
}

internal partial class BanterAccordionController : Node
{
    private const string StateSection = "modconfig_sections";
    private static readonly string[] SectionNames =
    {
        "Choice Compare",
        "Compatibility",
        "Deck X-Ray",
        "Drawing Palette",
        "Ghost Turn Planner",
        "Map Legend",
        "Multiplayer Pings",
        "Potion Guard",
        "Relic Progress",
        "Route Planner",
        "Run Telemetry",
        "Synergy Hints",
        "Trading",
        "Trigger Inspector",
        "Turn Guard",
        "Turn Timeline",
        "UI Layout",
        "Wishlist"
    };

    public override void _Ready()
    {
        if (GetTree() is not { } tree)
            return;

        tree.NodeAdded += OnNodeAdded;
        Callable.From(() => DecorateExisting(tree.Root)).CallDeferred();
    }

    public override void _ExitTree()
    {
        if (GetTree() is { } tree)
            tree.NodeAdded -= OnNodeAdded;
    }

    private static void OnNodeAdded(Node node)
    {
        Node? current = node;
        while (current is not null)
        {
            if (current is VBoxContainer box &&
                box.Name == $"Entries_{MainFile.ModId}")
            {
                Callable.From(() =>
                {
                    if (GodotObject.IsInstanceValid(box) &&
                        !box.HasMeta("BantersAccordionReady"))
                    {
                        DecorateEntries(box);
                    }
                }).CallDeferred();
                return;
            }

            current = current.GetParent();
        }
    }

    private static void DecorateExisting(Node node)
    {
        if (node is VBoxContainer box &&
            box.Name == $"Entries_{MainFile.ModId}" &&
            !box.HasMeta("BantersAccordionReady"))
        {
            DecorateEntries(box);
        }

        foreach (Node child in node.GetChildren())
            DecorateExisting(child);
    }

    private static void DecorateEntries(VBoxContainer box)
    {
        var children = box.GetChildren().OfType<CanvasItem>().ToArray();
        var headers = new List<(Label Label, string Name, int Index)>();

        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] is not Label label)
                continue;

            string raw = label.Text.Trim();
            if (!SectionNames.Contains(raw, StringComparer.Ordinal))
                continue;

            headers.Add((label, raw, i));
        }

        if (headers.Count == 0)
            return;

        for (int h = 0; h < headers.Count; h++)
        {
            var header = headers[h];
            int end = h + 1 < headers.Count ? headers[h + 1].Index : children.Length;
            string key = NormalizeKey(header.Name);
            bool collapsed = LocalPreferences.GetBool(StateSection, key, true);

            var body = children
                .Skip(header.Index + 1)
                .Take(end - header.Index - 1)
                .ToArray();

            header.Label.MouseFilter = Control.MouseFilterEnum.Stop;
            header.Label.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            header.Label.TooltipText = "Click to expand/collapse this Banter module.";
            header.Label.Name = $"BanterSection_{key}";

            void Apply()
            {
                header.Label.Text = $"{(collapsed ? "▶" : "▼")}  {header.Name}";
                foreach (var item in body)
                    item.Visible = !collapsed;
            }

            header.Label.Connect(
                Control.SignalName.GuiInput,
                Callable.From<InputEvent>(inputEvent =>
                {
                    if (inputEvent is not InputEventMouseButton
                        {
                            Pressed: true,
                            ButtonIndex: MouseButton.Left
                        })
                    {
                        return;
                    }

                    collapsed = !collapsed;
                    LocalPreferences.Set(StateSection, key, collapsed);
                    Apply();
                    header.Label.AcceptEvent();
                }));

            Apply();
        }

        box.SetMeta("BantersAccordionReady", true);
    }

    private static string NormalizeKey(string value)
        => new(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
}
