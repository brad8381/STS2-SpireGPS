# Banter's Tweak's - Full Pack Test Plan

Use this after a clean build. Test one module at a time so a failure can be isolated quickly.

## Current build smoke gate

Run these first before the full regression:

1. Startup: Banter's Tweak's loads with no exception and only the `BantersTweaks` mod folder is installed.
2. Route Planner: drag the panel; test Auto-select with Rush OFF and Rush ON.
3. Wishlist: favourite/unfavourite cards in the library and deck; confirm stars stay attached to the correct card; test ★ Wishlisted only.
4. Deck X-Ray: drag it and confirm ★ Wishlist highlights only favourited cards.
5. Relic Progress: test Brilliant Scarf through at least 0/5 -> 4/5.
6. Panels: drag Ghost Planner, Turn Timeline, Choice Compare and Trading once.
7. Telemetry: start a run and confirm `current_run.json` plus one file under `runs/` are created and contain a `RunStarted` event. End/die the run and confirm a single `RunEnded` event.
8. Integration: use Run Telemetry -> Open Folder and confirm the `integrations` directory exists.

## 0. Startup / configuration

- Build/install and confirm the game Mods folder contains BantersTweaks/BantersTweaks.dll and BantersTweaks.json.
- Confirm the legacy SpireGPS mod folder is removed.
- Launch with Banter's Tweak's enabled.
- Confirm no startup exception.
- If ModConfig is installed, open Settings -> Mods.
- Confirm Banter's modules are separate collapsible groups and appear A-Z.
- Confirm every implemented module has a setting.
- Confirm Trading is OFF by default.
- Toggle one harmless setting, restart, and confirm it persists.
- Confirm Run Telemetry is enabled by default.
- Start a run and confirm local telemetry creates `current_run.json` and a history file.
- Confirm the integration manifest directory is created even when it is empty.

## 1. Turn Guard

- First combat after launch: press End Turn with playable cards remaining.
  - Warning must contain text on the first attempt.
  - Warning should sit above the hand.
- Press End Turn with energy remaining but no playable cards.
  - Turn must end immediately.
- Put an enemy in guaranteed lethal Poison or Plague before its action.
  - That enemy must not cause a lethal-intent warning.
- In multiplayer, confirm another overlapping End Turn mod does not create stacked confirmation behavior.

## 2. Potion Guard

- Attempt to discard a potion.
- First action warns and blocks.
- Second action within the configured window confirms.
- Normal potion use must be unaffected.

## 3. Route Planner

- Open the map.
- Confirm all reachable full routes are listed.
- Sort by each room-count column.
- Set Priority 1 / 2 / 3 and confirm the star follows lexicographic priority order.
- Select a route and confirm only that route is highlighted.
- Auto-highlight suggested route should update after route changes.
- Auto-select next node:
  - Rush mode OFF: confirmation appears before travel
  - Rush mode ON: next highlighted node is selected directly with no confirmation
- Use the visible :: handle to drag the Route Planner and confirm it stays where moved while the map remains open.
- Lock the Route Planner and confirm dragging is blocked; unlock and confirm dragging returns.
- Minimize/expand the Route Planner from its top edge controls.
- Confirm the Route Planner uses rounded panel corners.
- Turn Auto-select OFF and confirm Rush is unchecked and disabled.

## 4. Drawing Palette

- Confirm 12 colors appear.
- Select several colors and draw separate strokes.
- Recolor old OFF:
  - old strokes retain their original individual colors
- Recolor old ON:
  - changing color recolors tracked old strokes
- Turn it OFF again:
  - original individual colors return
- Restart and confirm personal selection/mode persists.
- Multiplayer:
  - two players cannot keep the same claimed color
  - simultaneous conflict resolves to unique colors
  - host Force overrides Recolor old temporarily
  - removing Force restores each player's saved personal preference
- With BetterDrawing or ColorDrawLib installed, Banter's palette should yield rather than duplicate controls.

## 5. Map Legend

- Hide / Show works.
- Drag Move control and reposition the existing legend.
- Close/reopen map: position persists.
- Restart game/run: position persists.
- Reset returns to vanilla position.
- With Minty Spire 2, Minty's clickable legend behavior remains functional.

## 6. Relic Progress

Test several supported counter relics, including Brilliant Scarf.

- Brilliant Scarf shows card progress as 0/5, 1/5, etc. while active in combat.
- Fraction displays correctly.
- Ready star only appears when appropriate.
- No duplicate counter labels.
- Non-progress relics remain untouched.

## 7. Build Wishlist

- Card compendium: right-click a visible card to add/remove Wishlist.
- Deck view in a run: right-click a visible card to add/remove Wishlist.
- Other grid-card screens: Shift+right-click can add/remove Wishlist without stealing normal right-click behavior.
- Shop card/relic slots: Shift+right-click toggles Wishlist; plain right-click still opens Choice Compare.
- Card compendium: toggle ★ Wishlisted only and confirm only favourited cards remain.
- Relic collection: right-click a visible relic to add/remove Wishlist.
- Restart and confirm Wishlist persists.
- Matching reward/shop card or relic shows gold star.
- Wishlist stars appear as an attached badge on the bottom-right of the correct card in compendium and deck view.
- Left-click inspect remains normal.

