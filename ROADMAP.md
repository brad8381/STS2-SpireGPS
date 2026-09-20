# Banter's Tweak's - Roadmap

This file captures approved module designs before implementation. Planned modules should not appear in ModConfig until they actually have working code.

## 1. Ghost Turn Planner

Goal: let a player plan a turn without executing it.

- Enter planning mode from combat.
- Queue cards in an ordered list and assign targets.
- Do not remove cards, spend energy, trigger powers, alter RNG, or enqueue game actions.
- Show deterministic projections only:
  - planned energy spend
  - known direct damage
  - known Block
  - known debuffs/buffs where practical
- Random results remain `?`.
- X-cost cards show the deterministic spend based on the current planned energy state.
- Plans can be cleared or edited at any time.
- Multiplayer:
  - optionally publish the current plan to teammates
  - teammates can see intended targets/order
  - no player is forced to follow a plan
- Future integration:
  - Turn Timeline can show the projected result after the Ghost Plan
  - Context Pings can reference planned targets

This is QoL only. It must never autoplay the planned sequence.

## 2. Deck X-Ray

Goal: make the current deck understandable at a glance without scoring it.

Initial summary:

- total cards
- Attacks / Skills / Powers
- energy-cost distribution
- upgraded / unupgraded
- Exhaust
- Ethereal
- Retain
- Innate
- Unplayable
- draw sources
- energy sources
- Block cards
- AoE cards where deterministically identifiable

Interaction:

- click a statistic to highlight matching cards in the existing deck view
- clicking the statistic again clears that filter
- multiple-category filtering can be added later
- no recommendation score or judgement such as "too many Skills"

## 3. Trigger Inspector

Goal: make mechanical relationships inspectable in either direction.

Examples:

- inspect Joss Paper -> highlight/list owned Exhaust/Ethereal cards
- inspect Mummified Hand -> highlight/list owned Powers
- inspect Pen Nib -> highlight/list owned Attacks
- inspect an Exhaust card -> list Feel No Pain, Dark Embrace, Joss Paper, Burning Sticks, etc. that react to it

Rules:

- factual mechanical links only
- reuse the same relationship engine as Synergy Hints and Deck X-Ray
- support modded cards/relics when relationships can be identified safely
- do not assign synergy scores

## 4. Turn Timeline

Goal: answer "what deterministic things happen if I end my turn now?"

Example:

1. Ethereal cards exhaust
2. player end-turn effects
3. Enemy A start-of-turn Poison/Plague
4. Enemy A action if still alive
5. Enemy B start-of-turn effects
6. Enemy B action
7. player start-of-turn effects
8. draw/energy state

Display known projections such as:

- expected incoming damage
- projected player HP
- projected enemy HP
- enemy dies before acting
- known Block changes
- known counter/relic triggers

Rules:

- preserve actual engine ordering
- deterministic information only
- random target/draw/generated-card results display as `?`
- must not consume RNG or simulate by mutating the real combat state
- can eventually feed Turn Guard so both features use one source of truth

## 5. Choice Compare

Goal: compare offered choices without recommending a winner.

Workflow:

- right-click / compare action pins an offered card, relic or potion
- compare up to 3 items side-by-side
- dismiss individual pins or clear all

Card comparison can include:

- type
- cost
- upgrade changes
- rarity
- existing copies
- keywords
- Wishlist state
- factual Synergy Hints
- Trigger Inspector relationships

Relic comparison can include:

- rarity
- deck items that trigger/interact with the relic
- existing relevant counters/types from Deck X-Ray

Potion comparison can include factual target/type/use information where available.

No overall score, ranking or "best choice" label.

## 6. Trading

This is gameplay-altering and therefore separate from normal QoL.

Default: OFF.

Initial co-op concept:

- add a Trade interaction at Rest Sites
- trading must have a meaningful opportunity cost
- candidate default: Trade gives only 15% max-HP healing instead of the normal full Rest heal, then opens the trade screen
- host can configure/disable the module

Initial trade rules:

- Cards: allowed
- Relics: allowed with safety restrictions
- Potions: candidate
- Gold: not initially
- Starting/bound/character-critical relics: blocked
- 1-for-1 exchanges first
- gifting can be a later host option

The exact balance cost should remain configurable while testing.

## Deferred ideas

- Personal run notepad
- Event/journal memory
- richer Context Pings tied to Trading or Ghost Turn Planner
