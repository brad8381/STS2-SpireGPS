using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using SpireGPS.Config;
using SpireGPS.UI;

namespace SpireGPS.Features.Trading;

internal enum TradeItemKind
{
    Card = 0,
    Relic = 1
}

internal readonly record struct TradeItemChoice(int SourceIndex, string Label, string ExpectedId);

internal static class TradingService
{
    private const string TradeButtonName = "TradingButton";
    private const string TradePanelName = "TradingPanel";

    private static INetGameService? _netService;
    private static bool _handlersRegistered;
    private static NRestSiteRoom? _room;

    private static bool _hostPolicyReceived;
    private static bool _hostTradingEnabled;
    private static bool _hostAllowCards = true;
    private static bool _hostAllowRelics = true;

    internal static event Action? Changed;

    internal static bool IsAvailable =>
        MainFile.RunState is { Players.Count: > 1 } &&
        EffectiveEnabled;

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

    internal static void Attach(NRestSiteRoom room)
    {
        _room = room;
        EnsureNetwork();

        if (_netService?.Type == NetGameType.Host)
            BroadcastPolicy();

        RefreshRestSiteButton();
    }

    internal static void ApplySettingsChanged()
    {
        EnsureNetwork();

        if (_netService?.Type == NetGameType.Host)
        {
            BroadcastPolicy();
            RefreshRestSiteButton();
        }
    }

    private static void RefreshRestSiteButton()
    {
        if (_room is null || !GodotObject.IsInstanceValid(_room))
            return;

        var existing = _room.GetNodeOrNull<Button>(TradeButtonName);

        if (!IsAvailable)
        {
            existing?.QueueFree();
            _room.GetNodeOrNull<TradingPanel>(TradePanelName)?.QueueFree();
            return;
        }

        if (existing is not null)
            return;

        var button = new Button
        {
            Name = TradeButtonName,
            Text = "Trade",
            TooltipText = "Open 1-for-1 co-op trading. Trading is only available at Rest Sites.",
            FocusMode = Control.FocusModeEnum.None,
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = -178f,
            OffsetRight = -28f,
            OffsetTop = 82f,
            OffsetBottom = 126f,
            ZIndex = 120
        };

        button.Pressed += () => TogglePanel(_room);
        _room.AddChild(button);
    }

    internal static IReadOnlyList<Player> GetTradeTargets()
    {
        if (MainFile.RunState is null)
            return Array.Empty<Player>();

        ulong localId = _netService?.NetId ?? LocalContext.NetId ?? 0;
        return MainFile.RunState.Players
            .Where(player => player.NetId != localId)
            .ToArray();
    }

    internal static Player? GetLocalPlayer()
    {
        if (MainFile.RunState is null)
            return null;

        return LocalContext.GetMe(MainFile.RunState);
    }

    internal static IReadOnlyList<TradeItemChoice> GetChoices(Player player, TradeItemKind kind)
    {
        if (kind == TradeItemKind.Card)
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
        TradeItemKind kind,
        TradeItemChoice localItem,
        TradeItemChoice remoteItem)
    {
        EnsureNetwork();

        if (_netService is null || !IsAvailable)
            return;

        _netService.SendMessage(new TradeProposalMessage
        {
            TargetNetId = target.NetId,
            Kind = (int)kind,
            ProposerIndex = localItem.SourceIndex,
            TargetIndex = remoteItem.SourceIndex,
            ProposerExpectedId = localItem.ExpectedId,
            TargetExpectedId = remoteItem.ExpectedId
        });

        SpireGpsToast.Show(
            $"Trading: proposal sent to {GetPlayerName(target)}.");
    }

