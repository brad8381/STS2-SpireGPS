using System.Text.Json.Serialization;

namespace BantersTweaks.Integration;

/// <summary>
/// Stable, data-only manifest for Banter's Tweak's integrations.
/// Keep this contract free of Slay the Spire 2 model types so third-party
/// mods and external tools can consume the same schema.
/// </summary>
public sealed class IntegrationManifest
{
    public string Schema { get; set; } = BantersIntegration.SchemaId;
    public string ModId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public List<MechanicDefinition> Mechanics { get; set; } = new();
    public Dictionary<string, List<string>> CardTags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> RelicTags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<RelationshipDefinition> Relationships { get; set; } = new();
}

public sealed class MechanicDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ProducerModId { get; set; } = string.Empty;
}

public sealed class RelationshipDefinition
{
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ProducerModId { get; set; } = string.Empty;
}

public sealed class IntegrationStatus
{
    public string ProviderId { get; set; } = string.Empty;
    public string StatusId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string IconKey { get; set; } = string.Empty;
}
