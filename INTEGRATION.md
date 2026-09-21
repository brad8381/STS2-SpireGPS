# Banter's Tweak's Integration API

Banter's Tweak's exposes a small, data-oriented integration surface for other mods and external tools.

The design goal is **soft integration**:

- another mod does not need Banter's Tweak's to function;
- Banter's Tweak's does not require the other mod;
- static metadata can be supplied as JSON;
- dynamic integrations can call the public API directly or via reflection;
- the API exposes metadata, statuses and run events, not gameplay execution.

## Versions

- API version: `1`
- Static JSON schema ID: `banters.integration.v1`
- Run telemetry schema ID: `banters.run.v1`

## Static JSON integrations

On startup Banter's Tweak's scans:

`user://banters_tweaks/integrations/*.integration.json`

The absolute directory is created automatically.

Example:

```json
{
  "schema": "banters.integration.v1",
  "modId": "ExampleMod",
  "displayName": "Example Mod",
  "version": "1.0.0",
  "mechanics": [
    {
      "id": "ExampleMod.Bleed",
      "name": "Bleed",
      "description": "Example custom mechanic."
    }
  ],
  "cardTags": {
    "EXAMPLE_MOD.BLOODY_STRIKE": [
      "Bleed",
      "Attack"
    ]
  },
  "relicTags": {
    "EXAMPLE_MOD.BLOOD_VIAL": [
      "Bleed"
    ]
  },
  "relationships": [
    {
      "sourceId": "EXAMPLE_MOD.BLOODY_STRIKE",
      "targetId": "ExampleMod.Bleed",
      "kind": "applies",
      "description": "Applies Bleed."
    }
  ]
}
```

Unknown or malformed files are ignored and logged rather than preventing the mod from loading.

## Runtime API

The public entry point is:

`BantersTweaks.Integration.BantersIntegration`

The public contract intentionally uses IDs, strings and DTOs rather than STS2 `CardModel`/`RelicModel` objects.

Examples:

```csharp
BantersIntegration.RegisterCardTags(
    "MY_MOD.CARD_ID",
    "Poison",
    "AoE");

BantersIntegration.RegisterMechanic(new MechanicDefinition
{
    Id = "MyMod.CustomResource",
    Name = "Custom Resource",
    Description = "A resource used by My Mod.",
    ProducerModId = "MyMod"
});

BantersIntegration.RegisterRelationship(new RelationshipDefinition
{
    SourceId = "MY_MOD.CARD_ID",
    TargetId = "MyMod.CustomResource",
    Kind = "generates",
    Description = "Generates Custom Resource.",
    ProducerModId = "MyMod"
});
```

Dynamic multiplayer/status integrations can register a provider:

```csharp
BantersIntegration.RegisterStatusProvider(
    "MyMod.Status",
    () => new IntegrationStatus
    {
        StatusId = "busy",
        Label = "BUSY",
        Detail = "Choosing a reward",
        IconKey = "thinking"
    });
```

A provider should be cheap to query and must not mutate game state.

## Run events

Other mods can contribute information to Banter's local run history:

```csharp
BantersIntegration.PublishRunEvent(
    "MyMod",
    "CustomMechanicTriggered",
    new Dictionary<string, string>
    {
        ["mechanic"] = "Bleed",
        ["amount"] = "4"
    });
```

Run events are informational only. The integration API does not expose commands such as card play, node travel, trade execution or other gameplay actions.

## Telemetry files

When local telemetry is enabled, Banter's Tweak's writes:

- `user://banters_tweaks/current_run.json`
- `user://banters_tweaks/runs/<timestamp>-<run-id>.json`

The most recent 50 run files are retained.

The initial foundation records run start, act entry and room entry. Later Deck Evolution, Shop Planner and Post-Mortem features will add richer events using the same schema.

All telemetry is local. Nothing in this system uploads run data.
