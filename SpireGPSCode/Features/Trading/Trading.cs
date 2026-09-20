using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using SpireGPS.Config;
using SpireGPS.UI;

namespace SpireGPS.Features.Trading;

internal enum TradeLegKind
{
    None = 0,
    Card = 1,
    Relic = 2,
    Gold = 3
}

internal readonly record struct TradeItemChoice(
    int SourceIndex,
    string Label,
    string ExpectedId);

internal readonly record struct TradeLegSpec(
    TradeLegKind Kind,
    int SourceIndex,
    string ExpectedId,
    int GoldAmount,
    string Label);

internal sealed class ResolvedTradeLeg
{
    internal TradeLegKind Kind { get; init; }
    internal CardModel? Card { get; init; }
    internal RelicModel? Relic { get; init; }
    internal int GoldAmount { get; init; }
    internal string Label { get; init; } = string.Empty;
}

internal sealed class TradeRestSiteOption : RestSiteOption
{
    private readonly Player _owner;
    private bool _isEnabled;

    internal Player OwnerPlayer => _owner;

    public override string OptionId => "SPIREGPS_TRADE";
    public override bool IsEnabled => _isEnabled;

    public override IEnumerable<string> AssetPaths => new HealRestSiteOption(_owner).AssetPaths;

    internal TradeRestSiteOption(Player owner) : base(owner)
    {
        _owner = owner;
        _isEnabled = TradingService.CanChooseTrade(owner);
    }

    internal void SetEnabled(bool enabled)
    {
        _isEnabled = enabled;
    }

    public override async Task<bool> OnSelect()
    {
        if (!TradingService.CanChooseTrade(_owner))
            return false;

        decimal heal = Math.Ceiling(_owner.Creature.MaxHp * 0.15m);
        if (heal > 0)
            await CreatureCmd.Heal(_owner.Creature, heal);

        TradingService.MarkTradeReady(_owner.NetId);
        return true;
    }

    public override Task DoLocalPostSelectVfx(CancellationToken ct = default)
    {
        if (LocalContext.IsMe(_owner))
            TradingService.OpenLocalTradePanel();

        return Task.CompletedTask;
    }
}

internal static class TradingService
{
    private const string TradePanelName = "SpireGpsTradingPanel";

    private static INetGameService? _netService;
    private static bool _handlersRegistered;
    private static NRestSiteRoom? _room;

    private static readonly FieldInfo? TradeButtonUnclickableField =
        AccessTools.Field(typeof(NRestSiteButton), "_isUnclickable");

    private static readonly HashSet<ulong> ReadyPlayers = new();
    private static readonly HashSet<ulong> CompletedPlayers = new();

    private static bool _hostPolicyReceived;
    private static bool _hostTradingEnabled;
    private static bool _hostAllowCards = true;
    private static bool _hostAllowRelics = true;
    private static bool _hostAllowGold = true;
    private static bool _hostAllowGifting;

    internal static event Action? Changed;

    private static bool EffectiveEnabled =>
        _netService?.Type == NetGameType.Host
            ? SpireGpsSettings.TradingEnabled
            : _hostPolicyReceived && _hostTradingEnabled;

    private static bool EffectiveAllowCards =>
        _netService?.Type == NetGameType.Host
            ? SpireGpsSettings.TradingAllowCards
            : _hostPolicyReceived && _hostAllowCards;

    private static bool EffectiveAllowRelics =>
        _netService?.Type == NetGameType.Host
            ? SpireGpsSettings.TradingAllowRelics
            : _hostPolicyReceived && _hostAllowRelics;

    private static bool EffectiveAllowGold =>
        _netService?.Type == NetGameType.Host
            ? SpireGpsSettings.TradingAllowGold
            : _hostPolicyReceived && _hostAllowGold;

    internal static bool CardTradingEnabled => EffectiveAllowCards;
    internal static bool RelicTradingEnabled => EffectiveAllowRelics;
    internal static bool GoldTradingEnabled => EffectiveAllowGold;

    internal static bool AllowGifting =>
        _netService?.Type == NetGameType.Host
            ? SpireGpsSettings.TradingAllowGifting
            : _hostPolicyReceived && _hostAllowGifting;

    internal static bool IsMultiplayer =>
        MainFile.RunState is { Players.Count: > 1 };

    internal static bool IsAvailable =>
        IsMultiplayer &&
        EffectiveEnabled &&
        (EffectiveAllowCards || EffectiveAllowRelics || EffectiveAllowGold);

    internal static void InitializeForRun()
    {
        ReadyPlayers.Clear();
        CompletedPlayers.Clear();
        _room = null;

        EnsureNetwork();

        if (_netService?.Type == NetGameType.Host)
        {
            BroadcastPolicy();
        }
        else
        {
            _hostPolicyReceived = false;
            _hostTradingEnabled = false;
            _hostAllowCards = true;
            _hostAllowRelics = true;
            _hostAllowGold = true;
            _hostAllowGifting = false;
            RequestPolicy();
        }
    }

    internal static void BeginRestSite(NRestSiteRoom room)
    {
        _room = room;
        ReadyPlayers.Clear();
        CompletedPlayers.Clear();
        EnsureNetwork();

        if (_netService?.Type == NetGameType.Host)
            BroadcastPolicy();
        else
            RequestPolicy();

        Changed?.Invoke();
    }

    internal static void ApplySettingsChanged()
    {
        EnsureNetwork();

        if (_netService?.Type == NetGameType.Host)
            BroadcastPolicy();

        RefreshTradeOptionUi();
        Changed?.Invoke();
    }

