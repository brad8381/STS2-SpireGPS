using SpireGPS.Telemetry;

namespace BantersTweaks.Integration;

/// <summary>
/// Public soft-integration surface for Banter's Tweak's.
///
/// Static metadata can also be supplied through banters.integration.v1 JSON.
/// Runtime integrations may reference this class directly or discover it by
/// reflection. This API intentionally exposes information/events only; it
/// does not provide gameplay actions such as travel, trading or card play.
/// </summary>
public static class BantersIntegration
{
    public const int ApiVersion = 1;
    public const string SchemaId = "banters.integration.v1";

    private static readonly Dictionary<string, MechanicDefinition> Mechanics =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, HashSet<string>> CardTags =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, HashSet<string>> RelicTags =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, RelationshipDefinition> Relationships =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, Func<IntegrationStatus?>> StatusProviders =
        new(StringComparer.OrdinalIgnoreCase);

    public static event Action? RegistryChanged;

    public static IReadOnlyCollection<MechanicDefinition> RegisteredMechanics
        => Mechanics.Values.ToArray();

    public static IReadOnlyCollection<RelationshipDefinition> RegisteredRelationships
        => Relationships.Values.ToArray();

    internal static void Initialize()
        => ExternalIntegrationLoader.LoadAll();

    public static bool RegisterManifest(IntegrationManifest manifest)
    {
        if (manifest is null ||
            !string.Equals(manifest.Schema, SchemaId, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(manifest.ModId))
        {
            return false;
        }

        foreach (var mechanic in manifest.Mechanics ?? new List<MechanicDefinition>())
        {
            if (string.IsNullOrWhiteSpace(mechanic.Id))
                continue;

            mechanic.ProducerModId = string.IsNullOrWhiteSpace(mechanic.ProducerModId)
                ? manifest.ModId
                : mechanic.ProducerModId;

            RegisterMechanic(mechanic, notify: false);
        }

        foreach (var pair in manifest.CardTags ?? new Dictionary<string, List<string>>())
            RegisterCardTags(pair.Key, pair.Value, notify: false);

        foreach (var pair in manifest.RelicTags ?? new Dictionary<string, List<string>>())
            RegisterRelicTags(pair.Key, pair.Value, notify: false);

        foreach (var relationship in manifest.Relationships ?? new List<RelationshipDefinition>())
        {
            relationship.ProducerModId = string.IsNullOrWhiteSpace(relationship.ProducerModId)
                ? manifest.ModId
                : relationship.ProducerModId;

            RegisterRelationship(relationship, notify: false);
        }

        RegistryChanged?.Invoke();
        return true;
    }

    public static void RegisterMechanic(MechanicDefinition mechanic)
        => RegisterMechanic(mechanic, notify: true);

    public static void RegisterCardTags(string cardId, params string[] tags)
        => RegisterCardTags(cardId, tags, notify: true);

    public static void RegisterCardTags(string cardId, IEnumerable<string> tags)
        => RegisterCardTags(cardId, tags, notify: true);

    public static void RegisterRelicTags(string relicId, params string[] tags)
        => RegisterRelicTags(relicId, tags, notify: true);

    public static void RegisterRelicTags(string relicId, IEnumerable<string> tags)
        => RegisterRelicTags(relicId, tags, notify: true);

    public static void RegisterRelationship(RelationshipDefinition relationship)
        => RegisterRelationship(relationship, notify: true);

    public static IReadOnlyCollection<string> GetCardTags(string cardId)
        => TryGetTags(CardTags, cardId);

    public static IReadOnlyCollection<string> GetRelicTags(string relicId)
        => TryGetTags(RelicTags, relicId);

    public static IReadOnlyCollection<RelationshipDefinition> GetRelationships(string sourceId)
        => Relationships.Values
            .Where(r => string.Equals(r.SourceId, sourceId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    public static void RegisterStatusProvider(
        string providerId,
        Func<IntegrationStatus?> provider)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            throw new ArgumentException("Provider ID is required.", nameof(providerId));

        StatusProviders[providerId] = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public static bool UnregisterStatusProvider(string providerId)
        => StatusProviders.Remove(providerId);

    public static IReadOnlyCollection<IntegrationStatus> GetStatuses()
    {
        var result = new List<IntegrationStatus>();

        foreach (var pair in StatusProviders.ToArray())
        {
            try
            {
                var status = pair.Value();
                if (status is null)
                    continue;

                if (string.IsNullOrWhiteSpace(status.ProviderId))
                    status.ProviderId = pair.Key;

                result.Add(status);
            }
            catch (Exception ex)
            {
                SpireGPS.MainFile.Logger.Warn(
                    $"Integration status provider '{pair.Key}' failed: {ex.Message}");
            }
        }

        return result;
    }

    public static void PublishRunEvent(
        string producerModId,
        string eventType,
        IReadOnlyDictionary<string, string>? data = null)
    {
        if (string.IsNullOrWhiteSpace(producerModId))
            throw new ArgumentException("Producer mod ID is required.", nameof(producerModId));
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type is required.", nameof(eventType));

        RunTelemetryService.Publish(producerModId, eventType, data);
    }

    public static string GetExternalManifestDirectory()
        => ExternalIntegrationLoader.GetAbsoluteDirectory();

    private static void RegisterMechanic(MechanicDefinition mechanic, bool notify)
    {
        if (mechanic is null || string.IsNullOrWhiteSpace(mechanic.Id))
            return;

        Mechanics[mechanic.Id] = mechanic;
        if (notify)
            RegistryChanged?.Invoke();
    }

    private static void RegisterCardTags(
        string cardId,
        IEnumerable<string> tags,
        bool notify)
    {
        RegisterTags(CardTags, cardId, tags);
        if (notify)
            RegistryChanged?.Invoke();
    }

    private static void RegisterRelicTags(
        string relicId,
        IEnumerable<string> tags,
        bool notify)
    {
        RegisterTags(RelicTags, relicId, tags);
        if (notify)
            RegistryChanged?.Invoke();
    }

    private static void RegisterRelationship(
        RelationshipDefinition relationship,
        bool notify)
    {
        if (relationship is null ||
            string.IsNullOrWhiteSpace(relationship.SourceId) ||
            string.IsNullOrWhiteSpace(relationship.TargetId) ||
            string.IsNullOrWhiteSpace(relationship.Kind))
        {
            return;
        }

        string key =
            $"{relationship.ProducerModId}|{relationship.SourceId}|{relationship.TargetId}|{relationship.Kind}";

        Relationships[key] = relationship;
        if (notify)
            RegistryChanged?.Invoke();
    }

    private static void RegisterTags(
        Dictionary<string, HashSet<string>> registry,
        string modelId,
        IEnumerable<string> tags)
    {
        if (string.IsNullOrWhiteSpace(modelId) || tags is null)
            return;

        if (!registry.TryGetValue(modelId, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            registry[modelId] = set;
        }

        foreach (string tag in tags)
        {
            if (!string.IsNullOrWhiteSpace(tag))
                set.Add(tag.Trim());
        }
    }

    private static IReadOnlyCollection<string> TryGetTags(
        Dictionary<string, HashSet<string>> registry,
        string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId) ||
            !registry.TryGetValue(modelId, out var tags))
        {
            return Array.Empty<string>();
        }

        return tags.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
