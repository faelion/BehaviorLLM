# PrisonYard

A small prison run by eight decision makers at once: three guards, four prisoners and a warden, all sharing one local model. The whole scene, art included, is generated from one menu item.

Where [StealthGuard](../StealthGuard/README.md) shows the loop with a single character, this shows what happens when several of them share a world and change it for each other. You are not a character here. You are the director: you provoke the prison with a few keys and watch it deal with the consequences.

## The layout

The yard is the hub. Every other section opens onto it through a five-metre corridor, so any two places are a short straight walk apart and most journeys cross the yard, which is where prisoners run into each other and into guards.

```
                 ControlRoom
      CellBlock   (warden)
          |
Infirmary-Yard-Cafeteria
          |
      Workshop
```

The yard has four doorways, one per wall; every other section has one. A gate sits in each doorway, and closing it both blocks the way and carves the opening out of the NavMesh, so a lockdown is a physical fact rather than a rule anyone has to respect.

## Running it

1. **Download a model.** `BehaviorLLM > Model Manager`, or drop a GGUF into `Assets/StreamingAssets/models/`. The scene expects `granite-4.1-3b-Q4_K_M.gguf`; to use another, edit `Data/Model_PrisonYard.asset`.
2. **Make `llama-server` reachable.** Install it with `BehaviorLLM > Model Manager (Server tab)`, or install llama.cpp yourself (`winget install llama.cpp`) and put it on your `PATH`.
3. **Open `Scenes/PrisonYard.unity` and press Play.** The server starts on its own and takes a few seconds to load the model. Eight decision makers then begin deciding on their own schedules.

To rebuild the scene from scratch: `BehaviorLLM > Samples > Build PrisonYard Scene`.

The characters use the shipped Unity prefabs, meshes and animation clips under
`Art/Characters/Native/`; no glTF importer is needed. The three guards and four prisoners have
animated bodies; the warden is deliberately a decision maker without a body. Controllers include
idle/walk/run, punch, interaction, pickup, hit and resting poses. The builder assigns textured
materials for the active Built-in or Universal render pipeline.

For a package installed under `Packages/`, generated scenes, configs and controllers go to
`Assets/BehaviorLLMSamples/PrisonYard/`. Rebuilding adds missing shipped art to an older copy
without overwriting existing files. If all art has been removed, the primitive fallback remains
available. The original `.glb` files are retained as authoring sources: maintainers can regenerate
native assets with `PrisonYardCharacterBake.Bake` in a writable Assets checkout that has glTFast.
Consumers do not need that dependency or regeneration step.

### The director's keys

| Key | What it does |
|---|---|
| `1` | The hothead attacks whoever is nearest. |
| `2` | Plants contraband on the escapee. |
| `3` | Injures the guard nearest the middle of the prison. |
| `4` | Cuts the lights: skips straight to lights out. |
| `5` | Releases every gate, undoing a lockdown by hand. |

Right-drag orbits the camera, the scroll wheel zooms. The panel on the left shows the time of day, who is in each section, what is locked, and the last few radio messages.

## What to watch

Press `1` and follow the radio panel. Two real runs, with nothing scripted:

```
Prisoner_03: fight in Workshop
Guard_01:    Yard looks clear
Control:     fight in Workshop handled by Guard_03
Warden:      Guard_01 to Workshop
```

```
Prisoner_03: fight in CellBlock
Control:     fight in CellBlock handled by Guard_02
Warden:      CellBlock is on lockdown
Warden:      lockdown lifted in CellBlock
```

Four decision makers, and not one line of code that tells any of them what to do. The chain works because every step is an ordinary action on one side and an ordinary observation on the other:

- **The fight** sets Prisoner_03's `activity` to `Fighting` and opens an incident. Nobody is notified.
- **Guard_02 finds out** because the incident posts to the radio, and its `RadioObservationModule` puts recent messages in its prompt. A message naming its own section also raises an interrupt, so it does not wait for its next scheduled tick.
- **The warden finds out** because it reads the status board, which counts what is in every section. It has no eyes and no body.
- **The lockdown** shuts a gate, which carves the doorway out of the NavMesh *and* removes that section from every prisoner's argument list. The prisoners are not asked to respect it; they cannot express going there.