    internal static void RefreshTradeOptionUi()
    {
        if (_room is null || !GodotObject.IsInstanceValid(_room))
            return;

        foreach (var option in _room.Options.OfType<TradeRestSiteOption>())
        {
            option.SetEnabled(IsAvailable && !CompletedPlayers.Contains(option.OwnerPlayer.NetId));

            var button = _room.GetButtonForOption(option);
            if (button is null)
                continue;

            button.Visible = IsAvailable;
            TradeButtonUnclickableField?.SetValue(button, !option.IsEnabled);

            if (option.IsEnabled)
                button.Enable();
            else
                button.Disable();
        }
    }

    internal static bool CanChooseTrade(Player player)
    {
        return IsAvailable &&
               !CompletedPlayers.Contains(player.NetId);
    }

    internal static void MarkTradeReady(ulong playerId)
    {
        ReadyPlayers.Add(playerId);
        Changed?.Invoke();
    }

    internal static bool IsReady(ulong playerId)
        => ReadyPlayers.Contains(playerId) && !CompletedPlayers.Contains(playerId);

    internal static void OpenLocalTradePanel()
    {
        if (_room is null || !GodotObject.IsInstanceValid(_room))
            return;

        var local = GetLocalPlayer();
        if (local is null || !IsReady(local.NetId))
            return;

        var existing = _room.GetNodeOrNull<TradingPanel>(TradePanelName);
        if (existing is not null)
        {
            existing.Visible = true;
            existing.RefreshAll();
            return;
        }

        _room.AddChild(new TradingPanel
        {
            Name = TradePanelName
        });
    }

    internal static IReadOnlyList<Player> GetTradeTargets()
    {
        if (MainFile.RunState is null)
            return Array.Empty<Player>();

        var local = GetLocalPlayer();
        if (local is null)
            return Array.Empty<Player>();

        return MainFile.RunState.Players
            .Where(player =>
                player.NetId != local.NetId &&
                IsReady(player.NetId))
            .ToArray();
    }

    internal static Player? GetLocalPlayer()
    {
        if (MainFile.RunState is null)
            return null;

        return LocalContext.GetMe(MainFile.RunState);
    }

    internal static IReadOnlyList<TradeItemChoice> GetChoices(Player player, TradeLegKind kind)
    {
        if (kind == TradeLegKind.Card)
        {
            if (!EffectiveAllowCards)
                return Array.Empty<TradeItemChoice>();

            return player.Deck.Cards
                .Select((card, index) => (card, index))
                .Where(pair => IsSafeCard(pair.card))
                .Select(pair => new TradeItemChoice(
                    pair.index,
                    FormatCard(pair.card),
                    pair.card.Id.ToString()))
                .ToArray();
        }

        if (kind == TradeLegKind.Relic)
        {
            if (!EffectiveAllowRelics)
                return Array.Empty<TradeItemChoice>();

            return player.Relics
                .Select((relic, index) => (relic, index))
                .Where(pair => IsSafeRelic(player, pair.relic))
                .Select(pair => new TradeItemChoice(
                    pair.index,
                    pair.relic.Title.GetFormattedText(),
                    pair.relic.Id.ToString()))
                .ToArray();
        }

        return Array.Empty<TradeItemChoice>();
    }

    internal static string GetPlayerName(Player player)
    {
        try
        {
            if (_netService is not null)
                return PlatformUtil.GetPlayerName(_netService.Platform, player.NetId);
        }
        catch { }

        return player.Character.Title.GetFormattedText();
    }

    internal static void Propose(
        Player target,
        TradeLegSpec localLeg,
        TradeLegSpec remoteLeg)
    {
        EnsureNetwork();

        var local = GetLocalPlayer();
        if (_netService is null ||
            local is null ||
            !IsAvailable ||
            !IsReady(local.NetId) ||
            !IsReady(target.NetId))
        {
            SpireGpsToast.Show("Trading: both players must choose Trade at this Rest Site first.");
            return;
        }

        if (!AllowGifting &&
            (localLeg.Kind == TradeLegKind.None || remoteLeg.Kind == TradeLegKind.None))
        {
            SpireGpsToast.Show("Trading: gifting is disabled by the host.");
            return;
        }

        if (localLeg.Kind == TradeLegKind.None && remoteLeg.Kind == TradeLegKind.None)
        {
            SpireGpsToast.Show("Trading: choose something to exchange.");
            return;
        }

        _netService.SendMessage(new TradeProposalMessage
        {
            TargetNetId = target.NetId,

            ProposerKind = (int)localLeg.Kind,
            ProposerIndex = localLeg.SourceIndex,
            ProposerExpectedId = localLeg.ExpectedId,
            ProposerGold = localLeg.GoldAmount,

            TargetKind = (int)remoteLeg.Kind,
            TargetIndex = remoteLeg.SourceIndex,
            TargetExpectedId = remoteLeg.ExpectedId,
            TargetGold = remoteLeg.GoldAmount
        });

        SpireGpsToast.Show(
            $"Trading: proposal sent to {GetPlayerName(target)}.");
    }

