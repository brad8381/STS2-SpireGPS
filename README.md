# Banter's Tweak's

A modular quality-of-life tweak pack for **Slay the Spire 2**.

The internal mod ID and DLL remain `SpireGPS` for compatibility, but the visible mod name is **Banter's Tweak's**.

The idea is simple: surface useful information, prevent accidental inputs, and make planning easier without changing combat balance.

## Modules

- **Banter's Tweak's - Route Planner** - shows every reachable route to the boss, room counts, route highlighting, sorting, configurable priorities, and a suggested route.
- **Banter's Tweak's - Turn Guard** - warns before accidental end turns for playable cards, remaining energy, and potentially lethal incoming damage.
- **Banter's Tweak's - Potion Guard** - prevents accidental potion discards by requiring confirmation.
- **Banter's Tweak's - Relic Progress** - native-style progress fractions and ready indicators for relevant counter relics.
- **Banter's Tweak's - Drawing Palette** - selectable map drawing colours with unique multiplayer colour assignments.
- **Banter's Tweak's - Map Legend** - hide/show the vanilla map legend and move it to a preferred position.
- **Banter's Tweak's - Synergy Hints** - planned mechanical interaction hints for offered cards and relics.
- **Banter's Tweak's - Build Wishlist** - planned wishlist marking for cards and relics.
- **Banter's Tweak's - Multiplayer Pings** - planned lightweight tactical pings.

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

Queued next:
- Drawing Palette with exclusive multiplayer colours
- Map Legend hide/show, move, remember position, and reset
- Build Wishlist
- Synergy Hints
- Multiplayer Pings
