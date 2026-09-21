# Development References

These projects/resources were consulted while developing Banter's Tweak's.

Reference-only means their source was used to understand Slay the Spire 2 APIs or compatibility behaviour; source blocks were not copied into Banter's Tweak's unless specifically called out in THIRD_PARTY_NOTICES.md.

## Materially derived / attributed

- **STS2RouteSuggest** — https://github.com/jiegec/STS2RouteSuggest
  - Route enumeration/order and map highlighting architecture informed the Route Planner.
  - See THIRD_PARTY_NOTICES.md for the required MIT attribution.

## API / compatibility references

- **BaseLib-StS2** — https://github.com/Alchyr/BaseLib-StS2
  - Referenced for current Godot/C# and STS2 modding API usage.
- **STS2 Archipelago** — https://github.com/dlueben1/Slay-the-Spire-2-Archipelago
  - Referenced while checking current RestSiteOption behaviour.
- **sts2-rl-agent decompiled reference** — https://github.com/zhiyue/sts2-rl-agent
  - Referenced to understand current STS2 runtime APIs such as card-library filtering and relic state.
  - No source blocks from this repository are included in Banter's Tweak's.
- **CosmicPrincessKaguyaMod** — https://github.com/YujiSX/-Slay-The-Sprie2-CosmicPrincessKaguyaMod
  - Referenced while checking custom RestSiteOption implementations.
- **ModConfig**
  - Banter's Tweak's detects and uses its public runtime API through reflection; it remains optional.
- **BetterDrawing / ColorDrawLib / Minty Spire 2**
  - Used as compatibility targets so overlapping Banter modules can yield cleanly.