    private static void EnsureNetwork()
    {
        var service = RunManager.Instance?.NetService;
        if (service is null)
            return;

        if (ReferenceEquals(_netService, service) && _handlersRegistered)
            return;

        if (_handlersRegistered && _netService is not null)
        {
            try
            {
                _netService.UnregisterMessageHandler<TradeProposalMessage>(HandleProposal);
                _netService.UnregisterMessageHandler<TradeExecuteMessage>(HandleExecute);
                _netService.UnregisterMessageHandler<TradePolicyMessage>(HandlePolicy);
                _netService.UnregisterMessageHandler<TradePolicyRequestMessage>(HandlePolicyRequest);
                _netService.UnregisterMessageHandler<TradeCancelMessage>(HandleCancel);
            }
            catch { }
        }

        _netService = service;
        _netService.RegisterMessageHandler<TradeProposalMessage>(HandleProposal);
        _netService.RegisterMessageHandler<TradeExecuteMessage>(HandleExecute);
        _netService.RegisterMessageHandler<TradePolicyMessage>(HandlePolicy);
        _netService.RegisterMessageHandler<TradePolicyRequestMessage>(HandlePolicyRequest);
        _netService.RegisterMessageHandler<TradeCancelMessage>(HandleCancel);
        _handlersRegistered = true;

        if (_netService.Type == NetGameType.Host)
        {
            _hostPolicyReceived = true;
            _hostTradingEnabled = SpireGpsSettings.TradingEnabled;
            _hostAllowCards = SpireGpsSettings.TradingAllowCards;
            _hostAllowRelics = SpireGpsSettings.TradingAllowRelics;
            _hostAllowGold = SpireGpsSettings.TradingAllowGold;
            _hostAllowGifting = SpireGpsSettings.TradingAllowGifting;
        }
    }

    private static void BroadcastPolicy()
    {
        if (_netService?.Type != NetGameType.Host)
            return;

        _netService.SendMessage(BuildPolicyMessage());
    }

    private static TradePolicyMessage BuildPolicyMessage()
    {
        _hostPolicyReceived = true;
        _hostTradingEnabled = SpireGpsSettings.TradingEnabled;
        _hostAllowCards = SpireGpsSettings.TradingAllowCards;
        _hostAllowRelics = SpireGpsSettings.TradingAllowRelics;
        _hostAllowGold = SpireGpsSettings.TradingAllowGold;
        _hostAllowGifting = SpireGpsSettings.TradingAllowGifting;

        return new TradePolicyMessage
        {
            Enabled = _hostTradingEnabled,
            AllowCards = _hostAllowCards,
            AllowRelics = _hostAllowRelics,
            AllowGold = _hostAllowGold,
            AllowGifting = _hostAllowGifting
        };
    }

    private static void RequestPolicy()
    {
        if (_netService?.Type != NetGameType.Client)
            return;

        _netService.SendMessage(new TradePolicyRequestMessage());
    }

    private static void HandlePolicyRequest(TradePolicyRequestMessage message, ulong senderId)
    {
        if (_netService?.Type != NetGameType.Host || senderId == _netService.NetId)
            return;

        _netService.SendMessage(BuildPolicyMessage(), senderId);
    }

    private static void HandlePolicy(TradePolicyMessage message, ulong senderId)
    {
        if (_netService is null || _netService.Type != NetGameType.Client)
            return;

        if (_netService is not NetClientGameService client || senderId != client.HostNetId)
            return;

        _hostPolicyReceived = true;
        _hostTradingEnabled = message.Enabled;
        _hostAllowCards = message.AllowCards;
        _hostAllowRelics = message.AllowRelics;
        _hostAllowGold = message.AllowGold;
        _hostAllowGifting = message.AllowGifting;

        RefreshTradeOptionUi();
        Changed?.Invoke();
    }

    internal static void ForfeitLocalTrade()
    {
        EnsureNetwork();

        var local = GetLocalPlayer();
        if (_netService is null || local is null || !ReadyPlayers.Contains(local.NetId))
            return;

        ReadyPlayers.Remove(local.NetId);
        CompletedPlayers.Add(local.NetId);

        _netService.SendMessage(new TradeCancelMessage
        {
            PlayerId = local.NetId
        });

        _room?.GetNodeOrNull<TradingPanel>(TradePanelName)?.QueueFree();
        RefreshTradeOptionUi();
        Changed?.Invoke();
    }

    private static void HandleCancel(TradeCancelMessage message, ulong senderId)
    {
        if (message.PlayerId != senderId)
            return;

        ReadyPlayers.Remove(message.PlayerId);
        CompletedPlayers.Add(message.PlayerId);
        RefreshTradeOptionUi();
        Changed?.Invoke();
    }

