# SpireGPS

A modular quality-of-life toolkit for **Slay the Spire 2**.

The idea is simple: surface useful information, prevent accidental inputs, and make planning easier without changing combat balance.

## Planned modules

- **Route Planner** — enumerate every reachable route to the boss, show room counts in a right-side table, and click a route to highlight it on the map.
- **Turn Guard** — warn before accidental end turns and handle multiplayer re-ready cases.
- **Potion Guard** — prevent accidental potion discards.
- **Relic Progress** — show useful activation/progress counters on relevant relics.
- **Synergy Hints** — show interactions between offered cards/relics and things already in your build.
- **Build Wishlist** — mark cards/relics you are looking for.
- **Multiplayer Pings** — lightweight tactical pings for enemies and teammates.

Every feature is intended to be independently configurable through ModConfig. ModConfig integration is optional; sensible defaults are used when it is not installed.

## Current development status

### v0.1 foundation

- DLL-only project targeting Godot 4.5.1 / .NET 9
- Automatic Slay the Spire 2 path discovery
- Optional ModConfig integration
- Route enumeration from the current map position to the boss
- Per-route counts for Monster, Unknown (`?`), Elite, Shop, Rest Site, and Treasure rooms
- Compact route signatures such as `M → ? → R → E → $`

Next: route-list UI and native map highlighting.
