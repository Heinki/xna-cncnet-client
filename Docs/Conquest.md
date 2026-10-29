# Conquest

The main menu's **Conquest** entry opens centered Conquest settings directly,
using the campaign dialog's border and background. **Q** also opens it when
main-menu hotkeys are enabled. Select campaign size, starting credits, AI
difficulty, seed, node-buff density, and both factions. **New Campaign** replaces
the current save after confirmation; **Continue** restores it. **Main Menu** returns.
Battle maps are assigned randomly to nodes from the installed compatible map list
when the campaign is created. The assignment is saved. There is no battle-map
selector, map exclusion, or separate Battle Options screen. A missing map does
not prevent autobattle.

## Implementation order

The original launch prototype did not contain the strategic campaign systems.
The implementation adds them in this order:

1. **Milestone 1:** campaign settings, a generated route network, territory
   and army selection, zooming, pointer-centered wheel zoom, dragging, and keyboard panning.
2. **Milestone 2:** armies, recruitment, adjacent movement, capture, alternating
   turns, stepwise AI, Auto Combat, income, expenses, campaign outcomes, and saves.
3. **Milestone 3:** actual Skirmish launching through the installed mod, fresh
   result-log parsing, unresolved-battle recovery, and persistent AI interruptions.
4. **Milestone 4:** territory bonuses, permanent upgrades, consumable supplies,
   native TS/YR/Ares battle modifiers, and a mod-specific battle-rule hook.
5. **Final task:** the persistent unified action log and its map interface.

Lightweight structured gameplay notifications support animation and battle
interruptions. The history store, faction indexes, event journal, filters, and
complete action-log panel were added after the core campaign implementation.

## Strategic gameplay

The map contains 9, 25, 64, or 100 scattered nodes, joined by visible routes.
A seeded spanning tree guarantees connectivity; short non-crossing extra links
provide alternate routes. Nodes occupy an irregular sector with two opposing
starting bases. Positions and connections are stored in the save. The player
and AI begin in opposite corners, each with one strength-8 army. Other territories
are neutral, defended by strength 2. The seed controls territory bonuses and
Auto Combat randomness, whose state is saved.

Click one of your ready tank markers to select its army. Its node receives a
gold ring. Connected legal destinations and route arrows turn green for friendly
moves/merges, orange for attacks. Click a highlighted destination to issue that
order. Clicking an unlinked node only inspects it; it cannot move the army.
Click empty space or press Escape over the map to clear army selection.
Blue tanks belong to the player; red tanks belong to the AI. A slash marks an
army that has used its action. Strength appears beside the marker.
Friendly armies merge, using both armies' actions. An army acts once per turn.
**Recruit 100** creates or reinforces an army by 5 strength and uses its action.
Clicking an army that has acted still selects it for inspection. Recruitment
and merging are capped at 1000 strength per army.

Each node lists its own credit income, awarded at the start of its owner's turn.
New nodes generate 50–350 credits. Research-rich nodes tend to have lower income;
income-rich nodes have fewer combat buffs. **Few**, **Normal**, and **Many** in
setup control the number of buffs per node. Only stats supported by the current
game/mod are generated. Hovering or selecting a node shows its income, effects,
army, and assigned map in the left inspector. That list scrolls when necessary.
Combat buffs apply faction-wide while the node is owned and are removed on loss.

Starting credits are granted once. AI income is 75%, 100%, or 125% on easy,
normal, or hard difficulty. The AI purchases improvements, moves armies,
resolves neutral battles, recruits, and ends its turn. Eliminating the opposing
faction's territories ends the campaign. Finished campaigns retain map and history.

- **Research…:** opens the permanent research screen. HP, armor, speed, attack
  speed, and damage each have their own purchase row and three gold level markers.
  Buy a level for 400 credits. Each level adds 10%, up to three
  levels per stat. Unsupported choices are omitted. Research is permanent and
  faction-wide. Research and owned-node bonuses add, capped at +100% per stat.
