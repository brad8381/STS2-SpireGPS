# Banter's Tweak's

A modular quality-of-life tweak pack for **Slay the Spire 2**.

The installed mod ID and DLL are **`BantersTweaks` / `BantersTweaks.dll`**. The existing C# namespace remains `SpireGPS` internally to avoid unnecessary source/API churn.

The idea is simple: surface useful information, prevent accidental inputs, and make planning easier without changing combat balance.

## Modules

- **Banter's Tweak's - Route Planner** - shows every reachable route to the boss, room counts, route highlighting, sorting, configurable priorities, and a suggested route.
- **Banter's Tweak's - Turn Guard** - warns before accidental end turns for playable cards, remaining energy, and potentially lethal incoming damage.
- **Banter's Tweak's - Potion Guard** - prevents accidental potion discards by requiring confirmation.
- **Banter's Tweak's - Relic Progress** - native-style progress fractions and ready indicators for relevant counter relics.
- **Banter's Tweak's - Drawing Palette** - fixed map drawing palette with unique multiplayer colour assignments, saved per-player colour behaviour, and optional host override. Automatically yields to BetterDrawing and ColorDrawLib.
- **Banter's Tweak's - Map Legend** - hide/show the vanilla map legend, drag it to a preferred position, persist the position locally, or reset it.
- **Banter's Tweak's - Synergy Hints** - factual mechanical interaction hints between offered cards/relics and cards/relics already in your build.
- **Banter's Tweak's - Build Wishlist** - right-click visible cards/relics in the compendium to wishlist them and show a star when they appear later.
- **Banter's Tweak's - Multiplayer Pings** - right-click enemies, player creatures, or multiplayer player portraits for short synchronized tactical pings.
- **Banter's Tweak's - Ghost Turn Planner** - non-destructively queue cards/targets, preview known energy/damage/block, and optionally share plans in co-op.
- **Banter's Tweak's - Deck X-Ray** - deck statistics with clickable highlighting for types, costs, upgrades, keywords and utility categories.
- **Banter's Tweak's - Trigger Inspector** - Ctrl-hover cards/relics to inspect factual mechanical relationships with your current build.
- **Banter's Tweak's - Turn Timeline** - expandable deterministic preview of known end-turn effects, automatic damage, enemy intents and projected HP.
- **Banter's Tweak's - Choice Compare** - pin up to three reward/shop choices and compare factual properties, upgrade deltas, wishlist state and interactions.
- **Banter's Tweak's - Trading** - optional co-op Rest Site trading. OFF by default because it changes gameplay balance.

All main modules default to enabled. The existing community **ModConfig** mod is recommended but is not a dependency. Without ModConfig, Banter's Tweak's uses built-in defaults.

## Route Planner

The Route Planner keeps every valid route from the current map position to the boss internally. Five rows are visible at once and the list scrolls when more routes exist.

Columns:

- `M` - Monster
- `?` - Unknown/Event
- `E` - Elite
- `$` - Shop
- `R` - Rest Site
- `T` - Treasure

Routes can be sorted by route order, room counts, or suggested route.

### Suggested route priorities

The suggested route can use ordered priorities before the weighted score. Example:

`Elites ↑ > Treasure ↑ > None`

That means:

1. Prefer the route with the most Elites.
2. If tied, prefer the route with the most Treasure rooms.
3. If still tied, use the configurable weighted score.
4. If still tied, use stable route order.

Available priorities include Elites, Treasure, Rest, Shop, Unknown, fewer Monsters, more Monsters, or None.

### Suggested route automation

- **Auto-highlight suggested route** - automatically highlights the current suggested route.
- **Auto-select next suggested node** - automatically selects/votes for the next node on the highlighted route.
- **Rush mode** - when OFF, Auto-select asks for confirmation before selecting the next node. When ON, the next suggested node is selected immediately.

Auto-select next node defaults OFF. Rush mode defaults OFF.

## Current development status

Implemented for the current v0.1 development branch:

- DLL-only project targeting Godot 4.5.1 / .NET 9
- Optional ModConfig integration
- Route enumeration from the current map position to the boss
- Route room counts and signatures
- Scrollable route panel
- Clickable route highlighting
- Column sorting
- Suggested-route weighting
- Priority 1 / 2 / 3 route logic
- Optional automatic next-node selection with confirmation or Rush mode
- Turn Guard
- Potion Guard
- Relic Progress fractions and ready indicators
- Shared warning/toast UI

Also implemented on the current development branch:

- Drawing Palette with exclusive multiplayer colours
- Per-player "Recolor old" drawing behaviour plus host force option
- Map Legend hide/show, move, remember position, and reset
- Build Wishlist for cards and relics
- Synergy Hints for cards and relics
- Multiplayer combat pings

## Newly implemented modules

- **Ghost Turn Planner** - right-click hand cards while planning mode is active; targeted cards wait for a right-click target. The plan never plays cards or consumes RNG.
- **Deck X-Ray** - clickable deck filters/highlights for card type, cost, upgrade state, keywords, Block, AoE, Draw and Energy sources.
- **Trigger Inspector** - hold Ctrl while hovering supported cards/relics to inspect direct mechanical relationships.
- **Turn Timeline** - deterministic-only end-turn preview; random/unknown results are not guessed.
- **Choice Compare** - compare up to three cards/relics/potions without ranking them.
- **Trading** - host-controlled, OFF by default. Choosing Trade at a Rest Site heals 15% Max HP instead of Resting/Smithing. Both players must choose Trade before exchanging. Supports cards (including removable Curses), safe relics and Gold. The host can optionally allow gifting.

## Compatibility

Banter's Tweak's defaults to yielding when another QoL mod owns the same hook.

- **Minty Spire 2** - compatible with Turn Guard. Minty only attaches relic reminder UI to the End Turn button; Banter's guards the release action.
- **BetterDrawing** - Banter's Drawing Palette disables itself to avoid duplicate drawing controls and line-colour patches.
- **ColorDrawLib** - Banter's Drawing Palette disables itself to avoid duplicate drawing controls and colour protocols.
- **Other End Turn mods** - if another Harmony Prefix or Transpiler owns `NEndTurnButton.CallReleaseLogic`, Banter's Turn Guard yields by default.

This behaviour can be overridden with **Yield to Overlapping QoL Mods** in ModConfig.


## Integration API

Banter's Tweak's now includes an initial public integration layer for other mods and external tools.

- API version: `1`
- Static JSON schema: `banters.integration.v1`
- Run telemetry schema: `banters.run.v1`
- External manifests: `user://banters_tweaks/integrations/*.integration.json`
- Runtime entry point: `BantersTweaks.Integration.BantersIntegration`

The public API is deliberately informational: mods can register mechanics, card/relic tags, relationships, status providers, and run events. It does **not** expose gameplay execution commands.

See [INTEGRATION.md](INTEGRATION.md) for the full contract and example JSON.

## Local Run Telemetry

Local run telemetry is enabled by default and currently records the run lifecycle foundation needed by Deck Evolution and Post-Mortem.

Files:

- `user://banters_tweaks/current_run.json`
- `user://banters_tweaks/runs/<timestamp>-<run-id>.json`

The latest 50 run files are retained. Telemetry is local-only and is not uploaded anywhere.

The current lifecycle event set includes `RunStarted`, `ActEntered`, `RoomEntered`, `RunEnded`, and `RunClosed`. `RunEnded` also records a final player/run snapshot. Future modules will publish card, relic, shop, route, damage and decision events into the same history rather than creating separate tracking systems.

At the end of a run, Banter's Tweak's can show a compact in-game **Run Recap** with outcome, act/floors, route breakdown, run time, HP/Gold, deck/relic counts and score when available.

## Attribution and Development References

Route Planner contains implementation/design work derived from or inspired by STS2RouteSuggest. The upstream MIT notice is included in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Other repositories consulted only as API/compatibility references are listed in [DEVELOPMENT_REFERENCES.md](DEVELOPMENT_REFERENCES.md).


## Settings layout

When ModConfig is installed, Banter's Tweak's registers as **one top-level mod banner**. Inside that banner the individual Banter modules are listed A-Z as nested collapsible sections, so the Mods tab is not flooded with separate top-level registrations.