    private static void HandleProposal(TradeProposalMessage message, ulong senderId)
    {
        if (!IsAvailable ||
            _netService is null ||
            message.TargetNetId != _netService.NetId ||
            senderId == _netService.NetId ||
            !IsReady(senderId) ||
            !IsReady(_netService.NetId))
        {
            return;
        }

        var proposer = FindPlayer(senderId);
        var target = FindPlayer(_netService.NetId);
        if (proposer is null || target is null)
            return;

        if (!TryResolveLeg(
                proposer,
                (TradeLegKind)message.ProposerKind,
                message.ProposerIndex,
                message.ProposerExpectedId,
                message.ProposerGold,
                out var proposerLeg) ||
            !TryResolveLeg(
                target,
                (TradeLegKind)message.TargetKind,
                message.TargetIndex,
                message.TargetExpectedId,
                message.TargetGold,
                out var targetLeg))
        {
            SpireGpsToast.Show("Trading: proposal expired because an item changed.");
            return;
        }

        if (!AllowGifting &&
            (proposerLeg.Kind == TradeLegKind.None || targetLeg.Kind == TradeLegKind.None))
        {
            return;
        }

        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        var dialog = new ConfirmationDialog
        {
            Title = "Trading",
            DialogText =
                $"{GetPlayerName(proposer)} proposes:\n\n" +
                $"You give: {targetLeg.Label}\n" +
                $"You receive: {proposerLeg.Label}",
            OkButtonText = "Accept",
            CancelButtonText = "Decline",
            Exclusive = true
        };

        dialog.Confirmed += () =>
        {
            var execute = new TradeExecuteMessage
            {
                PlayerA = proposer.NetId,
                PlayerB = target.NetId,

                PlayerAKind = message.ProposerKind,
                PlayerAIndex = message.ProposerIndex,
                PlayerAExpectedId = message.ProposerExpectedId,
                PlayerAGold = message.ProposerGold,

                PlayerBKind = message.TargetKind,
                PlayerBIndex = message.TargetIndex,
                PlayerBExpectedId = message.TargetExpectedId,
                PlayerBGold = message.TargetGold
            };

            if (ApplyTrade(execute))
            {
                _netService.SendMessage(execute);
                SpireGpsToast.Show($"Trading: trade with {GetPlayerName(proposer)} completed.");
            }
            else
            {
                SpireGpsToast.Show("Trading: trade could not be completed because the items changed.");
            }

            dialog.QueueFree();
        };

        dialog.Canceled += () => dialog.QueueFree();
        tree.Root.AddChild(dialog);
        dialog.PopupCentered(new Vector2I(560, 250));
    }

    private static void HandleExecute(TradeExecuteMessage message, ulong senderId)
    {
        if (_netService is null ||
            senderId == _netService.NetId ||
            senderId != message.PlayerB)
        {
            return;
        }

        if (ApplyTrade(message))
            SpireGpsToast.Show("Trading: synchronized co-op trade completed.");
    }

    private static bool ApplyTrade(TradeExecuteMessage message)
    {
        var playerA = FindPlayer(message.PlayerA);
        var playerB = FindPlayer(message.PlayerB);

        if (playerA is null ||
            playerB is null ||
            playerA == playerB ||
            !IsReady(playerA.NetId) ||
            !IsReady(playerB.NetId))
        {
            return false;
        }

        try
        {
            if (!TryResolveLeg(
                    playerA,
                    (TradeLegKind)message.PlayerAKind,
                    message.PlayerAIndex,
                    message.PlayerAExpectedId,
                    message.PlayerAGold,
                    out var aLeg) ||
                !TryResolveLeg(
                    playerB,
                    (TradeLegKind)message.PlayerBKind,
                    message.PlayerBIndex,
                    message.PlayerBExpectedId,
                    message.PlayerBGold,
                    out var bLeg))
            {
                return false;
            }

            if (!AllowGifting &&
                (aLeg.Kind == TradeLegKind.None || bLeg.Kind == TradeLegKind.None))
            {
                return false;
            }

            if (aLeg.Kind == TradeLegKind.None && bLeg.Kind == TradeLegKind.None)
                return false;

            CardModel? aCard = aLeg.Card;
            CardModel? bCard = bLeg.Card;

            var aRelicSave = aLeg.Relic?.ToSerializable();
            var bRelicSave = bLeg.Relic?.ToSerializable();

            int aCardIndex = aCard is null ? -1 : playerA.Deck.Cards.ToList().IndexOf(aCard);
            int bCardIndex = bCard is null ? -1 : playerB.Deck.Cards.ToList().IndexOf(bCard);
            int aRelicIndex = aLeg.Relic is null ? -1 : playerA.Relics.ToList().IndexOf(aLeg.Relic);
            int bRelicIndex = bLeg.Relic is null ? -1 : playerB.Relics.ToList().IndexOf(bLeg.Relic);

            if (aCard is not null)
                playerA.Deck.RemoveInternal(aCard);

            if (bCard is not null)
                playerB.Deck.RemoveInternal(bCard);

            if (aLeg.Relic is not null)
                playerA.RemoveRelicInternal(aLeg.Relic);

            if (bLeg.Relic is not null)
                playerB.RemoveRelicInternal(bLeg.Relic);

            if (aCard is not null)
            {
                aCard.Owner = null!;
                aCard.Owner = playerB;
                playerB.Deck.AddInternal(
                    aCard,
                    bCardIndex >= 0
                        ? Math.Min(bCardIndex, playerB.Deck.Cards.Count)
                        : -1);
            }

            if (bCard is not null)
            {
                bCard.Owner = null!;
                bCard.Owner = playerA;
                playerA.Deck.AddInternal(
                    bCard,
                    aCardIndex >= 0
                        ? Math.Min(aCardIndex, playerA.Deck.Cards.Count)
                        : -1);
            }

            RelicModel? relicFromAToB = null;
            RelicModel? relicFromBToA = null;

            if (aRelicSave is not null)
            {
                relicFromAToB = RelicModel.FromSerializable(aRelicSave);
                playerB.AddRelicInternal(
                    relicFromAToB,
                    bRelicIndex >= 0
                        ? Math.Min(bRelicIndex, playerB.Relics.Count)
                        : -1);
            }

            if (bRelicSave is not null)
            {
                relicFromBToA = RelicModel.FromSerializable(bRelicSave);
                playerA.AddRelicInternal(
                    relicFromBToA,
                    aRelicIndex >= 0
                        ? Math.Min(aRelicIndex, playerA.Relics.Count)
                        : -1);
            }

            if (aLeg.Relic is not null ||
                bLeg.Relic is not null ||
                relicFromAToB is not null ||
                relicFromBToA is not null)
            {
                TaskHelper.RunSafely(RunRelicTransferHooks(
                    aLeg.Relic,
                    bLeg.Relic,
                    relicFromAToB,
                    relicFromBToA));
            }

            if (aLeg.GoldAmount > 0 || bLeg.GoldAmount > 0)
            {
                playerA.Gold = playerA.Gold - aLeg.GoldAmount + bLeg.GoldAmount;
                playerB.Gold = playerB.Gold - bLeg.GoldAmount + aLeg.GoldAmount;
            }

            CompletedPlayers.Add(playerA.NetId);
            CompletedPlayers.Add(playerB.NetId);
            ReadyPlayers.Remove(playerA.NetId);
            ReadyPlayers.Remove(playerB.NetId);

            MainFile.Logger.Info(
                $"Trading completed: {playerA.NetId} gave {aLeg.Label}; " +
                $"{playerB.NetId} gave {bLeg.Label}.");

            Changed?.Invoke();

            if (_room is not null && GodotObject.IsInstanceValid(_room))
            {
                var local = GetLocalPlayer();
                if (local is not null && CompletedPlayers.Contains(local.NetId))
                    _room.GetNodeOrNull<TradingPanel>(TradePanelName)?.QueueFree();
            }

            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Trading failed open while applying trade: {ex}");
            return false;
        }
    }

