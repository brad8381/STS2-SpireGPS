using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using SpireGPS.Config;
using SpireGPS.Telemetry;

namespace SpireGPS.Features.PostRunSummary;

internal static class PostRunSummary
{
    private const string LayerName = "BantersPostRunSummary";

    internal static void Show(SerializableRun run, bool isVictory, bool isAbandoned)
    {
        if (!SpireGpsSettings.PostRunSummaryEnabled ||
            Engine.GetMainLoop() is not SceneTree tree)
        {
            return;
        }

        Callable.From(() =>
        {
            try
            {
                tree.Root.GetNodeOrNull<CanvasLayer>(LayerName)?.QueueFree();

                var layer = new CanvasLayer
                {
                    Name = LayerName,
                    Layer = 250,
                    ProcessMode = Node.ProcessModeEnum.Always
                };

                var panel = BuildPanel(run, isVictory, isAbandoned);
                layer.AddChild(panel);
                tree.Root.AddChild(layer);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"Post-run summary could not be shown: {ex.Message}");
            }
        }).CallDeferred();
    }

    private static PanelContainer BuildPanel(
        SerializableRun run,
        bool isVictory,
        bool isAbandoned)
    {
        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0.10f,
            AnchorBottom = 0.10f,
            OffsetLeft = -270f,
            OffsetRight = 270f,
            OffsetTop = 0f,
            OffsetBottom = 0f,
            MouseFilter = Control.MouseFilterEnum.Stop
        };

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.025f, 0.03f, 0.04f, 0.97f),
            BorderColor = isVictory
                ? new Color(0.82f, 0.67f, 0.24f, 0.95f)
                : new Color(0.70f, 0.30f, 0.26f, 0.95f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 14,
            CornerRadiusTopRight = 14,
            CornerRadiusBottomLeft = 14,
            CornerRadiusBottomRight = 14,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 14,
            ContentMarginBottom = 14
        };
        panel.AddThemeStyleboxOverride("panel", style);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 8);
        panel.AddChild(outer);

        var top = new HBoxContainer();
        outer.AddChild(top);

        var title = new Label
        {
            Text = isVictory
                ? "Run Recap — Victory"
                : isAbandoned
                    ? "Run Recap — Abandoned"
                    : "Run Recap — Defeat",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 22);
        top.AddChild(title);

        var close = new Button
        {
            Text = "×",
            TooltipText = "Close recap",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(34f, 30f)
        };
        top.AddChild(close);

        var summary = new Label
        {
            Text = BuildSummary(run, isVictory),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(500f, 0f)
        };
        summary.AddThemeFontSizeOverride("font_size", 16);
        outer.AddChild(summary);

        var hint = new Label
        {
            Text = "Banter's Tweak's saved the detailed local run data for later Deck Evolution / Post-Mortem analysis.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", new Color(0.72f, 0.72f, 0.72f));
        outer.AddChild(hint);

        var actions = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End
        };
        actions.AddThemeConstantOverride("separation", 8);
        outer.AddChild(actions);

        var openData = new Button
        {
            Text = "Open Run Data",
            FocusMode = Control.FocusModeEnum.None
        };
        openData.Pressed += () =>
        {
            try
            {
                string path = RunTelemetryService.GetAbsoluteBaseDirectory();
                Directory.CreateDirectory(path);
                OS.ShellShowInFileManager(path);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"Could not open Banter run data: {ex.Message}");
            }
        };
        actions.AddChild(openData);

        var dismiss = new Button
        {
            Text = "Close",
            FocusMode = Control.FocusModeEnum.None
        };
        actions.AddChild(dismiss);

        void Close()
            => panel.GetParent<CanvasLayer>()?.QueueFree();

        close.Pressed += Close;
        dismiss.Pressed += Close;

        return panel;
    }

    private static string BuildSummary(SerializableRun run, bool isVictory)
    {
        var player = LocalContext.GetMe(run);
        int floors = run.VisitedMapCoords.Count;
        int acts = Math.Max(1, run.CurrentActIndex + 1);
        TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0L, run.RunTime));

        var history = run.MapPointHistory.SelectMany(act => act).ToArray();
        int monsters = history.Count(point => point.MapPointType == MapPointType.Monster);
        int elites = history.Count(point => point.MapPointType == MapPointType.Elite);
        int shops = history.Count(point => point.MapPointType == MapPointType.Shop);
        int rests = history.Count(point => point.MapPointType == MapPointType.RestSite);
        int treasures = history.Count(point => point.MapPointType == MapPointType.Treasure);
        int unknowns = history.Count(point => point.MapPointType == MapPointType.Unknown);

        var lines = new List<string>
        {
            $"Act reached: {acts}    Floors visited: {floors}    Ascension: {run.Ascension}",
            $"Run time: {FormatDuration(duration)}    Events seen: {run.EventsSeen.Count}",
            $"Path: {monsters} fights • {elites} elites • {unknowns} ? • {shops} shops • {rests} rests • {treasures} treasures"
        };

        if (player is not null)
        {
            int upgradedCards = player.Deck.Count(card => card.CurrentUpgradeLevel > 0);

            lines.Add(
                $"Final HP: {player.CurrentHp}/{player.MaxHp}    Gold: {player.Gold}");
            lines.Add(
                $"Deck: {player.Deck.Count} cards ({upgradedCards} upgraded)    Relics: {player.Relics.Count}    Potions: {player.Potions.Count}");
        }

        try
        {
            int score = ScoreUtility.CalculateScore(run, isVictory);
            lines.Add($"Score: {score}");
        }
        catch
        {
            // A modded run may contain scoring data the current game cannot
            // resolve cleanly. The recap should still display everything else.
        }

        return string.Join("\n", lines);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}";

        return $"{duration.Minutes}:{duration.Seconds:00}";
    }
}
