# SpireGPS

A modular quality-of-life toolkit for **Slay the Spire 2**.

The idea is simple: surface useful information, prevent accidental inputs, and make planning easier without changing combat balance.

## Planned modules

- **Route Planner** — enumerate every reachable route to the boss, show room counts in a right-side table, sort the list, and click a route to highlight it on the map.
- **Turn Guard** — warn before accidental end turns and handle multiplayer re-ready cases.
- **Potion Guard** — prevent accidental potion discards.
- **Relic Progress** — show useful activation/progress counters on relevant relics.
- **Synergy Hints** — show interactions between offered cards/relics and things already in your build.
- **Build Wishlist** — mark cards/relics you are looking for.
- **Multiplayer Pings** — lightweight tactical pings for enemies and teammates.

All modules default to enabled. If the existing community **ModConfig** mod is installed, SpireGPS registers its settings there. ModConfig is recommended but is not a dependency; without it, SpireGPS uses its built-in defaults.

## Route Planner

SpireGPS keeps every valid route from the current map position to the boss internally. The UI is intended to show five rows at once and scroll when more routes exist.

Routes can be sorted by:

- Preferred score
- Map/route order
- Monsters
- Unknown (`?`) rooms
- Elites
- Shops
- Campsites
- Treasures

The preferred route is not a hard-coded strategy. It uses configurable room weights:

`score = Monsters×MonsterWeight + Unknowns×UnknownWeight + Elites×EliteWeight + Shops×ShopWeight + Campsites×RestWeight + Treasures×TreasureWeight`

Positive weights prefer a room type; negative weights avoid it. Defaults are deliberately mild and can be changed through ModConfig.

## Current development status

### v0.1 foundation

- DLL-only project targeting Godot 4.5.1 / .NET 9
- Automatic Slay the Spire 2 path discovery
- Optional ModConfig integration
- All gameplay/QoL modules default ON when ModConfig is absent
- Route enumeration from the current map position to the boss
- Per-route counts for Monster, Unknown (`?`), Elite, Shop, Rest Site, and Treasure rooms
- Compact route signatures such as `M → ? → R → E → $`
- Route sorting backend
- Configurable preferred-route scoring

Next: route-list UI, clickable column sorting, and native map highlighting.
