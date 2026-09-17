using Godot;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Runs;
using SpireGPS.Config;
using SpireGPS.Features.RoutePlanner;

namespace SpireGPS;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "SpireGPS";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static RunState? RunState { get; private set; }
    public static IReadOnlyList<RouteInfo> Routes => RoutePlannerService.Routes;

    public static void Initialize()
    {
        Logger.Info($"Loading SpireGPS {typeof(MainFile).Assembly.GetName().Version}");
        ModConfigBridge.DeferredRegister();

        var manager = RunManager.Instance;
        manager.RunStarted += OnRunStarted;
        manager.ActEntered += OnActEntered;
        manager.RoomEntered += OnRoomEntered;
    }

    private static void OnRunStarted(RunState runState)
    {
        RunState = runState;
        RefreshRoutes();
    }

    private static void OnActEntered() => RefreshRoutes();
    private static void OnRoomEntered() => RefreshRoutes();

    internal static void RefreshRoutes()
    {
        if (!SpireGpsSettings.RoutePlannerEnabled || RunState is null)
        {
            RoutePlannerService.Clear();
            return;
        }

        RoutePlannerService.Refresh(RunState);
        Logger.Info($"Route Planner: {Routes.Count} route(s) reachable from the current map position.");
    }
}