- **Supplies…:** opens the temporary supply screen, with blue charge markers and
  an explicit **Buy supply 150** button. A charge grants +20% damage in the next battle involving that faction, with up
  to three supplies stored. One supply is committed when the attack begins,
  including while defending. Loading or retrying cannot consume it again.
- **HP / armor:** improve durability. The engine adapter may represent both as
  effective health; cloning converts both to the clone's Strength, following
  the randomizers' durability approach.
- **Attack speed:** reduces weapon reload delay, rather than increasing it.

**Bonuses…** opens the faction overview. **Player bonuses** and **Enemy bonuses**
show each stat's permanent research, owned-node contribution, capped total, and
opponent's total. An equal-army stat index compares both factions against a base
of 100, excluding army size, supplies, and starting grants. Those are shown
separately, together with actual income and recruitment strength. Values ahead
of the opponent are green; values behind are red. The window scrolls for long
inventories. AI presentation and map orders pause while this window is open.

Permanent research and temporary supplies have separate gold and blue summary
cards on the map. There is no unlabelled stat dropdown. Research survives node
losses; territory stat bonuses apply only while owned; supplies last one battle.

Some nodes also carry a **permanent capture reward**, shown in the inspector.
The reward is collected on the first capture by each faction and remains in
that faction's inventory after losing the node. Recapture cannot farm rewards.
Reward frequency follows the Few / Normal / Many setup option. Built-in rewards
are **Recruitment training** (+1 army strength per recruit per level) and **War
treasury** (+25 credits per turn per level), both capped at three levels.
New campaigns generate these reward nodes; older campaigns retain their map.

Game-specific rewards are capability-gated. **Starting troops** and **Starting
defenses** require explicit game-pack definitions with actual map/spawner rule
adapters for the supported engines and map pool. The client does not guess
faction unit IDs, terrain placement, or engine scripting commands. **Veteran
production** is available on YR/Ares only when the configured isolated clone
roster contains trainable units. It grants veteran construction for that roster;
human and AI country lists stay separate even in same-faction matches. This
uses YR's native [country veteran-unit lists](https://github.com/Ares-Developers/YRpp/blob/master/HouseTypeClass.h).
Unsupported special rewards are not offered. See the configuration example for
`ConquestPerk:` definitions and faction-scoped adapter templates.

Selecting an attack focuses and zooms to its target. The left panel shows that
battle's territory, **Start Battle**, **Autobattle**, and player win/loss odds.
The bottom status includes the committed attack/defense ratings. Autobattle's
win chance is exactly the player's rating divided by both ratings' sum. These
ratings combine army strength and supported stats; real Skirmish outcomes depend
on play. Battle buttons remain tied to the pending target while inspecting other
nodes. Start Battle is unavailable when no map was assigned.

Auto Combat rolls against the two committed power values. Defeated armies are
removed. Victors suffer casualties, retain at least 1 strength, and capture the
territory when attacking. Territory bonuses immediately follow ownership.

Right/middle dragging pans the map; the wheel zooms around the pointer. Arrow
keys pan while the pointer is over the map. **Zoom +**, **Zoom -**, and **Center**
provide additional camera controls. **Overview** fits every node on screen and is
the default view when opening a campaign. At wide zoom levels, compact node
labels and army markers keep large maps readable, including at 800×600.
Clicking a territory reference in history selects that territory and centers the camera, including edge territories.

## Campaign history and AI animation

The strategic map contains a collapsible history panel. **All Actions** is the
default; **Player Actions** and **AI Actions** retain the original chronological
order within their faction. Every entry includes its round number and faction.
Player entries are blue, AI entries red; attacks, captures, losses, destroyed
armies, battle results, and campaign outcomes receive additional highlighting.
Source and target territories are clickable references; inspecting history does
not issue a move order.