    private static async Task RunRelicTransferHooks(
        RelicModel? removedFromA,
        RelicModel? removedFromB,
        RelicModel? addedToB,
        RelicModel? addedToA)
    {
        try
        {
            if (removedFromA is not null)
                await removedFromA.AfterRemoved();

            if (removedFromB is not null)
                await removedFromB.AfterRemoved();

            if (addedToB is not null)
                await addedToB.AfterObtained();

            if (addedToA is not null)
                await addedToA.AfterObtained();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Trading relic transfer hook failed: {ex}");
        }
    }

    private static bool TryResolveLeg(
        Player owner,
        TradeLegKind kind,
        int sourceIndex,
        string expectedId,
        int goldAmount,
        out ResolvedTradeLeg leg)
    {
        leg = new ResolvedTradeLeg
        {
            Kind = kind,
            Label = "Nothing"
        };

        switch (kind)
        {
            case TradeLegKind.None:
                return AllowGifting;

            case TradeLegKind.Card:
            {
                if (!EffectiveAllowCards ||
                    sourceIndex < 0 ||
                    sourceIndex >= owner.Deck.Cards.Count)
                {
                    return false;
                }

                var card = owner.Deck.Cards[sourceIndex];
                if (card.Id.ToString() != expectedId || !IsSafeCard(card))
                    return false;

                leg = new ResolvedTradeLeg
                {
                    Kind = kind,
                    Card = card,
                    Label = FormatCard(card)
                };
                return true;
            }

            case TradeLegKind.Relic:
            {
                if (!EffectiveAllowRelics ||
                    sourceIndex < 0 ||
                    sourceIndex >= owner.Relics.Count)
                {
                    return false;
                }

                var relic = owner.Relics[sourceIndex];
                if (relic.Id.ToString() != expectedId || !IsSafeRelic(owner, relic))
                    return false;

                leg = new ResolvedTradeLeg
                {
                    Kind = kind,
                    Relic = relic,
                    Label = relic.Title.GetFormattedText()
                };
                return true;
            }

            case TradeLegKind.Gold:
                if (!EffectiveAllowGold || goldAmount <= 0 || goldAmount > owner.Gold)
                    return false;

                leg = new ResolvedTradeLeg
                {
                    Kind = kind,
                    GoldAmount = goldAmount,
                    Label = $"{goldAmount} Gold"
                };
                return true;

            default:
                return false;
        }
    }

    private static bool IsSafeCard(CardModel card)
        => card.IsRemovable &&
           card.Type != CardType.Quest &&
           card.Pile?.Type == PileType.Deck;

    private static bool IsSafeRelic(Player owner, RelicModel relic)
    {
        // Use the game's own trading safety rule first. Keep stackable relics
        // excluded for now because this module transfers one concrete relic
        // instance rather than a partial stack.
        return relic.IsTradable &&
               !relic.IsStackable &&
               !owner.Character.StartingRelics.Any(starting => starting.Id == relic.Id);
    }

    private static string FormatCard(CardModel card)
        => card.Title + (card.IsUpgraded ? "+" : string.Empty);

    private static Player? FindPlayer(ulong netId)
        => MainFile.RunState?.Players.FirstOrDefault(player => player.NetId == netId);
}

public sealed class TradePolicyMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => true;
    public bool ShouldBuffer => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;

    public bool Enabled;
    public bool AllowCards;
    public bool AllowRelics;
    public bool AllowGold;
    public bool AllowGifting;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteBool(Enabled);
        writer.WriteBool(AllowCards);
        writer.WriteBool(AllowRelics);
        writer.WriteBool(AllowGold);
        writer.WriteBool(AllowGifting);
    }

    public void Deserialize(PacketReader reader)
    {
        Enabled = reader.ReadBool();
        AllowCards = reader.ReadBool();
        AllowRelics = reader.ReadBool();
        AllowGold = reader.ReadBool();
        AllowGifting = reader.ReadBool();
    }
}

