namespace SpireGPS.Telemetry;

public sealed class RunTelemetryDocument
{
    public string Schema { get; set; } = "banters.run.v1";
    public string RunId { get; set; } = string.Empty;
    public string ProducerVersion { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; }
    public List<RunTelemetryEvent> Events { get; set; } = new();
}

public sealed class RunTelemetryEvent
{
    public long Sequence { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string ProducerModId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public Dictionary<string, string> Data { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
