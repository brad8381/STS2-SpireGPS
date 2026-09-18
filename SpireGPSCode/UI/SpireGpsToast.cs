using Godot;

namespace SpireGPS.UI;

internal static class SpireGpsToast
{
    private const string LayerName = "SpireGPSToast";

    internal static void EnsureInstalled()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        if (tree.Root.GetNodeOrNull<SpireGpsToastLayer>(LayerName) is not null)
            return;

        tree.Root.CallDeferred(Node.MethodName.AddChild, new SpireGpsToastLayer { Name = LayerName });
    }

    internal static void Show(string text, double seconds = 2.5)
    {
        EnsureInstalled();
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        void TryShow()
        {
            tree.Root.GetNodeOrNull<SpireGpsToastLayer>(LayerName)?.ShowMessage(text, seconds);
        }

        if (tree.Root.GetNodeOrNull<SpireGpsToastLayer>(LayerName) is null)
            tree.ProcessFrame += OneFrame;
        else
            TryShow();

        void OneFrame()
        {
            tree.ProcessFrame -= OneFrame;
            TryShow();
        }
    }
}

internal partial class SpireGpsToastLayer : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _label = null!;
    private double _hideAt;
    private bool _ready;
    private string? _pendingText;
    private double _pendingSeconds;

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
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _label.AddThemeFontSizeOverride("font_size", 16);
        _panel.AddChild(_label);
        AddChild(_panel);

        _ready = true;
        if (_pendingText is not null)
        {
            string text = _pendingText;
            double seconds = _pendingSeconds;
            _pendingText = null;
            ShowMessage(text, seconds);
        }
    }

    public override void _Process(double delta)
    {
        if (!_ready)
            return;

        if (_panel.Visible && Time.GetTicksMsec() / 1000.0 >= _hideAt)
            _panel.Visible = false;

        if (_panel.Visible)
            PositionPanel();
    }

    internal void ShowMessage(string text, double seconds)
    {
        if (!_ready)
        {
            _pendingText = text;
            _pendingSeconds = seconds;
            return;
        }

        _label.Text = text;
        _hideAt = Time.GetTicksMsec() / 1000.0 + Math.Max(0.5, seconds);
        _panel.Visible = true;
        _panel.ResetSize();
        _panel.CallDeferred(Control.MethodName.ResetSize);
        PositionPanel();
    }

    private void PositionPanel()
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        _panel.Position = new Vector2(
            Math.Max(12f, (viewport.X - _panel.Size.X) * 0.5f),
            Math.Max(12f, viewport.Y * 0.78f));
    }
}
