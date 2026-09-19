using Godot;

namespace SpireGPS.UI;

internal static class SpireGpsToast
{
    private const string LayerName = "SpireGPSToast";
    private static readonly Queue<(string text, double seconds)> Pending = new();

    private static SpireGpsToastLayer? _instance;
    private static bool _installQueued;

    internal static void EnsureInstalled()
    {
        if (_instance is not null && GodotObject.IsInstanceValid(_instance))
            return;

        if (_installQueued || Engine.GetMainLoop() is not SceneTree tree)
            return;

        var existing = tree.Root.GetNodeOrNull<SpireGpsToastLayer>(LayerName);
        if (existing is not null)
        {
            _instance = existing;
            return;
        }

        _installQueued = true;
        tree.Root.CallDeferred(Node.MethodName.AddChild, new SpireGpsToastLayer { Name = LayerName });
    }

    internal static void Show(string text, double seconds = 2.5)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (_instance is not null &&
            GodotObject.IsInstanceValid(_instance) &&
            _instance.IsReady)
        {
            _instance.ShowMessage(text, seconds);
            return;
        }

        Pending.Enqueue((text, seconds));
        EnsureInstalled();
    }

    internal static void RegisterInstance(SpireGpsToastLayer layer)
    {
        _instance = layer;
        _installQueued = false;

        while (Pending.Count > 0)
        {
            var item = Pending.Dequeue();
            layer.ShowMessage(item.text, item.seconds);
        }
    }

    internal static void UnregisterInstance(SpireGpsToastLayer layer)
    {
        if (ReferenceEquals(_instance, layer))
            _instance = null;
    }
}

internal partial class SpireGpsToastLayer : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _label = null!;

    private double _hideAt;
    private string? _queuedText;
    private double _queuedSeconds;
    private int _framesUntilReveal;

    internal bool IsReady { get; private set; }

    public SpireGpsToastLayer()
    {
        Layer = 120;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        _panel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
            CustomMinimumSize = new Vector2(360f, 0f)
        };

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.08f, 0.09f, 0.94f),
            BorderColor = new Color(0.95f, 0.72f, 0.22f, 0.9f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 16,
            ContentMarginRight = 16,
            ContentMarginTop = 10,
            ContentMarginBottom = 10
        };
        _panel.AddThemeStyleboxOverride("panel", style);

        _label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(330f, 38f)
        };
        _label.AddThemeFontSizeOverride("font_size", 16);

        _panel.AddChild(_label);
        AddChild(_panel);

        IsReady = true;
        SpireGpsToast.RegisterInstance(this);
    }

    public override void _ExitTree()
    {
        IsReady = false;
        SpireGpsToast.UnregisterInstance(this);
    }

    public override void _Process(double delta)
    {
        if (!IsReady)
            return;

        if (_framesUntilReveal > 0)
        {
            _framesUntilReveal--;

            if (_framesUntilReveal == 0)
                RevealQueuedMessage();
        }

        if (_panel.Visible && Time.GetTicksMsec() / 1000.0 >= _hideAt)
            _panel.Visible = false;

        if (_panel.Visible)
            PositionPanel();
    }

    internal void ShowMessage(string text, double seconds)
    {
        if (!IsReady || string.IsNullOrWhiteSpace(text))
            return;

        _queuedText = text;
        _queuedSeconds = Math.Max(0.5, seconds);

        // Populate the label immediately, but keep the panel hidden for two
        // complete UI frames so the theme/font and container layout are valid.
        _label.Text = text;
        _panel.Visible = false;
        _panel.ResetSize();
        _framesUntilReveal = 2;
    }

    private void RevealQueuedMessage()
    {
        if (string.IsNullOrWhiteSpace(_queuedText))
            return;

        _label.Text = _queuedText;
        _panel.ResetSize();

        _hideAt = Time.GetTicksMsec() / 1000.0 + _queuedSeconds;
        _panel.Visible = true;
        PositionPanel();

        _queuedText = null;
    }

    private void PositionPanel()
    {
        var viewport = GetViewport().GetVisibleRect().Size;

        _panel.Position = new Vector2(
            Math.Max(12f, (viewport.X - _panel.Size.X) * 0.5f),
            Math.Max(12f, viewport.Y * 0.64f));
    }
}