public sealed class TradePolicyRequestMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => false;
    public bool ShouldBuffer => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;

    public void Serialize(PacketWriter writer)
    {
    }

    public void Deserialize(PacketReader reader)
    {
    }
}

public sealed class TradeCancelMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => true;
    public bool ShouldBuffer => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;

    public ulong PlayerId;

    public void Serialize(PacketWriter writer)
        => writer.WriteULong(PlayerId);

    public void Deserialize(PacketReader reader)
        => PlayerId = reader.ReadULong();
}

public sealed class TradeProposalMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => true;
    public bool ShouldBuffer => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;

    public ulong TargetNetId;

    public int ProposerKind;
    public int ProposerIndex;
    public string ProposerExpectedId = string.Empty;
    public int ProposerGold;

    public int TargetKind;
    public int TargetIndex;
    public string TargetExpectedId = string.Empty;
    public int TargetGold;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(TargetNetId);

        writer.WriteUInt((uint)ProposerKind, 3);
        writer.WriteInt(ProposerIndex);
        writer.WriteString(ProposerExpectedId);
        writer.WriteInt(ProposerGold);

        writer.WriteUInt((uint)TargetKind, 3);
        writer.WriteInt(TargetIndex);
        writer.WriteString(TargetExpectedId);
        writer.WriteInt(TargetGold);
    }

    public void Deserialize(PacketReader reader)
    {
        TargetNetId = reader.ReadULong();

        ProposerKind = (int)reader.ReadUInt(3);
        ProposerIndex = reader.ReadInt();
        ProposerExpectedId = reader.ReadString();
        ProposerGold = reader.ReadInt();

        TargetKind = (int)reader.ReadUInt(3);
        TargetIndex = reader.ReadInt();
        TargetExpectedId = reader.ReadString();
        TargetGold = reader.ReadInt();
    }
}

public sealed class TradeExecuteMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => true;
    public bool ShouldBuffer => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;

    public ulong PlayerA;
    public ulong PlayerB;

    public int PlayerAKind;
    public int PlayerAIndex;
    public string PlayerAExpectedId = string.Empty;
    public int PlayerAGold;

    public int PlayerBKind;
    public int PlayerBIndex;
    public string PlayerBExpectedId = string.Empty;
    public int PlayerBGold;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(PlayerA);
        writer.WriteULong(PlayerB);

        writer.WriteUInt((uint)PlayerAKind, 3);
        writer.WriteInt(PlayerAIndex);
        writer.WriteString(PlayerAExpectedId);
        writer.WriteInt(PlayerAGold);

        writer.WriteUInt((uint)PlayerBKind, 3);
        writer.WriteInt(PlayerBIndex);
        writer.WriteString(PlayerBExpectedId);
        writer.WriteInt(PlayerBGold);
    }

    public void Deserialize(PacketReader reader)
    {
        PlayerA = reader.ReadULong();
        PlayerB = reader.ReadULong();

        PlayerAKind = (int)reader.ReadUInt(3);
        PlayerAIndex = reader.ReadInt();
        PlayerAExpectedId = reader.ReadString();
        PlayerAGold = reader.ReadInt();

        PlayerBKind = (int)reader.ReadUInt(3);
        PlayerBIndex = reader.ReadInt();
        PlayerBExpectedId = reader.ReadString();
        PlayerBGold = reader.ReadInt();
    }
}

internal partial class TradingPanel : PanelContainer
{
    private OptionButton _player = null!;

    private OptionButton _mineKind = null!;
    private OptionButton _mineItem = null!;
    private SpinBox _mineGold = null!;

    private OptionButton _theirKind = null!;
    private OptionButton _theirItem = null!;
    private SpinBox _theirGold = null!;

    private Label _status = null!;

    private IReadOnlyList<Player> _targets = Array.Empty<Player>();
    private IReadOnlyList<TradeItemChoice> _myChoices = Array.Empty<TradeItemChoice>();
    private IReadOnlyList<TradeItemChoice> _theirChoices = Array.Empty<TradeItemChoice>();

    public TradingPanel()
    {
        MouseFilter = Control.MouseFilterEnum.Stop;
        ZIndex = 160;

        AnchorLeft = 0.5f;
        AnchorRight = 0.5f;
        AnchorTop = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -390f;
        OffsetRight = 390f;
        OffsetTop = -285f;
        OffsetBottom = 285f;
    }