Other things worth trying:

- **Press `2`, then watch a guard search the escapee.** `Search` only appears on a guard's menu when a prisoner is within reach and in view.
- **Press `3` twice.** Below half health the guard loses `Escort` and `Search` and gains `Retreat`, and walks to the infirmary. The prompt did not change; the available actions did.
- **Watch the prisoners between phases.** They walk to wherever the clock says they should be: cells, yard, cafeteria, workshop.
- **Watch the hothead when no guard is looking.** `Fight` is only ever on its menu while unwatched.

## The lesson this sample exists for

StealthGuard makes the case that a condition the game knows should gate the action menu rather than be written into the prompt. PrisonYard is what that looks like when there are eight of them:

| Rule | How it is enforced |
|---|---|
| Only a hothead starts fights | `PrisonerAvailability` checks a field. `Fight` is absent from every other prisoner's schema. |
| Only when no guard is watching | `PrisonerState.ObservedByGuard`, recomputed every frame by asking each guard's vision module. |
| Nobody enters a locked section | The section is not in `Wander`'s argument list. |
| A hurt guard does not wade in | `Escort` and `Search` are withheld below half health. |
| The warden does not lock down a quiet prison | `Lockdown` needs an open incident to appear. |

Every one of these could have been a sentence in the prompt, and every one would have been unreliable: measured across three models, a two-condition rule was ignored in 15 of 18 cases. None of them is a sentence here. **If the game can already answer the question, do not ask the model.**

Each provider has a toggle that turns its gating off, so you can watch the failure for yourself: untick `Gate On Being Watched` on a prisoner and it will start fights in front of the guards.

## How the pieces fit

| Component | Role |
|---|---|
| `PrisonRadio` | The message log actions write to and observation modules read from. This is the whole of how decision makers reach each other. |
| `RadioObservationModule` | A **custom sense**. Puts recent radio traffic in one character's prompt, and interrupts when a message names them or their section. |
| `PrisonStatusBoard` | Who is in each section, what is locked, what is going wrong. Read by the warden and by every availability provider. |
| `StatusBoardObservationModule` | A **custom sense**. The warden's only view of the prison. |
| `PrisonClock` | The daily routine. A full day is four real minutes. |
| `PrisonGate` | A door. Closing it carves the doorway out of the NavMesh. |
| `GuardAvailability` / `PrisonerAvailability` / `WardenAvailability` | Decide what is on each menu this turn. |
| `GuardArgumentOptions` / `PrisonerArgumentOptions` / `WardenArgumentOptions` | Decide what values each action may take, read from the scene. |
| `GuardExecutor` / `PrisonerExecutor` / `WardenExecutor` | Turn a chosen action into movement and consequences. None of them knows a model exists. |
| `PrisonRunReport` | Adds the prison's own numbers to the telemetry summary. |

The two observation modules are the reason this sample exists alongside StealthGuard. The package documents `IObservationModule` as its extension point for custom senses, and until now no sample implemented one. A radio and a status board are about as different from "a cone that sees objects" as a sense can be, and both are around a hundred lines.

## Three roles, three profiles, one server

| | Guards | Prisoners | Warden |
|---|---|---|---|
| Profile | Reactive | Reactive | **Deliberative** |
| Decides every | 2.5 s | 4 s | 12 s |
| Sees | 14 m cone, blocked by walls | 8 m sphere | nothing |
| Reads | vision, memory, radio, self | vision, memory, radio, self | status board, radio, memory |
| Slots | 0, 1, 2 | 3, 4, 5, 6 | 7 |

The warden is the argument that this package is not only for characters. It has no body, no vision and no NavMesh agent; it reads an aggregate and issues orders, which is what a difficulty director or an economy manager would do. It is also the only one that reasons before answering: its answers arrive as `{"reason":"Prisoners present in CellBlock, no lockdown needed.","action":"Observe","arg":""}`.

Each decision maker pins its own server slot, so the server keeps eight cached prompts alive instead of evicting one for the next.

## Measured on this scenario

