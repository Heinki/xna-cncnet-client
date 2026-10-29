# Starting forces and node defenses

Conquest's **Perk shop…** buys permanent faction perks and local defenses. Training
and treasury cost 500 campaign credits per level. When effective TS or YR rules
are available, the shop also offers:

| Purchase | Cost | Effect |
| --- | ---: | --- |
| Infantry escort | 500 / level | Three extra faction infantry per level, up to three levels |
| Vehicle escort | 750 / level | One extra faction combat vehicle per level, up to three levels |
| Field power plant | 500 | One faction power plant near the base in every battle |
| Field refinery | 800 | One faction refinery near the base in every battle |
| Field barracks | 500 | One faction barracks near the base in every battle |
| Veteran escorts | 700 | Purchased infantry and vehicle escorts start at veteran rank; requires an escort purchase |
| Node garrison | 450 | One faction defense on the selected owned node, used only when defending |

These purchases persist across battles. They use campaign credits, not in-battle
starting money. Troops are selected from each participant's eligible combat
roster; power, refinery, and barracks types come from the game's `[AI]` build
lists. Owner, RequiredHouses, and ForbiddenHouses restrictions apply. Custom
rosters are never taken from another installed game. TS component-tower defenses
include their weapon attachment. Powered defenses still require power.

Starting objects use the resolved `SpawnN` owner for each participant. Placement
reads the map's compressed tile and overlay packs and the installed theater's
flat ground groups. Clear/sand/grass/pavement, rough ground and flat roads are
eligible when that game's rules allow building on the corresponding land type.
Slopes, cliffs, water, bridges and railways remain excluded. Sites use the
base's elevation, reserves building footprints and a margin, avoids existing
objects and trees, and leaves room around start positions for the construction
yard. Purchases are committed at attack time. A map without safe space leaves
the battle pending with Auto Combat available; it never receives half a grant.
A failed preparation, including an `InvalidDataException` from placement, is
caught at the Conquest launch boundary. It clears the launched flag, preserves
the pending attack and committed bonuses, saves the retryable battle and reports
the reason without closing the client.
The native structure bounds are conservative; custom structure types reserve a
6-by-6 area. Packs with larger/custom foundations should provide explicit adapters.
Map decompression has fixed input/output bounds and disposes its streams.

Native grants require an effective loose `rules.ini` (TS) or `rulesmd.ini` (YR),
in the game root or `INI/`, or an explicit `[Conquest] RulesFile`. Flat ground lookup reads matching theater INIs from the same game root or
`INI/` directory, preferring expansion-specific definitions for YR. If no theater
file is available, placement uses its conservative clear-tile fallback. A pack
keeping all effective data inside MIX files must export the relevant INIs or
supply adapters.
Only entries supported by both selected factions appear in the shop. Existing
campaigns gain newly available definitions when resumed; their map, purchases,
and pending battle snapshots stay intact. New campaigns can also generate their
capture rewards and fortified nodes.

Game packs can still add specialized troops, defenses, and veteran teams through
`ConquestBattle.ini` and map/spawn adapters, described below.

`TerritoryOnly=yes` makes a StartingDefenses perk a node improvement. Select an
owned node before opening the shop. Building defenses does not use an army's
action. New campaigns can generate nodes with these improvements, including
neutral nodes. The defenses apply to that node's defender only, remain after a
battle, and pass to its captor. They do not follow an attacking army or grant
permanent faction rewards. Battle inventories and autobattle strength are
committed when the attack starts, including when the defender is neutral.

`PurchaseCost=0` disables buying that perk. Nonlocal perks can still be capture
rewards; local defenses can still appear on generated nodes. Existing campaigns keep saved definitions and layout; resume adds newly available
definitions without replacing existing ones. Start a new campaign to generate
new reward/defense nodes.

## TS and YR integration

Both engines accept map objects, scripts, task forces, and teams, but their
country lists, unit IDs, extensions, and valid placements differ. Declare `Games`
explicitly. Use separate adapters for TS and YR; do not reuse a mod's unit roster
in another game. The client never loads rules from another installed game.