    private static void TogglePanel(NRestSiteRoom room)
    {
        var existing = room.GetNodeOrNull<TradingPanel>(TradePanelName);
        if (existing is not null)
        {
            existing.QueueFree();
            return;
        }

        room.AddChild(new TradingPanel
        {
            Name = TradePanelName
        });
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
            }
            catch { }
        }

        _netService = service;
        _netService.RegisterMessageHandler<TradeProposalMessage>(HandleProposal);
        _netService.RegisterMessageHandler<TradeExecuteMessage>(HandleExecute);
        _netService.RegisterMessageHandler<TradePolicyMessage>(HandlePolicy);
        _handlersRegistered = true;

        if (_netService.Type == NetGameType.Host)
        {
            _hostPolicyReceived = true;
            _hostTradingEnabled = SpireGpsSettings.TradingEnabled;
            _hostAllowCards = SpireGpsSettings.TradingAllowCards;
            _hostAllowRelics = SpireGpsSettings.TradingAllowRelics;
        }
    }

    private static void BroadcastPolicy()
    {
        if (_netService?.Type != NetGameType.Host)
            return;

        _hostPolicyReceived = true;
        _hostTradingEnabled = SpireGpsSettings.TradingEnabled;
        _hostAllowCards = SpireGpsSettings.TradingAllowCards;
        _hostAllowRelics = SpireGpsSettings.TradingAllowRelics;

        _netService.SendMessage(new TradePolicyMessage
        {
            Enabled = _hostTradingEnabled,
            AllowCards = _hostAllowCards,
            AllowRelics = _hostAllowRelics
        });
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

        RefreshRestSiteButton();
        Changed?.Invoke();
    }

    private static void HandleProposal(TradeProposalMessage message, ulong senderId)
    {
        if (!IsAvailable ||
            _netService is null ||
            message.TargetNetId != _netService.NetId ||
            senderId == _netService.NetId)
        {
            return;
        }

        var proposer = FindPlayer(senderId);
        var target = FindPlayer(_netService.NetId);
        if (proposer is null || target is null)
            return;

        var kind = (TradeItemKind)message.Kind;

        if (!TryResolveItem(proposer, kind, message.ProposerIndex, message.ProposerExpectedId, out string proposerItem) ||
            !TryResolveItem(target, kind, message.TargetIndex, message.TargetExpectedId, out string targetItem))
        {
            SpireGpsToast.Show("Trading: proposal expired because an item changed.");
            return;
        }

        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        var dialog = new ConfirmationDialog
        {
            Title = "Trading",
            DialogText =
                $"{GetPlayerName(proposer)} proposes a 1-for-1 trade:\n\n" +
                $"You give: {targetItem}\n" +
                $"You receive: {proposerItem}",
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
                Kind = message.Kind,
                PlayerAIndex = message.ProposerIndex,
                PlayerBIndex = message.TargetIndex,
                PlayerAExpectedId = message.ProposerExpectedId,
                PlayerBExpectedId = message.TargetExpectedId
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
        if (_netService is null || senderId == _netService.NetId)
            return;

        if (ApplyTrade(message))
            SpireGpsToast.Show("Trading: synchronized co-op trade completed.");
    }

    private static bool ApplyTrade(TradeExecuteMessage message)
    {
        var playerA = FindPlayer(message.PlayerA);
        var playerB = FindPlayer(message.PlayerB);
        if (playerA is null || playerB is null || playerA == playerB)
            return false;

        var kind = (TradeItemKind)message.Kind;

        try
        {
            bool applied = kind switch
            {
                TradeItemKind.Card => ApplyCardTrade(
                    playerA,
                    playerB,
                    message.PlayerAIndex,
                    message.PlayerBIndex,
                    message.PlayerAExpectedId,
                    message.PlayerBExpectedId),

                TradeItemKind.Relic => ApplyRelicTrade(
                    playerA,
                    playerB,
                    message.PlayerAIndex,
                    message.PlayerBIndex,
                    message.PlayerAExpectedId,
                    message.PlayerBExpectedId),

                _ => false
            };

            if (applied)
                Changed?.Invoke();

            return applied;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Trading failed open while applying trade: {ex}");
            return false;
        }
    }

    private static bool ApplyCardTrade(
        Player a,
        Player b,
        int aIndex,
        int bIndex,
        string aExpected,
        string bExpected)
    {
        if (aIndex < 0 || aIndex >= a.Deck.Cards.Count ||
            bIndex < 0 || bIndex >= b.Deck.Cards.Count)
        {
            return false;
        }

        var aCard = a.Deck.Cards[aIndex];
        var bCard = b.Deck.Cards[bIndex];

        if (aCard.Id.ToString() != aExpected ||
            bCard.Id.ToString() != bExpected ||
            !IsSafeCard(aCard) ||
            !IsSafeCard(bCard))
        {
            return false;
        }

        a.Deck.RemoveInternal(aCard);
        b.Deck.RemoveInternal(bCard);

        aCard.Owner = null!;
        bCard.Owner = null!;

        aCard.Owner = b;
        bCard.Owner = a;

        a.Deck.AddInternal(bCard, Math.Min(aIndex, a.Deck.Cards.Count));
        b.Deck.AddInternal(aCard, Math.Min(bIndex, b.Deck.Cards.Count));

        MainFile.Logger.Info(
            $"Trading: swapped cards {aExpected} ({a.NetId}) and {bExpected} ({b.NetId}).");
        return true;
    }

    private static bool ApplyRelicTrade(
        Player a,
        Player b,
        int aIndex,
        int bIndex,
        string aExpected,
        string bExpected)
    {
        if (aIndex < 0 || aIndex >= a.Relics.Count ||
            bIndex < 0 || bIndex >= b.Relics.Count)
        {
            return false;
        }

        var aRelic = a.Relics[aIndex];
        var bRelic = b.Relics[bIndex];

        if (aRelic.Id.ToString() != aExpected ||
            bRelic.Id.ToString() != bExpected ||
            !IsSafeRelic(a, aRelic) ||
            !IsSafeRelic(b, bRelic))
        {
            return false;
        }

        var aSave = aRelic.ToSerializable();
        var bSave = bRelic.ToSerializable();

        a.RemoveRelicInternal(aRelic);
        b.RemoveRelicInternal(bRelic);

        var aForB = RelicModel.FromSerializable(aSave);
        var bForA = RelicModel.FromSerializable(bSave);

        a.AddRelicInternal(bForA, Math.Min(aIndex, a.Relics.Count));
        b.AddRelicInternal(aForB, Math.Min(bIndex, b.Relics.Count));

        MainFile.Logger.Info(
            $"Trading: swapped relics {aExpected} ({a.NetId}) and {bExpected} ({b.NetId}).");
        return true;
    }

    private static bool IsSafeCard(CardModel card)
        => card.IsRemovable &&
           card.Type != CardType.Quest &&
           card.Pile?.Type == PileType.Deck;

    private static bool IsSafeRelic(Player owner, RelicModel relic)
    {
        if (relic.IsMelted ||
            relic.IsUsedUp ||
            relic.IsStackable ||
            relic.HasUponPickupEffect ||
            relic.SpawnsPets)
        {
            return false;
        }

        return !owner.Character.StartingRelics.Any(starting => starting.Id == relic.Id);
    }

    private static string FormatCard(CardModel card)
        => card.Title + (card.IsUpgraded ? "+" : string.Empty);

    private static Player? FindPlayer(ulong netId)
        => MainFile.RunState?.Players.FirstOrDefault(player => player.NetId == netId);

    private static bool TryResolveItem(
        Player player,
        TradeItemKind kind,
        int index,
        string expectedId,
        out string label)
    {
        label = string.Empty;

        if (kind == TradeItemKind.Card)
        {
            if (index < 0 || index >= player.Deck.Cards.Count)
                return false;

            var card = player.Deck.Cards[index];
            if (card.Id.ToString() != expectedId || !IsSafeCard(card))
                return false;

            label = FormatCard(card);
            return true;
        }

        if (index < 0 || index >= player.Relics.Count)
            return false;

        var relic = player.Relics[index];
        if (relic.Id.ToString() != expectedId || !IsSafeRelic(player, relic))
            return false;

        label = relic.Title.GetFormattedText();
        return true;
    }
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

    public void Serialize(PacketWriter writer)
    {
        writer.WriteBool(Enabled);
        writer.WriteBool(AllowCards);
        writer.WriteBool(AllowRelics);
    }

    public void Deserialize(PacketReader reader)
    {
        Enabled = reader.ReadBool();
        AllowCards = reader.ReadBool();
        AllowRelics = reader.ReadBool();
    }
}