The history records turn starts/ends, recruitment, movement, attacks, captures,
losses, destruction, credits generated/spent, territory bonuses gained/lost,
permanent purchases, supplies purchased/consumed, Auto Combat and Skirmish
results, and campaign victory/defeat. Contested battle results are recorded once
for each participating faction, with that faction's own outcome.

New entries scroll into view automatically. Scrolling upward suspends following
so previous events stay readable. Scroll back to the bottom or click **Latest**
to resume. The scrollbar, mouse wheel, Home/End, and Page Up/Down navigate older
entries. Keyboard history controls apply while the pointer is over the history.
Filtering and drawing do not create one control or formatted string per event:
the panel draws only visible rows and uses prebuilt faction indexes.

**Normal**, **2×**, **4×**, and **Instant** control AI presentation. Their controls
remain available when the history is collapsed. Instant skips presentation
waits; it still executes every gameplay action. AI work is bounded per frame.
Every AI attack on the player pauses for **Autobattle** or **Start Battle**,
including in Instant mode. After resolution, AI execution resumes automatically
at the saved speed. AI attacks on neutral territories resolve automatically.

## Battles and mod integration

`ConquestLobby` retains the installed mod's `SkirmishLobby.ini` layout and
`GameOptions.ini`. It has separate settings, players, and controls from regular
Skirmish. `GameClass` registers it separately, and `MainMenu` opens it through the
existing top-bar and music lifecycle.

Campaign Skirmishes use the target node's saved map/game mode, with support
for one human and one AI. Co-op, multiplayer-only, human-only, and incompatible
player-count maps are rejected. The campaign's two sides and AI difficulty are
restored for each battle; random factions are resolved once at campaign creation,
and game options are stored in the campaign snapshot. Starting positions and
colors are randomized, teams are cleared, and normal lobby validation still runs. `GameLobbyBase` writes the normal `spawn.ini` and prepared map and
uses the installation's normal executable, launcher, platform wrapper, and
command line. Conquest matches are excluded from regular Skirmish statistics.
The usual replay and saved-game infrastructure still runs.

Before launching, Conquest saves the pending battle and clears the configured
statistics log. On exit, it accepts only a completed match with an explicit
non-spectator local-player result from the normal parser, matching the saved
match ID and local player identity. Contradictory winner records are rejected. It maps that result
back to the campaign attacker, including when the human was defending.
An aborted launch or absent/incomplete result leaves the attack unresolved.
Retrying or choosing Auto Combat preserves its committed bonuses and history.
A restart also returns to the unresolved attack rather than claiming a result.

Combat integration is capability-based because this client serves multiple games.
No game executable, unit ID, country, or Vinifera extension is inferred from a
randomizer installation elsewhere on the machine. Python randomizer code is a
reference, not a runtime dependency.

- **TS / YR / Ares defaults:** HP, armor, speed, attack speed, and damage use
  separate native handicap profiles for human and AI. Even same-faction opponents
  receive separate stats. HP and armor compose as durability, speed uses movement
  multipliers, damage uses Firepower, and attack speed divides ROF. The human's
  base profile is Normal; the AI retains its chosen difficulty. Loose engine-
  specific rules and generated-map profile values provide the baseline. Changed
  stats without an exposed baseline use native multiplier defaults of 1. A mod
  whose difficulty values live only in packed/custom rules should declare its
  effective `RulesFile` or disable native profiles and provide its own mapping.
- **TD / RA / D2K defaults:** no guessed combat-stat adapter or unit cloning.
  Combat research and supply are unavailable; nodes provide income. Armies,
  map navigation, real Skirmishes, autobattle, AI interruptions, saves, and history
  still work. A mod can explicitly declare stats and implement their map hooks.