`Player@N:` and `AI@N:` section prefixes select an internal side/country index,
after the client's `InternalSideIndices` mapping. Prefixes are removed before
writing the map. A section also requires that participant to own the committed
perk. This allows GDI/Nod or Allied/Soviet/Yuri grants in the same adapter without
giving both players the same faction's units. Include every side named by a
definition's `Sides` restriction. The perk is offered only when both campaign
factions meet that restriction.

Conquest assigns distinct starting waypoints itself. `{PlayerSpawn}` and
`{AISpawn}` refer to `SpawnN` aliases for those waypoints, rather than assuming
that a human always starts at waypoint zero. See the
[TS spawner's Spawn aliases](https://github.com/CnCNet/ts-patches/blob/master/src/spawner/spawner.asm)
and the client lobby's existing SpawnX map-object handling in
`ManipulateStartingLocations`.

For example, this **map-adapter fragment** places a stock TS Nod buggy four
cells away from country 1's base. Verify the country's index and the `BGGY` ID in
the actual pack before enabling it:

```ini
[Player@1:Units]
CQGarrisonP={PlayerSpawn},{PlayerUnit:BGGY},256,{PlayerStartX:4},{PlayerStartY:0},64,Guard,None,0,-1,0,-1,1,0

[AI@1:Units]
CQGarrisonA={AISpawn},BGGY,256,{AIStartX:4},{AIStartY:0},64,Guard,None,0,-1,0,-1,1,0
```

A separate stock YR adapter can use a validated country's tank ID instead, such
as `MTNK` for an Allied country. Keep `{PlayerUnit:TYPE}` for the player's mobile
units: when isolated clones are enabled it resolves to the private player type,
so grants receive the player's researched stats without changing the enemy's
native unit. When cloning is disabled, native handicap profiles apply.

Preplaced structures use the engine's `[Structures]` records. For example,
this fragment places a stock TS Nod laser turret for a defending AI country 1:

```ini
[AI@1:Structures]
CQDefenseA={AISpawn},NALASR,256,{AIStartX:-4},{AIStartY:4},64,None,1,0,1,0,0,None,None,None,0,0
```

Supply equivalent player/faction sections. YR requires its own building IDs,
such as a pack's Allied pillbox or Soviet sentry gun. TS component towers require
the tower and appropriate component configuration; placing an upgrade type as a
standalone building is insufficient. Powered defenses need working power at
battle start. Keep grants clear of the starting MCV and other starting units.

Coordinate substitutions only calculate offsets; they cannot determine terrain
passability, slope, occupancy, building footprints, or whether a cell lies inside
the playable area. Validate every allowed start in the pack's campaign map pool.
Use engine scripts/reinforcements when direct placement is unsuitable. Reserve
unique IDs across maps and all grant adapters. For a fixed object list, use
`MaximumLevel=1`; higher levels require a template that actually consumes the
level, for example a task force count using `{PlayerLevel}` or `{AILevel}`.

## Starting upgraded units

Research stat upgrades already affect starting native units through the TS/YR
handicap adapter. Starting **veterancy** is separate: a game pack can create
veteran/elite reinforcement teams using the engine's native team definitions,
including `VeteranLevel`, task forces, scripts, and a one-shot start trigger.
Conquest preserves those fields and substitutes each participant's base and
committed level. It does not apply a global rank switch that promotes the enemy's
units as well. Validate that the selected unit is trainable and the engine/mod
supports the chosen rank. See the
[TS team definitions](https://github.com/Vinifera-Developers/Tiberian-Sun-INIs/blob/master/AIFS.INI)
for native `VeteranLevel` examples.

The existing **Veteran production** perk is a different benefit: on YR/Ares it
uses country veteran-unit lists for the configured isolated clone roster. It
does not automatically promote preplaced units. TS does not advertise that YR
country feature; use a TS-specific starting-team adapter instead.

These fragments document adapter output, not a complete map pack. Engine play
tests are required before enabling them. The regression harness verifies TS/YR
configuration gates, faction selection, coordinate substitution, clone aliases,
and committed inventories; it does not simulate engine placement.
