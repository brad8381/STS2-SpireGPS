using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Runs;
using SpireGPS.Config;

namespace SpireGPS.Telemetry;

internal static class RunTelemetryService
{
    private const string BaseDirectory = "user://banters_tweaks";
    private const string RunsDirectory = "user://banters_tweaks/runs";
    private const string CurrentRunPath = "user://banters_tweaks/current_run.json";
    private const int RetainedRuns = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static RunTelemetryDocument? _current;
    private static string? _historyPath;
    private static long _sequence;

    internal static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(ProjectSettings.GlobalizePath(BaseDirectory));
            Directory.CreateDirectory(ProjectSettings.GlobalizePath(RunsDirectory));
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
            return;
        }

        DateTimeOffset started = DateTimeOffset.UtcNow;
        string runId = Guid.NewGuid().ToString("N");

        _sequence = 0;
        _current = new RunTelemetryDocument
        {
            RunId = runId,
            StartedAtUtc = started,
            ProducerVersion = typeof(MainFile).Assembly.GetName().Version?.ToString() ?? "0.0.0"
        };

        string fileName = $"{started:yyyyMMdd-HHmmss}-{runId}.json";
        _historyPath = Path.Combine(
            ProjectSettings.GlobalizePath(RunsDirectory),
            fileName);

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
                : new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase)
        };

        _current.Events.Add(item);
        SaveCurrent();
    }

    internal static string GetAbsoluteBaseDirectory()
        => ProjectSettings.GlobalizePath(BaseDirectory);

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
            AtomicWrite(ProjectSettings.GlobalizePath(CurrentRunPath), json);
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
            string directory = ProjectSettings.GlobalizePath(RunsDirectory);
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
