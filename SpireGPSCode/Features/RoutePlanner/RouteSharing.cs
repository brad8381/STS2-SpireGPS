using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;
using SpireGPS.UI;

namespace SpireGPS.Features.RoutePlanner;

internal static class RouteSharingService
{
    private static INetGameService? _netService;
    private static bool _registered;

    internal static void InitializeForRun()
    {
        EnsureNetwork();
    }

    internal static bool ShareCurrent()
    {
        EnsureNetwork();

        var route = RoutePlannerService.SelectedRoute ?? RoutePlannerService.PreferredRoute;
        if (route is null)
        {
            SpireGpsToast.Show("Route Planner: select a route before sharing.");
            return false;
        }

        if (_netService is null ||
            _netService.Type is not (NetGameType.Host or NetGameType.Client))
        {
            SpireGpsToast.Show("Route Planner: route sharing is only available in multiplayer.");
            return false;
        }

        _netService.SendMessage(new RouteShareMessage
        {
            RouteIndex = route.Index
        });

        SpireGpsToast.Show($"Route Planner: shared Route {route.Index}.");
        return true;
    }

    private static void EnsureNetwork()
    {
        var service = RunManager.Instance?.NetService;
        if (service is null)
            return;

        if (ReferenceEquals(_netService, service) && _registered)
            return;

        if (_registered && _netService is not null)
        {
            try { _netService.UnregisterMessageHandler<RouteShareMessage>(HandleRouteShare); }
            catch { }
        }

        _netService = service;
        _netService.RegisterMessageHandler<RouteShareMessage>(HandleRouteShare);
        _registered = true;
    }

    private static void HandleRouteShare(RouteShareMessage message, ulong senderId)
    {
        if (_netService is null || senderId == _netService.NetId)
            return;

        var route = RoutePlannerService.FindRoute(message.RouteIndex);
        if (route is null)
        {
            MainFile.Logger.Warn(
                $"Route share ignored: Route {message.RouteIndex} is not available on this client.");
            return;
        }

        RoutePlannerService.SetSelectedRoute(route);
        RoutePanelController.Refresh();
        RouteHighlighter.Refresh();

        SpireGpsToast.Show($"Teammate shared Route {route.Index}; highlighted locally.");
    }
}

public sealed class RouteShareMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => true;
    public bool ShouldBuffer => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;

    public int RouteIndex;

    public void Serialize(PacketWriter writer)
        => writer.WriteInt(RouteIndex);

    public void Deserialize(PacketReader reader)
        => RouteIndex = reader.ReadInt();
}