- **Unit cloning is opt-in:** `EnableUnitClones`, an effective `RulesFile`, and
  an explicit `CloneTypes` list are required. TS additionally requires a reserved
  Vinifera `ProductionHouseIndex`. DLL presence alone never enables cloning.
  Only listed mobile types and their deployment forms are copied. Player units
  and weapons receive private IDs; ownership gates prevent native same-faction
  AI from producing them. Native enemy copies receive only enemy stats. Unlisted
  shared types and shared weapon definitions retain their stats. Production
  infrastructure and prerequisites remain native. Starting native units and
  custom spawn/delivery systems require mod-specific integration; the generic
  adapter does not rewrite them. Mod authors should enable this mode only for a
  validated roster and engine extension, rather than cloning an arbitrary mod.

The native spawner interfaces are documented in the primary sources:
[TS house handicaps](https://github.com/CnCNet/ts-patches/blob/master/src/spawner/spawner.asm)
and [YR per-house handicap overrides](https://github.com/CnCNet/yrpp-spawner/blob/main/src/Spawner/Spawner.Config.cpp).

**ConquestBattle.ini** in the active/base resource directory declares supported
stats and optional cloning. Non-configuration sections are applied as generated-
map overrides after the adapter. Placeholders expose committed ratings, damage
multipliers, and each faction's individual stat percentages. See
[ConquestBattle.example.ini](ConquestBattle.example.ini). Installed rules are never
rewritten. Regular Skirmish does not receive these changes.

## Persistence

- Battle setup: `Client/ConquestSettings.ini`.
- Strategic snapshot: `Client/Conquest/Campaign.json`.
- Previous snapshot: `Client/Conquest/Campaign.json.bak`.
- Event journal: `Client/Conquest/<campaign-id>.history`.

Keep the snapshot and matching journal together when copying a campaign. The
journal contains length-prefixed UTF-8 JSON events, without a BOM. Saves append
only new events, flush them, then atomically replace the strategic snapshot.
The snapshot commits the journal byte length and event count. Loading ignores
an uncommitted tail; the next save truncates it. A failed save pauses campaign
progress until **Save** succeeds. Snapshot validation rejects invalid territories,
armies, economy, phase, outcome, or pending-battle state. IDs preserve chronology
independently of wall-clock adjustments and reject repeated result callbacks.
Save format 4 also accepts formats 1–3. Permanent reward definitions, claimed-node
markers, faction inventories, and committed battle inventories are saved. Legacy flat upgrades become damage
research; legacy node bonuses become income, damage, or armor effects. Pending
battles retain their already committed ratings. Old grid-prototype saves gain scattered
node positions while retaining every
original route, army, history entry, and pending battle. New campaigns use the
new generated route network. Old campaign journals are retained so the previous snapshot remains recoverable.
To recover a previous snapshot, preserve the files and replace `Campaign.json`
with `Campaign.json.bak`, retaining the journal named by that snapshot.

## Theming

The main-menu entry remains named `btnConquest`. The bundled theme moves the
main menu upward and places Conquest between Skirmish and Options. Its caption
uses TeX Gyre Adventor Bold, with the rounded geometric forms of the bundled
menu captions, a soft black shadow, cyan idle text, and white hover bloom.
The font and its GUST Font License are embedded for packs without the new font assets. Themes may provide
custom idle/hover images using normal button attributes in `MainMenu.ini`.
Do not add a duplicate ExtraControl. Debug builds refresh the Conquest
INI definitions in existing output folders, including the main-menu placement. Older themes without a Conquest section
receive an entry after Skirmish using the column's actual spacing. Text-button
themes reuse their Skirmish background, font, colors, and hover sound.

The settings, strategic map, and bonus windows use `INItializableWindow`, like
the game lobbies. `ConquestSetupPanel.ini`, `ConquestWindow.ini`, and
`ConquestBenefitsPanel.ini` expose named control sections for standard attributes
and `$X`, `$Y`, `$Width`, and `$Height` layout expressions. Campaign-bound controls
retain code defaults for packs without these files; additional decorations can
use the normal `[$ExtraControls]` declarations.

`ConquestSetupPanel.ini` inherits the campaign-style `GenericWindow.ini` border
and uses `MainMenu/dbak.png` in the bundled TS theme. Other packs fall back to
their own GenericWindow styling. The settings form centers and shrinks to fit the
client resolution. The map uses faction-tinted tank silhouettes by default;
mods can supply a transparent `ConquestArmy.png` in the active/base resource
search path to use their own unit or fleet marker. PNG preserves transparent
edges around the unit. No additional bitmap asset is required.

History, territory effects, and faction bonus summaries use `XNAScrollPanel`
for native scrolling and clipping. History draws only visible rows on one
virtual surface, with a bounded scrollbar range and a viewport-sized render
target. Large histories retain filtering, territory links, manual reading
position, and automatic following of new events.

Research and Supply are faction-wide purchases, independent of the inspected
node. Their permanent/one-battle effects, prices, limits and consumption are
explained in setup, persistent map captions, and button tooltips.

## Verification

Restore before building, as required by the repository:

```shell
dotnet restore DXClient.slnx -p:Configuration=UniversalGLRelease
dotnet build DXMainClient/DXMainClient.csproj -p:Configuration=UniversalGLRelease -f net8.0 --no-restore
```

No unit tests were added. Temporary command-line campaign scenarios exercised
complete campaigns, purchases, auto combat, simulated Skirmish outcomes,
interruptions, repeated result callbacks, save/load, and outcomes. A separate
100,005-event scenario checked chronological faction filtering, save/load, and
uncommitted-tail recovery. These scenarios do not replace a real game playthrough. Additional scenarios used the production statistics parser for all
attacker/defender outcomes, rejected incomplete/stale/contradictory/duplicate
results, and inspected generated TS/YR/Ares modifiers for every AI difficulty.
Software-rendered UI checks exercised node selection, edge centering, zoom,
panning, filters, collapse/expand, animation speeds, and shared texture lifetime.
Actual main-menu, lobby, settings, new-campaign, saved-campaign and map navigation,
including return/reopen, were also checked in an isolated client copy. This environment required system OpenAL in
the temporary client copy to avoid a bundled native audio startup failure;
no repository native libraries were replaced.

Use a compatible game/mod installation for the remaining manual checks:

1. Open Conquest with its menu button and Q. Start every node count, adjust seed,
   credits, difficulty, and buff density. Check zoom, dragging, arrows, selection, recruitment,
   gold army selection, green/orange legal-route previews, unlinked-node rejection,
   friendly merging, captures, economy, victory, and defeat.
2. Follow multiple player and AI turns. Check every event family, all three
   filters, faction colors, important-event highlighting, scrolling, Latest,
   collapse/expand, and both source/target territory links at the map edges.
3. Switch all four AI speeds during movement. In Instant mode, let an AI army
   attack the player. Verify a pause before gameplay resolution; resolve the
   battle and confirm continuation at the same speed.
4. Save/restart during each turn phase, during a pending attack, and after the
   campaign finishes. Confirm history, random state, purchases, bonuses, army
   actions, pending attacks, and animation speed survive without duplicate events.
5. Launch real battles as attacker and defender. Verify `spawn.ini`, prepared
   map, mod rules, executable, explicit victory/defeat detection, and AI resumption.
   Abort a battle or remove its statistics log; confirm retry/Auto Combat remains
   possible without an extra attack or supply consumption.
6. Inspect generated handicap/map modifiers for each supported stat. Confirm
   same-faction opponents keep separate bonuses. Exercise the four bonus tabs,
   permanent research levels, temporary supply charges, first-capture rewards,
   persistence after territory loss, and recapture protection. For an opt-in cloning adapter,
   validate the configured roster, deployment, weapons, native starting units,
   and mod-specific delivery systems in that mod. Check unsupported games omit
   unavailable research instead of receiving guessed unit edits.
7. Verify regular Skirmish settings/ranks, main-menu navigation, music, campaign,
   CnCNet, and LAN remain functional. Repeat with a second mod/theme and with a
   long campaign history. The bundled resources do not include a playable game.