    public override void _Ready()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.035f, 0.045f, 0.98f),
            BorderColor = new Color(0.7f, 0.57f, 0.27f, 0.95f),
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
            ContentMarginTop = 14,
            ContentMarginBottom = 14
        };
        AddThemeStyleboxOverride("panel", style);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 9);
        AddChild(outer);

        var top = new HBoxContainer();
        outer.AddChild(top);

        var title = new Label
        {
            Text = "Trading",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        title.AddThemeFontSizeOverride("font_size", 22);
        top.AddChild(title);
        PanelDrag.Attach(title, this);

        var close = new Button
        {
            Text = "Close",
            FocusMode = Control.FocusModeEnum.None
        };
        close.Pressed += QueueFree;
        top.AddChild(close);

        var note = new Label
        {
            Text =
                "Both players must choose Trade at this Rest Site. " +
                "Each player can complete one exchange. Cards, safe relics and Gold are supported; Curses can be traded as cards.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        note.AddThemeFontSizeOverride("font_size", 13);
        outer.AddChild(note);

        outer.AddChild(new Label { Text = "Trade with" });
        _player = new OptionButton();
        _player.ItemSelected += _ => RefreshLegs();
        outer.AddChild(_player);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 14);
        outer.AddChild(columns);

        var mineBox = BuildLegEditor(
            "You give",
            out _mineKind,
            out _mineItem,
            out _mineGold);
        columns.AddChild(mineBox);

        var theirsBox = BuildLegEditor(
            "You receive",
            out _theirKind,
            out _theirItem,
            out _theirGold);
        columns.AddChild(theirsBox);

        _mineKind.ItemSelected += _ => RefreshMine();
        _theirKind.ItemSelected += _ => RefreshTheirs();

        _status = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _status.AddThemeFontSizeOverride("font_size", 13);
        outer.AddChild(_status);

        var propose = new Button
        {
            Text = "Propose Exchange",
            FocusMode = Control.FocusModeEnum.None
        };
        propose.Pressed += Propose;
        outer.AddChild(propose);

        TradingService.Changed += RefreshAll;
        RefreshAll();
    }

    public override void _ExitTree()
    {
        TradingService.Changed -= RefreshAll;
    }

    internal void RefreshAll()
    {
        _targets = TradingService.GetTradeTargets();

        int oldPlayer = _player.Selected;
        _player.Clear();

        foreach (var target in _targets)
            _player.AddItem(TradingService.GetPlayerName(target));

        if (_player.ItemCount > 0)
            _player.Select(Math.Clamp(oldPlayer, 0, _player.ItemCount - 1));

        PopulateKinds(_mineKind, allowNothing: TradingService.AllowGifting);
        PopulateKinds(_theirKind, allowNothing: TradingService.AllowGifting);

        RefreshLegs();
    }

    private static VBoxContainer BuildLegEditor(
        string heading,
        out OptionButton kind,
        out OptionButton item,
        out SpinBox gold)
    {
        var box = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(350f, 0f)
        };
        box.AddThemeConstantOverride("separation", 5);

        var label = new Label { Text = heading };
        label.AddThemeFontSizeOverride("font_size", 17);
        box.AddChild(label);

        kind = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        box.AddChild(kind);

        item = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        box.AddChild(item);

        gold = new SpinBox
        {
            MinValue = 1,
            MaxValue = 99999,
            Step = 1,
            Value = 50,
            AllowGreater = false,
            AllowLesser = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        box.AddChild(gold);

        return box;
    }

    private static void PopulateKinds(OptionButton button, bool allowNothing)
    {
        int oldId = button.ItemCount > 0 && button.Selected >= 0
            ? button.GetItemId(button.Selected)
            : (int)TradeLegKind.Card;

        button.Clear();

        if (TradingService.CardTradingEnabled)
            button.AddItem("Card", (int)TradeLegKind.Card);

        if (TradingService.RelicTradingEnabled)
            button.AddItem("Relic", (int)TradeLegKind.Relic);

        if (TradingService.GoldTradingEnabled)
            button.AddItem("Gold", (int)TradeLegKind.Gold);

        if (allowNothing)
            button.AddItem("Nothing (Gift)", (int)TradeLegKind.None);

        for (int i = 0; i < button.ItemCount; i++)
        {
            if (button.GetItemId(i) == oldId)
            {
                button.Select(i);
                return;
            }
        }

        if (button.ItemCount > 0)
            button.Select(0);
    }

    private void RefreshLegs()
    {
        RefreshMine();
        RefreshTheirs();

        if (_targets.Count == 0)
        {
            _status.Text =
                "Waiting for another player to choose Trade at this Rest Site.";
            return;
        }

        _status.Text = TradingService.AllowGifting
            ? "Host allows gifting. The other player must still accept."
            : "1-for-1 exchange. Gifting is disabled by the host.";
    }

    private void RefreshMine()
    {
        var local = TradingService.GetLocalPlayer();
        RefreshLeg(local, _mineKind, _mineItem, _mineGold, ref _myChoices);
    }

    private void RefreshTheirs()
    {
        var target = GetSelectedTarget();
        RefreshLeg(target, _theirKind, _theirItem, _theirGold, ref _theirChoices);
    }

    private static void RefreshLeg(
        Player? owner,
        OptionButton kindButton,
        OptionButton itemButton,
        SpinBox goldBox,
        ref IReadOnlyList<TradeItemChoice> choices)
    {
        itemButton.Clear();
        choices = Array.Empty<TradeItemChoice>();

        if (owner is null || kindButton.ItemCount == 0 || kindButton.Selected < 0)
        {
            itemButton.Visible = false;
            goldBox.Visible = false;
            return;
        }

        var kind = (TradeLegKind)kindButton.GetItemId(kindButton.Selected);

        itemButton.Visible = kind is TradeLegKind.Card or TradeLegKind.Relic;
        goldBox.Visible = kind == TradeLegKind.Gold;

        if (kind is TradeLegKind.Card or TradeLegKind.Relic)
        {
            choices = TradingService.GetChoices(owner, kind);
            foreach (var choice in choices)
                itemButton.AddItem(choice.Label);
        }
        else if (kind == TradeLegKind.Gold)
        {
            goldBox.MaxValue = Math.Max(1, owner.Gold);
            goldBox.Value = Math.Clamp((int)goldBox.Value, 1, Math.Max(1, owner.Gold));
        }
    }

    private Player? GetSelectedTarget()
    {
        int index = _player.Selected;
        return index >= 0 && index < _targets.Count
            ? _targets[index]
            : null;
    }

    private void Propose()
    {
        var local = TradingService.GetLocalPlayer();
        var target = GetSelectedTarget();

        if (local is null || target is null)
        {
            _status.Text = "Another Trade-ready player is required.";
            return;
        }

        if (!TryBuildLeg(local, _mineKind, _mineItem, _mineGold, _myChoices, out var mine) ||
            !TryBuildLeg(target, _theirKind, _theirItem, _theirGold, _theirChoices, out var theirs))
        {
            _status.Text = "Select valid items or Gold amounts on both sides.";
            return;
        }

        TradingService.Propose(target, mine, theirs);
        _status.Text = "Proposal sent.";
    }

    private static bool TryBuildLeg(
        Player owner,
        OptionButton kindButton,
        OptionButton itemButton,
        SpinBox goldBox,
        IReadOnlyList<TradeItemChoice> choices,
        out TradeLegSpec leg)
    {
        leg = default;

        if (kindButton.ItemCount == 0 || kindButton.Selected < 0)
            return false;

        var kind = (TradeLegKind)kindButton.GetItemId(kindButton.Selected);

        if (kind == TradeLegKind.None)
        {
            leg = new TradeLegSpec(kind, -1, string.Empty, 0, "Nothing");
            return TradingService.AllowGifting;
        }

        if (kind == TradeLegKind.Gold)
        {
            int amount = (int)goldBox.Value;
            if (amount <= 0 || amount > owner.Gold)
                return false;

            leg = new TradeLegSpec(
                kind,
                -1,
                string.Empty,
                amount,
                $"{amount} Gold");
            return true;
        }

        int index = itemButton.Selected;
        if (index < 0 || index >= choices.Count)
            return false;

        var choice = choices[index];
        leg = new TradeLegSpec(
            kind,
            choice.SourceIndex,
            choice.ExpectedId,
            0,
            choice.Label);
        return true;
    }
}