public sealed class TradeProposalMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => true;
    public bool ShouldBuffer => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;

    public ulong TargetNetId;
    public int Kind;
    public int ProposerIndex;
    public int TargetIndex;
    public string ProposerExpectedId = string.Empty;
    public string TargetExpectedId = string.Empty;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(TargetNetId);
        writer.WriteUInt((uint)Kind, 2);
        writer.WriteInt(ProposerIndex);
        writer.WriteInt(TargetIndex);
        writer.WriteString(ProposerExpectedId);
        writer.WriteString(TargetExpectedId);
    }

    public void Deserialize(PacketReader reader)
    {
        TargetNetId = reader.ReadULong();
        Kind = (int)reader.ReadUInt(2);
        ProposerIndex = reader.ReadInt();
        TargetIndex = reader.ReadInt();
        ProposerExpectedId = reader.ReadString();
        TargetExpectedId = reader.ReadString();
    }
}

public sealed class TradeExecuteMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => true;
    public bool ShouldBuffer => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;

    public ulong PlayerA;
    public ulong PlayerB;
    public int Kind;
    public int PlayerAIndex;
    public int PlayerBIndex;
    public string PlayerAExpectedId = string.Empty;
    public string PlayerBExpectedId = string.Empty;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(PlayerA);
        writer.WriteULong(PlayerB);
        writer.WriteUInt((uint)Kind, 2);
        writer.WriteInt(PlayerAIndex);
        writer.WriteInt(PlayerBIndex);
        writer.WriteString(PlayerAExpectedId);
        writer.WriteString(PlayerBExpectedId);
    }

    public void Deserialize(PacketReader reader)
    {
        PlayerA = reader.ReadULong();
        PlayerB = reader.ReadULong();
        Kind = (int)reader.ReadUInt(2);
        PlayerAIndex = reader.ReadInt();
        PlayerBIndex = reader.ReadInt();
        PlayerAExpectedId = reader.ReadString();
        PlayerBExpectedId = reader.ReadString();
    }
}

