using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using SpireGPS.Config;

namespace SpireGPS.Features.TurnTimeline;

internal static class TurnTimelineService
{
    internal static IReadOnlyList<string> BuildTimeline()
    {
        var lines = new List<string>();

        if (!SpireGpsSettings.TurnTimelineEnabled ||
            MainFile.RunState is null)
        {
            return lines;
        }

        var player = LocalContext.GetMe(MainFile.RunState);
        var state = player?.Creature.CombatState;
        var pcs = player?.PlayerCombatState;

        if (player is null || state is null || pcs is null)
            return lines;

        int index = 1;

        int ethereal = pcs.Hand.Cards.Count(card =>
            card.Keywords.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Ethereal));

        if (ethereal > 0)
            lines.Add($"{index++}. {ethereal} Ethereal {(ethereal == 1 ? "card" : "cards")} will Exhaust.");

        int totalIncoming = 0;

        foreach (var enemy in state.Enemies.Where(enemy => enemy.IsAlive))
        {
            string enemyName = enemy.Monster?.Title.GetFormattedText() ?? "Enemy";
            int projectedHp = enemy.CurrentHp;

            int poisonDamage = 0;
            try
            {
                var poison = enemy.GetPower<PoisonPower>();
                poisonDamage = poison?.CalculateTotalDamageNextTurn() ?? 0;
            }
            catch { }

            int plagueDamage = GetPlagueDamage(enemy);

            if (poisonDamage > 0)
            {
                projectedHp -= poisonDamage;
                lines.Add($"{index++}. {enemyName}: Poison {poisonDamage} -> {Math.Max(0, projectedHp)} HP.");
            }

            if (plagueDamage > 0)
            {
                projectedHp -= plagueDamage;
                lines.Add($"{index++}. {enemyName}: Plague {plagueDamage} -> {Math.Max(0, projectedHp)} HP.");
            }

            if (projectedHp <= 0)
            {
                lines.Add($"{index++}. {enemyName}: dies before acting.");
                continue;
            }

            try
            {
                int attackDamage = 0;
                var nonAttack = new List<string>();

                foreach (var intent in enemy.Monster?.NextMove.Intents ?? Array.Empty<MegaCrit.Sts2.Core.MonsterMoves.Intents.Intent>())
                {
                    if (intent is AttackIntent attack)
                    {
                        attackDamage += attack.GetTotalDamage(state.PlayerCreatures, enemy);
                    }
                    else
                    {
                        string name = intent.GetType().Name.Replace("Intent", string.Empty, StringComparison.Ordinal);
                        if (!string.IsNullOrWhiteSpace(name))
                            nonAttack.Add(name);
                    }
                }

                if (attackDamage > 0)
                {
                    totalIncoming += attackDamage;
                    lines.Add($"{index++}. {enemyName}: attack intent {attackDamage} total damage.");
                }

                if (nonAttack.Count > 0)
                    lines.Add($"{index++}. {enemyName}: {string.Join(", ", nonAttack.Distinct())}.");
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"Turn Timeline skipped intents for {enemyName}: {ex.Message}");
                lines.Add($"{index++}. {enemyName}: intent details ?");
            }
        }

        if (state.Players.Count == 1)
        {
            int block = player.Creature.Block;
            int knownHpLoss = Math.Max(0, totalIncoming - block);
            int projectedPlayerHp = Math.Max(0, player.Creature.CurrentHp - knownHpLoss);

            lines.Add(
                $"{index++}. Known attacks: {totalIncoming} damage - {block} current Block -> ~{projectedPlayerHp} HP.");

            lines.Add(
                $"{index++}. Next turn: up to {pcs.MaxEnergy} base energy before start-of-turn effects.");
        }
        else
        {
            lines.Add($"{index++}. Known enemy attack intents: {totalIncoming} total damage across current targets.");
        }

        if (lines.Count == 0)
            lines.Add("No deterministic end-turn events identified.");

        return lines;
    }

    private static int GetPlagueDamage(Creature enemy)
    {
        try
        {
            var plague = enemy.Powers.FirstOrDefault(power =>
                power.GetType().FullName == "PB.Powers.PlaguePower" ||
                (power.GetType().Name == "PlaguePower" &&
                 string.Equals(
                     power.GetType().Assembly.GetName().Name,
                     "PlagueBringer",
                     StringComparison.OrdinalIgnoreCase)));

            return plague is null ? 0 : Math.Max(0, (int)plague.Amount);
        }
        catch
        {
            return 0;
        }
    }
}

internal partial class TurnTimelinePanel : PanelContainer
{
    private Label _body = null!;
    private Button _toggle = null!;
    private bool _expanded;
    private ulong _nextRefreshAt;

    public TurnTimelinePanel()
    {
        MouseFilter = Control.MouseFilterEnum.Stop;
        ZIndex = 92;

        AnchorLeft = 1f;
        AnchorRight = 1f;
        AnchorTop = 0f;
        AnchorBottom = 0f;
        OffsetLeft = -430f;
        OffsetRight = -18f;
        OffsetTop = 82f;
        OffsetBottom = 128f;
    }

    public override void _Ready()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.035f, 0.04f, 0.05f, 0.94f),
            BorderColor = new Color(0.72f, 0.55f, 0.28f, 0.9f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 7,
            CornerRadiusTopRight = 7,
            CornerRadiusBottomLeft = 7,
            CornerRadiusBottomRight = 7,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 6,
            ContentMarginBottom = 6
        };
        AddThemeStyleboxOverride("panel", style);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 5);
        AddChild(box);

        _toggle = new Button
        {
            Text = "Turn Timeline",
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Show deterministic effects currently known to happen if you end the turn now."
        };
        _toggle.Pressed += () =>
        {
            _expanded = !_expanded;
            _body.Visible = _expanded;
            _toggle.Text = _expanded ? "Turn Timeline ▲" : "Turn Timeline";
            Refresh();
        };
        box.AddChild(_toggle);

        _body = new Label
        {
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(390f, 0f)
        };
        _body.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(_body);
    }

    public override void _Process(double delta)
    {
        if (!SpireGpsSettings.TurnTimelineEnabled)
        {
            Visible = false;
            return;
        }

        Visible = true;

        if (!_expanded)
            return;

        ulong now = Time.GetTicksMsec();
        if (now >= _nextRefreshAt)
        {
            _nextRefreshAt = now + 200;
            Refresh();
        }
    }

    private void Refresh()
    {
        if (!_expanded)
            return;

        _body.Text = string.Join("\n", TurnTimelineService.BuildTimeline());
        ResetSize();
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi._Ready))]
internal static class TurnTimelineCombatUiPatch
{
    private static void Postfix(NCombatUi __instance)
    {
        if (!SpireGpsSettings.TurnTimelineEnabled)
            return;

        if (__instance.GetNodeOrNull<TurnTimelinePanel>("TurnTimelinePanel") is not null)
            return;

        __instance.AddChild(new TurnTimelinePanel
        {
            Name = "TurnTimelinePanel"
        });
    }
}