[HarmonyPatch(typeof(RestSiteOption), nameof(RestSiteOption.Generate))]
internal static class TradingRestSiteOptionsPatch
{
    private static void Postfix(Player player, ref List<RestSiteOption> __result)
    {
        if (player.RunState.Players.Count <= 1)
            return;

        __result.Add(new TradeRestSiteOption(player));
    }
}

[HarmonyPatch(typeof(NRestSiteRoom), nameof(NRestSiteRoom._Ready))]
internal static class TradingRestSiteRoomPatch
{
    private static void Postfix(NRestSiteRoom __instance)
    {
        try
        {
            TradingService.BeginRestSite(__instance);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Trading Rest Site setup failed open: {ex}");
        }
    }
}



[HarmonyPatch(typeof(RestSiteOption), "get_Icon")]
internal static class TradingRestSiteIconPatch
{
    private static bool Prefix(RestSiteOption __instance, ref Texture2D __result)
    {
        if (__instance is not TradeRestSiteOption trade)
            return true;

        // Reuse the vanilla Rest icon so the module stays DLL-only while still
        // giving remote-player thought bubbles and other generic Rest Site UI
        // a valid texture.
        __result = new HealRestSiteOption(trade.OwnerPlayer).Icon;
        return false;
    }
}

[HarmonyPatch(typeof(NRestSiteButton), "Reload")]
internal static class TradingRestSiteReloadPatch
{
    private static readonly FieldInfo? IconField =
        AccessTools.Field(typeof(NRestSiteButton), "_icon");

    private static readonly FieldInfo? LabelField =
        AccessTools.Field(typeof(NRestSiteButton), "_label");

    private static bool Prefix(NRestSiteButton __instance)
    {
        if (__instance.Option is not TradeRestSiteOption trade ||
            !__instance.IsNodeReady())
        {
            return true;
        }

        __instance.Visible = TradingService.IsAvailable;

        if (IconField?.GetValue(__instance) is TextureRect icon)
            icon.Texture = new HealRestSiteOption(trade.OwnerPlayer).Icon;

        if (LabelField?.GetValue(__instance) is MegaLabel label)
            label.SetTextAutoSize("TRADE");

        return false;
    }
}

[HarmonyPatch(typeof(NRestSiteButton), nameof(NRestSiteButton.RefreshTextState))]
internal static class TradingRestSiteDescriptionPatch
{
    private static readonly MethodInfo? IsFocusedGetter =
        AccessTools.PropertyGetter(
            typeof(MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl),
            "IsFocused");

    private static readonly FieldInfo? ExecutingField =
        AccessTools.Field(typeof(NRestSiteButton), "_executingOption");

    private static bool Prefix(NRestSiteButton __instance)
    {
        if (__instance.Option is not TradeRestSiteOption)
            return true;

        bool focused = IsFocusedGetter?.Invoke(__instance, null) as bool? ?? false;
        bool executing = ExecutingField?.GetValue(__instance) as bool? ?? false;

        if (focused || executing)
        {
            NRestSiteRoom.Instance?.SetText(
                "Heal 15% Max HP and open trading. " +
                "Both players must choose Trade. You do not Rest or Smith.");
        }
        else
        {
            NRestSiteRoom.Instance?.FadeOutOptionDescription();
        }

        return false;
    }
}


[HarmonyPatch(typeof(NRestSiteRoom), "OnProceedButtonReleased")]
internal static class TradingProceedPatch
{
    private static void Prefix()
    {
        var local = TradingService.GetLocalPlayer();
        if (local is not null && TradingService.IsReady(local.NetId))
            TradingService.ForfeitLocalTrade();
    }
}