internal partial class TradingPanel : PanelContainer
{
    private OptionButton _player = null!;
    private OptionButton _kind = null!;
    private OptionButton _mine = null!;
    private OptionButton _theirs = null!;
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
        OffsetLeft = -330f;
        OffsetRight = 330f;
        OffsetTop = -240f;
        OffsetBottom = 240f;
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
        outer.AddThemeConstantOverride("separation", 10);
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

        var close = new Button
        {
            Text = "Close",
            FocusMode = Control.FocusModeEnum.None
        };
        close.Pressed += QueueFree;
        top.AddChild(close);

        var note = new Label
        {
            Text = "Rest Site only - 1-for-1 trades - both players must accept.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        note.AddThemeFontSizeOverride("font_size", 13);
        outer.AddChild(note);

        outer.AddChild(new Label { Text = "Trade with" });
        _player = new OptionButton();
        _player.ItemSelected += _ => RefreshItems();
        outer.AddChild(_player);

        outer.AddChild(new Label { Text = "Item type" });
        _kind = new OptionButton();
        if (TradingService.GetLocalPlayer() is { } localForKinds)
        {
            if (TradingService.GetChoices(localForKinds, TradeItemKind.Card).Count > 0)
                _kind.AddItem("Cards", (int)TradeItemKind.Card);
            if (TradingService.GetChoices(localForKinds, TradeItemKind.Relic).Count > 0)
                _kind.AddItem("Relics", (int)TradeItemKind.Relic);
        }
        _kind.ItemSelected += _ => RefreshItems();
        outer.AddChild(_kind);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 12);
        outer.AddChild(columns);

        var mineBox = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        mineBox.AddChild(new Label { Text = "You give" });
        _mine = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        mineBox.AddChild(_mine);
        columns.AddChild(mineBox);

        var theirsBox = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        theirsBox.AddChild(new Label { Text = "You receive" });
        _theirs = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        theirsBox.AddChild(_theirs);
        columns.AddChild(theirsBox);

        _status = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _status.AddThemeFontSizeOverride("font_size", 13);
        outer.AddChild(_status);

        var propose = new Button
        {
            Text = "Propose 1-for-1 Trade",
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

    private void RefreshAll()
    {
        _targets = TradingService.GetTradeTargets();

        _player.Clear();
        foreach (var target in _targets)
            _player.AddItem(TradingService.GetPlayerName(target));

        RefreshItems();
    }

    private void RefreshItems()
    {
        _mine.Clear();
        _theirs.Clear();

        var local = TradingService.GetLocalPlayer();
        var target = GetSelectedTarget();

        if (local is null || target is null || _kind.ItemCount == 0)
        {
            _status.Text = "No valid multiplayer trade target.";
            return;
        }

        var kind = (TradeItemKind)_kind.GetItemId(_kind.Selected);
        _myChoices = TradingService.GetChoices(local, kind);
        _theirChoices = TradingService.GetChoices(target, kind);

        foreach (var item in _myChoices)
            _mine.AddItem(item.Label);

        foreach (var item in _theirChoices)
            _theirs.AddItem(item.Label);

        _status.Text =
            _myChoices.Count == 0 || _theirChoices.Count == 0
                ? "No safe tradable items are available for this combination."
                : "The other player receives an Accept / Decline confirmation.";
    }

    private Player? GetSelectedTarget()
    {
        int index = _player.Selected;
        return index >= 0 && index < _targets.Count ? _targets[index] : null;
    }

    private void Propose()
    {
        var target = GetSelectedTarget();
        if (target is null ||
            _kind.ItemCount == 0 ||
            _mine.Selected < 0 ||
            _theirs.Selected < 0 ||
            _mine.Selected >= _myChoices.Count ||
            _theirs.Selected >= _theirChoices.Count)
        {
            _status.Text = "Select one valid item from each player.";
            return;
        }

        var kind = (TradeItemKind)_kind.GetItemId(_kind.Selected);
        TradingService.Propose(
            target,
            kind,
            _myChoices[_mine.Selected],
            _theirChoices[_theirs.Selected]);

        _status.Text = "Proposal sent.";
    }
}

[HarmonyPatch(typeof(NRestSiteRoom), nameof(NRestSiteRoom._Ready))]
internal static class TradingRestSitePatch
{
    private static void Postfix(NRestSiteRoom __instance)
    {
        try
        {
            TradingService.Attach(__instance);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Trading setup failed open: {ex}");
        }
    }
}
