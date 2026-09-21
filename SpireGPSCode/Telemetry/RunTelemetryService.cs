using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using SpireGPS.Config;
using SpireGPS.Features.PostRunSummary;

namespace SpireGPS.Telemetry;

internal static class RunTelemetryService
{
    private const string FolderName = "banters_tweaks";
    private const int RetainedRuns = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static RunTelemetryDocument? _current;
    private static string? _historyPath;
    private static long _sequence;
    private static bool _ended;

    private static string BaseDirectory => Path.Combine(OS.GetUserDataDir(), FolderName);
    private static string RunsDirectory => Path.Combine(BaseDirectory, "runs");
    private static string CurrentRunPath => Path.Combine(BaseDirectory, "current_run.json");

    internal static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(BaseDirectory);
            Directory.CreateDirectory(RunsDirectory);
            MainFile.Logger.Info($"Banter data directory: {BaseDirectory}");
            PruneHistory();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Run telemetry directory could not be initialized: {ex.Message}");
        }
    }

    internal static void StartRun(RunState runState)
    {
        if (!SpireGpsSettings.RunTelemetryEnabled)
        {
            _current = null;
            _historyPath = null;
            _sequence = 0;
            _ended = false;
            return;
        }

        DateTimeOffset started = DateTimeOffset.UtcNow;
        string runId = Guid.NewGuid().ToString("N");

        _sequence = 0;
        _ended = false;
        _current = new RunTelemetryDocument
        {
            RunId = runId,
            StartedAtUtc = started,
            ProducerVersion = typeof(MainFile).Assembly.GetName().Version?.ToString() ?? "0.0.0"
        };

        string fileName = $"{started:yyyyMMdd-HHmmss}-{runId}.json";
        _historyPath = Path.Combine(RunsDirectory, fileName);

        Publish(
            MainFile.ModId,
            "RunStarted",
            BuildRunStateData(runState));

        PruneHistory();
    }

    internal static void RecordActEntered(RunState? runState)
    {
        if (runState is null)
            return;

        Publish(MainFile.ModId, "ActEntered", BuildRunStateData(runState));
    }

    internal static void RecordRoomEntered(RunState? runState)
    {
        if (runState is null)
            return;

        Publish(MainFile.ModId, "RoomEntered", BuildRunStateData(runState));
    }

    internal static void Publish(
        string producerModId,
        string eventType,
        IReadOnlyDictionary<string, string>? data = null)
    {
        if (!SpireGpsSettings.RunTelemetryEnabled || _current is null)
            return;

        var item = new RunTelemetryEvent
        {
            Sequence = ++_sequence,
            TimestampUtc = DateTimeOffset.UtcNow,
            ProducerModId = producerModId,
            EventType = eventType,
            Data = data is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : data.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase)
        };

        _current.Events.Add(item);
        SaveCurrent();
    }

    internal static string GetAbsoluteBaseDirectory()
        => BaseDirectory;

    internal static void RecordRunEnded(bool isVictory, bool isAbandoned)
    {
        if (_ended || _current is null)
            return;

        _ended = true;
        Publish(
            MainFile.ModId,
            "RunEnded",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["victory"] = isVictory.ToString(),
                ["abandoned"] = isAbandoned.ToString()
            });
    }

    internal static void RecordRunClosed()
    {
        if (_ended || _current is null)
            return;

        _ended = true;
        Publish(
            MainFile.ModId,
            "RunClosed",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string> BuildRunStateData(RunState state)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["actIndex"] = state.CurrentActIndex.ToString(),
            ["floor"] = state.ActFloor.ToString(),
            ["ascension"] = state.AscensionLevel.ToString(),
            ["playerCount"] = state.Players.Count.ToString()
        };

        if (state.CurrentMapPoint is not null)
        {
            data["mapCoord"] = state.CurrentMapPoint.coord.ToString();
            data["roomType"] = state.CurrentMapPoint.PointType.ToString();
        }

        return data;
    }

    private static void SaveCurrent()
    {
        if (_current is null || string.IsNullOrWhiteSpace(_historyPath))
            return;

        try
        {
            string json = JsonSerializer.Serialize(_current, JsonOptions);
            AtomicWrite(CurrentRunPath, json);
            AtomicWrite(_historyPath, json);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Run telemetry could not be saved: {ex.Message}");
        }
    }

    private static void AtomicWrite(string path, string contents)
    {
        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, true);
    }

    private static void PruneHistory()
    {
        try
        {
            string directory = RunsDirectory;
            if (!Directory.Exists(directory))
                return;

            var files = new DirectoryInfo(directory)
                .EnumerateFiles("*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Skip(RetainedRuns)
                .ToArray();

            foreach (var file in files)
            {
                try { file.Delete(); }
                catch (Exception ex)
                {
                    MainFile.Logger.Warn(
                        $"Run telemetry could not prune '{file.Name}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Run telemetry history pruning failed: {ex.Message}");
        }
    }
}


[HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded))]
internal static class RunTelemetryEndedPatch
{
    private static void Postfix(
        RunManager __instance,
        bool isVictory,
        MegaCrit.Sts2.Core.Saves.SerializableRun __result)
    {
        RunTelemetryService.RecordRunEnded(isVictory, __instance.IsAbandoned);
        PostRunSummary.Show(__result, isVictory, __instance.IsAbandoned);
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
internal static class RunTelemetryCleanupPatch
{
    private static void Prefix()
        => RunTelemetryService.RecordRunClosed();
}
