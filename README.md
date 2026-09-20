# Banter's Tweak's

A modular quality-of-life tweak pack for **Slay the Spire 2**.

The internal mod ID and DLL remain `SpireGPS` for compatibility, but the visible mod name is **Banter's Tweak's**.

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
- **Confirm before auto-travel** - enabled by default. Shows a confirmation before selecting the node because selecting a map node can immediately cause travel in single-player.

Auto-select next node defaults OFF. Confirm before auto-travel defaults ON.

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
- Optional automatic next-node selection with confirmation
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

## Planned next modules

- **Ghost Turn Planner** - queue cards and targets as a non-destructive plan, preview deterministic energy/damage/block, and optionally share the plan with co-op teammates. Random outcomes remain unknown rather than being guessed.
- **Deck X-Ray** - collapsible deck statistics with clickable categories for card type, cost, keywords, upgrades, draw, energy, AoE and other factual deck properties.
- **Trigger Inspector** - inspect a card/relic/power to see which owned items currently trigger it, and inspect a card to see which owned effects react to it.
- **Turn Timeline** - show the deterministic order of end-turn/start-turn effects, automatic damage, enemy actions and projected known HP/block changes before ending the turn.
- **Choice Compare** - pin 2-3 offered cards/relics/potions and compare their factual properties, upgrade changes, owned copies, wishlist state and existing build interactions side-by-side.
- **Trading** - optional gameplay-altering co-op module, OFF by default. Initial design is a Rest Site trade action with a real opportunity cost, 1-for-1 trading first, and host-configurable rules.

These are intentionally separated from the existing modules until implemented so ModConfig never shows controls that do nothing.

## Compatibility

Banter's Tweak's defaults to yielding when another QoL mod owns the same hook.

- **Minty Spire 2** - compatible with Turn Guard. Minty only attaches relic reminder UI to the End Turn button; Banter's guards the release action.
- **BetterDrawing** - Banter's Drawing Palette disables itself to avoid duplicate drawing controls and line-colour patches.
- **ColorDrawLib** - Banter's Drawing Palette disables itself to avoid duplicate drawing controls and colour protocols.
- **Other End Turn mods** - if another Harmony Prefix or Transpiler owns `NEndTurnButton.CallReleaseLogic`, Banter's Turn Guard yields by default.

This behaviour can be overridden with **Yield to Overlapping QoL Mods** in ModConfig.