## 8. Synergy Hints

- Offer cards with known direct interactions with owned cards/relics.
- Synergy chip appears only where a known factual interaction exists.
- Hover shows the reason.
- Test offered relics against a deck containing relevant card types.
- Confirm no score, ranking or recommended winner is shown.

## 9. Multiplayer Pings

- Right-click enemy: Attack / Focus / Wait / I'll handle.
- Right-click player creature or player portrait: Ready / Need Block / Need Damage / Wait.
- Teammate sees ping on correct creature.
- Ping disappears after roughly four seconds.
- Re-pinging same target replaces previous bubble cleanly.

## 10. Ghost Turn Planner

- Enable Plan Turn.
- Right-click a no-target card: it enters plan but is not played.
- Right-click a targeted card, then a valid creature.
- Invalid target warns without playing anything.
- Remove individual planned rows and Clear all.
- Confirm energy projection updates in planned order.
- Confirm known damage/Block summary updates.
- X-cost uses remaining planned energy.
- Random/unknown outcomes remain unknown rather than consuming RNG.
- Disable planning and confirm normal card play is untouched.
- Drag Ghost Planner from its visible :: handle and confirm it moves.
- Lock/unlock and minimize/expand controls work.
- Open the deck view during combat: Ghost Planner hides until the deck closes.
- Multiplayer:
  - Share ON publishes plan summary
  - Share OFF stops publishing
  - player portrait can be used as a Ghost target
  - Ghost target selection takes priority over ping menu

## 11. Deck X-Ray

- Open deck.
- Confirm total/upgraded counts.
- Click Type, Cost, State, Keyword, Utility and ★ Wishlist filters.
- ★ Wishlist highlights only favourited cards.
- Matching cards stay full opacity; nonmatches dim.
- Drag Deck X-Ray from its visible :: handle and confirm the panel moves.
- Lock/unlock and minimize/expand controls work.
- Click selected filter again / Clear Highlight to restore deck.
- Disable module while deck is open and confirm every card returns to normal opacity.

## 12. Trigger Inspector

- Hold Ctrl while hovering supported card.
- Confirm owned effects that react to the card are listed.
- Hold Ctrl while hovering supported relic.
- Confirm matching deck cards are listed.
- Release Ctrl: panel disappears.
- No recommendation score should appear.

## 13. Turn Timeline

- Expand Turn Timeline.
- Confirm Ethereal count when present.
- Confirm Poison and Plague projections.
- Enemy guaranteed to die before acting should be marked as such.
- Attack intents should appear as known incoming damage.
- Random/unknown effects must not be presented as certain.
- Timeline should update as combat state changes.
- Drag Turn Timeline from its visible :: handle and confirm the whole handle is usable.
- Lock/unlock and minimize/expand controls work.
- Open the deck view during combat: Turn Timeline hides until the deck closes.

## 14. Choice Compare

- Card reward: right-click up to three cards.
- Shop: right-click cards, relics and potions.
- Reward screen: right-click relic/potion rewards.
- Confirm side-by-side factual properties.
- Card upgrade delta should show cost/dynamic-var/keyword changes where detectable.
- Wishlist and Synergy information should appear.
- Fourth item should be rejected until one is removed.
- Leaving reward/shop screen should clear the tray.
- Normal left-click/select behavior remains untouched.
- Drag Choice Compare from its visible :: handle and confirm it moves.
- Lock/unlock and minimize/expand controls work.

## 15. Trading

Trading is host-controlled and OFF by default.

Setup:
- Enable Trading on the host.
- Configure Cards / Safe Relics / Gold / Gifting.
- Enter a Rest Site in multiplayer.

Rest Site:
- Trade appears as a Rest Site choice.
- Choosing Trade heals 15% Max HP.
- It consumes that player's Rest Site choice, so they do not Rest or Smith.
- If only one player chooses Trade, no exchange can complete.
- Once two players choose Trade, they can target one another.

Cards:
- Standard removable cards can trade.
- Removable Curses can trade.
- Eternal/bound/Quest cards must not appear as safe trades.

Relics:
- Starting relics do not appear.
- unsafe pickup/stateful/pet/stackable relics do not appear.
- accepted safe relic exchange synchronizes to all players.

Gold:
- Host can disable Gold trading.
- proposed amount cannot exceed owner's Gold.
- accepted Gold exchange updates both players identically.

Gifting:
- OFF: both sides must provide something.
- ON: one side may select Nothing (Gift).
- acceptance is still required.

Synchronization:
- Drag the Trading panel from its visible :: handle and confirm it moves.
- Lock/unlock and minimize/expand controls work.
- target receives Accept / Decline.
- decline changes nothing.
- accepted exchange occurs once.
- both clients end with identical decks/relics/Gold.
- each participant cannot complete a second exchange at that Rest Site.

## 16. Compatibility regression

Run once with the common QoL mods you use.

- Minty Spire 2
- BetterDrawing / ColorDrawLib where applicable
- ModConfig

Look specifically for:
- duplicate UI
- double input handling
- two confirmations for one action
- right-click conflicts
- map legend children breaking after moving parent
- crashes when another mod adds cards/relics with unfamiliar dynamic vars