A five-minute run on Granite 4.1 3B, Radeon RX 6650 XT, with the director provoking a fight, contraband, an injured guard and a blackout:

| | |
|---|---:|
| Decisions | 663 |
| Valid actions | **100%** (663 of 663) |
| Fallbacks, parse failures, backend errors | **0** |
| Prompt tokens served from cache | 67% |
| Median decision latency | 1,225 ms |
| Incidents opened / resolved | 2 / 2 |

Two honest notes on those numbers. **Latency is much higher than StealthGuard's 278 ms**, because eight decision makers queue on one server: the model is busy roughly two thirds of the time. It is still comfortably inside a 2.5-second tick, and the reason the sample stays responsive anyway is that a guard hearing its section on the radio interrupts rather than waiting. **The cache rate is pulled down by the warden** (59%), whose status block is large and changes every time; the guards, whose prompts are mostly static, sit at 70 to 73%.

Fights last twelve seconds on purpose. A guard decides every 2.5 seconds and the model takes about a second to answer, so a shorter brawl is over before anyone can see it and call it in; at five seconds the guards kept truthfully reporting "Workshop looks clear" just after it ended.


## The art

Two CC0 packs by Kay Lousberg, both under this sample's own `Art/` folder, with their licence files in `Art/LICENSES`, so the folder can be moved anywhere as one piece:

| Pack | Used for |
|---|---|
| [KayKit Adventurers](https://kaylousberg.itch.io/kaykit-adventurers) | The eight characters. Knights for the guards, and four different classes so the prisoners are told apart at a glance. |
| [KayKit Dungeon Remastered](https://kaylousberg.itch.io/kaykit-dungeon-remastered) | The building: walls, doorways, flagstones, torches, portcullis gates and the props in each room. |

The characters animate from a controller the builder generates: an idle/walk/run blend tree driven by the `NavMeshAgent`'s own speed, plus a one-shot per activity, so a guard that decides to `Search` visibly searches. `PrisonCharacterVisuals` is the only thing that reads the decision, and it reads it one-way: delete the component and the prison behaves exactly the same, it just stops showing you what it is doing.

Delete `Art/` and the sample still builds; the builder falls back to coloured capsules.

Six rooms cut from one texture atlas look like one room repeated, so each section gets its own **name board over its doorway**, its own **floor colour**, its own **torch colour** and its own furniture. The floor and torch colours are what carry from the sample's overhead camera; the furniture is what tells you what the room is for once you look closer. All of it lives in one `Styles` table next to the geometry table, and none of it can move a wall.

The dungeon pieces sit on a four-metre grid, which is why every room in the layout above is a multiple of four, and why the whole compound is a 64 x 64 m square of flagstones: the rooms lay their own tiled floors, and everything outside them - the yard's approaches, the corridors, the margin - is paved with the same pack. A stone wall runs the full perimeter, so the prison is a walled place rather than a set of buildings on an infinite plane, and nobody can walk off the map.

Props carry no colliders. That does **not** mean they are free: this sample bakes its NavMesh from render meshes, so a prop still blocks walking, which is why nothing is ever placed on a section centre. Sight is a separate question and is answered by colliders, which is why the perimeter has four invisible boxes on the occluder layer behind its stonework.

Scenery that must *not* block anything goes on the **`PrisonDecor` layer**, which the `NavMeshSurface` is told to skip. Two things need it: the pack's wood and grate floors, which are not closed surfaces and bake full of holes, and the room name boards, which are flat meshes hanging over each doorway. Both are laid over a plain tiled floor that is what actually carries the NavMesh.

## Two limits worth knowing

**Argument options follow world state.** BehaviorLLM refreshes dynamic argument providers every decision and checks their values again before dispatch. Lockdowns and changing reinforcement targets therefore require no manual schema invalidation. An answer that becomes invalid during inference is rejected or replaced with a currently valid fallback.

**An action carries one argument.** "Send Guard_03 to the Yard" needs two, so `Reinforce(Section)` takes the section and the warden's executor picks which guard goes. That is a deliberate consequence of the package's `{action, arg}` contract, not something this sample works around, and multi-argument actions are a future line for the package rather than a gap in the scenario.
