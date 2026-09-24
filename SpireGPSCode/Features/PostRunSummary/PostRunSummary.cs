using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using SpireGPS.Config;
using SpireGPS.Telemetry;
using SpireGPS.UI;

namespace SpireGPS.Features.PostRunSummary;

internal sealed record RunRecapSnapshot(
    string Title,
    string Summary,
    bool IsVictory);

internal static class PostRunSummary
{
    private const string LayerName = "BantersPostRunSummary";
    private const string MainMenuButtonName = "BanterRunRecapButton";
    private const string PrefSection = "run_recap";

    private static RunRecapSnapshot? _lastRecap;

    internal static bool HasLastRecap => GetLastRecap() is not null;

    internal static void Show(SerializableRun run, bool isVictory, bool isAbandoned)
    {
        if (!SpireGpsSettings.PostRunSummaryEnabled)
            return;

        string title = isVictory
            ? "Run Recap — Victory"
            : isAbandoned
                ? "Run Recap — Abandoned"
                : "Run Recap — Defeat";

        _lastRecap = new RunRecapSnapshot(
            title,
            BuildSummary(run, isVictory),
            isVictory);

        SaveLastRecap(_lastRecap);
        ShowSnapshot(_lastRecap);
    }

    internal static void ShowLast()
    {
        if (!SpireGpsSettings.PostRunSummaryEnabled)
            return;

        var recap = GetLastRecap();
        if (recap is not null)
            ShowSnapshot(recap);
    }

    internal static void DismissActive(bool fade)
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        var layer = tree.Root.GetNodeOrNull<CanvasLayer>(LayerName);
        if (layer is null)
            return;

        if (!fade)
        {
            layer.QueueFree();
            return;
        }

        var panel = layer.GetChildren().OfType<Control>().FirstOrDefault();
        if (panel is null || !GodotObject.IsInstanceValid(panel))
        {
            layer.QueueFree();
            return;
        }

        var tween = panel.CreateTween();
        tween.TweenProperty(panel, "modulate:a", 0f, 0.18f);
        tween.Finished += layer.QueueFree;
    }

    internal static void AttachMainMenuButton(NMainMenu menu)
    {
        if (!SpireGpsSettings.PostRunSummaryEnabled ||
            !HasLastRecap ||
            menu.GetNodeOrNull<Button>(MainMenuButtonName) is not null)
        {
            return;
        }

        var button = new Button
        {
            Name = MainMenuButtonName,
            Text = "RUN RECAP",
            Flat = true,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 0.78f,
            AnchorBottom = 0.78f,
            OffsetLeft = -320f,
            OffsetRight = -48f,
            OffsetTop = -26f,
            OffsetBottom = 30f,
            Alignment = HorizontalAlignment.Right,
            TooltipText = "Open the most recent Banter run recap."
        };

        button.AddThemeFontSizeOverride("font_size", 24);
        button.AddThemeColorOverride("font_color", new Color(0.88f, 0.80f, 0.58f));
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", new Color(0.96f, 0.78f, 0.27f));
        button.Pressed += ShowLast;

        menu.AddChild(button);
        button.Visible = !menu.SubmenuStack.SubmenusOpen;
        menu.SubmenuStack.Connect(
            NSubmenuStack.SignalName.StackModified,
            Callable.From(() =>
                button.Visible =
                    GodotObject.IsInstanceValid(button) &&
                    !menu.SubmenuStack.SubmenusOpen));
    }

    private static void ShowSnapshot(RunRecapSnapshot recap)
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        Callable.From(() =>
        {
            try
            {
                DismissActive(fade: false);

                var layer = new CanvasLayer
                {
                    Name = LayerName,
                    Layer = 250,
                    ProcessMode = Node.ProcessModeEnum.Always
                };

                var panel = BuildPanel(recap);
                layer.AddChild(panel);
                tree.Root.AddChild(layer);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"Post-run summary could not be shown: {ex.Message}");
            }
        }).CallDeferred();
    }

    private static PanelContainer BuildPanel(RunRecapSnapshot recap)
    {
        var panel = new PanelContainer
        {
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 0.12f,
            AnchorBottom = 0.12f,
            OffsetLeft = -585f,
            OffsetRight = -28f,
            OffsetTop = 0f,
            OffsetBottom = 0f,
            MouseFilter = Control.MouseFilterEnum.Stop
        };

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.025f, 0.03f, 0.04f, 0.97f),
            BorderColor = recap.IsVictory
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
            Text = recap.Title,
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
            Text = recap.Summary,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(500f, 0f)
        };
        summary.AddThemeFontSizeOverride("font_size", 16);
        outer.AddChild(summary);

        var hint = new Label
        {
            Text = "Detailed local run data is saved for Deck Evolution / Post-Mortem analysis.",
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

        void Close() => DismissActive(fade: false);

        close.Pressed += Close;
        dismiss.Pressed += Close;

        PanelChrome.Attach(
            "post_run_summary_v2",
            panel,
            top,
            outer);

        return panel;
    }

    private static void SaveLastRecap(RunRecapSnapshot recap)
    {
        LocalPreferences.Set(PrefSection, "title", recap.Title);
        LocalPreferences.Set(PrefSection, "summary", recap.Summary);
        LocalPreferences.Set(PrefSection, "victory", recap.IsVictory);
    }

    private static RunRecapSnapshot? GetLastRecap()
    {
        if (_lastRecap is not null)
            return _lastRecap;

        string title = LocalPreferences.GetString(PrefSection, "title", string.Empty);
        string summary = LocalPreferences.GetString(PrefSection, "summary", string.Empty);

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(summary))
            return null;

        _lastRecap = new RunRecapSnapshot(
            title,
            summary,
            LocalPreferences.GetBool(PrefSection, "victory", false));

        return _lastRecap;
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

            lines.Add($"Final HP: {player.CurrentHp}/{player.MaxHp}    Gold: {player.Gold}");
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

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class PostRunSummaryMainMenuPatch
{
    private static void Postfix(NMainMenu __instance)
    {
        try
        {
            PostRunSummary.DismissActive(fade: true);
            PostRunSummary.AttachMainMenuButton(__instance);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Run Recap main-menu integration skipped: {ex.Message}");
        }
    }
}
