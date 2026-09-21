# StealthGuard

Three guards on a night shift, each driven by its own decision maker against one shared local model. The whole scene, art included, is generated from one menu item.

This is the sample to open first: it is small enough to read in a sitting, and it demonstrates the one lesson that changed the package's design.

## Running it

1. Download a model. `BehaviorLLM > Model Manager`, or drop a GGUF into `Assets/StreamingAssets/models/`. The scene expects `granite-4.1-3b-Q4_K_M.gguf`; to use another, assign a different model config asset to `LLMManager > Model Config` and to the guard's `BehaviorLLMClient`.
2. Make `llama-server` reachable. Either install it with `BehaviorLLM > Model Manager (Server tab)`, or install llama.cpp yourself (`winget install llama.cpp`) and put the executable on your `PATH`. `LLMManager > Executable Name` also accepts an absolute path.
3. Open `Scenes/StealthGuard.unity` and press Play. The server starts on its own and takes a few seconds to load the model; the first decision follows shortly after.
4. **WASD** to move the intruder. **Hold Space** near the guard to attack it.

## What you are trying to do

Steal the three glowing crates marked **STEAL** and carry them out through the green **EXIT** gate in the south fence. The gate's light is red until you are carrying all three.

Each crate sits in a different guard's half of the compound, so taking all three means crossing all three patrol beats. If a guard decides to **Chase** and reaches you, everything you are carrying goes back where it came from - the only consequence in the sample, and a mild one on purpose: the thing worth watching is the decision, not a game-over screen.

**Taking a crate radios the nearest guard.** The report names the closest place that guard can be sent to - "Supplies were stolen near NorthStore about 4s ago" - and arrives as its own `--- Radio ---` topic in the STATE block, with an interrupt so the guard re-thinks straight away instead of finishing its decision interval. Only the closest guard is told, so a theft in the north-west does not summon the whole compound. What that guard does about it is the model's business: it may go and look, and it may decide that whatever it can already see matters more.

That is the shape of the answer whenever a game knows something the model should act on: an observation module (`TheftReportModule`), not a rewritten prompt. Turn off **Report Thefts** on `_Objective` to watch how long a theft goes unnoticed when the guards have only their eyes.

### What you can see them see

Each guard drags a pale fan across the ground. It is not a cone - a cone is a lie the moment a wall gets in the way. `GuardVisionCone` casts the same rays the vision module casts, against the same occluder layers, and builds a shape whose edge stops where each ray stopped. It reads the guard's `PerceptionConfig` rather than carrying its own range and angle, so the drawing cannot drift from the sensing. It turns amber while a guard is investigating and red while it is chasing.

Walk behind the metal walls and watch the fan cut off at the corner. That is the sample's line-of-sight claim, on screen, rather than in this file.

`StealthObjective`, `StealthLoot` and `GuardVisionCone` are the whole of it, and they touch nothing in the package. Delete them and the scene still demonstrates everything it demonstrated before; there is just no longer a reason to walk anywhere in particular.

To rebuild the scene from scratch: `Tools > StealthGuard > Build Scene`. That one menu item also configures the character rig, writes the animator controller and dresses the compound, so the scene never has to be hand-edited.

## The art

Two CC0 packs by Kenney, both under this sample's `Art/` folder, with the licence files in `Art/LICENSES`:

| Pack | Used for |
|---|---|
| [Animated Characters 2](https://kenney.nl/assets/animated-characters-2) | The guard and the intruder. One rig, two skins. |
| [Survival Kit](https://kenney.nl/assets/survival-kit) | The compound: metal walls, perimeter fence, crates, barrels, tents, trees. |

Everything lives under this sample's own `Art/` folder, licences included, so the folder can be moved anywhere as one piece. Delete `Art/` and the sample still builds; the builder falls back to coloured primitives, which is how it looked before the art arrived.

Everything the pack contributes is decoration and carries no collider. The four sight-blocking walls are still invisible boxes at the exact positions the sample was tuned for, and the NavMesh is built from **physics colliders** rather than render meshes so that a crate can never carve a hole in the guard's walkable area. If you dress the scene further, keep that split: art under `_Environment/Dressing`, gameplay volumes as colliders.

## What to watch

- **The three guards patrol** between the blue markers while nothing is happening, each choosing its own route each time - and each choosing from a *different* list. `GuardArgumentOptions.patrolBeat` gives every guard its own beat, because argument options are built per decision maker: the same `Patrol` action means different places to different guards. Left unset, all three would keep independently picking the same route and standing in one corner.
- **They have different personas and behave differently for it.** They also have different patrol vocabularies and observations, so this scene does not isolate the effect of persona alone.
- **Walk into its cone and it reacts immediately.** The vision module raises an interrupt when a previously unseen object enters view, so the guard does not wait for its next scheduled tick.
- **Hide behind a wall and it loses you.** Cone vision raycasts against the walls' layer, so line of sight is real.
- **Attack it below half health and the action menu changes.** `Chase` disappears, `Retreat` appears, and the guard withdraws to a green safe zone. The prompt did not change: the *available actions* did.

The console shows each decision as `[DecisionMaker] Answer: {"action":...,"arg":...}` followed by the dispatch. Turn on **Log Prompts** in `Data/StealthGuardDecisions.asset` to see the whole prompt as well.

## The lesson this sample exists for

The obvious way to express "do not chase while badly hurt" is a sentence in the action config's Model Instructions. It does not work. Measured across three models on 16 labelled scenarios, that two-condition rule was ignored in 15 of 18 cases, and rewording it to state the exception first made the larger model *worse*.

Removing `Chase` from the menu instead took the smallest model from 0 to 5 correct out of 6. The JSON Schema is enforced while the model samples, so an action that is not in it cannot be produced; the guide is only a suggestion.

`GuardAvailability` is where that happens, and it is nine lines of logic. The rule of thumb it demonstrates: **if the game can already answer the question, do not ask the model.** Untick `Gate Chase On Health` on the guard to watch the failure for yourself.

## How the pieces fit

| Component | Role |
|---|---|
| `DecisionMaker` | The loop. Composes observations, prompts, parses, dispatches. Tuned by `Data/StealthGuardDecisions.asset`. |
| `BehaviorLLMClient` | Talks to the local server. Takes sampling from the model config. |
| `ModularVisionModule` | Cone vision with wall occlusion, and the interrupt that makes the guard reactive. |
| `TheftReportModule` | The radio. An observation module, so a theft the game noticed reaches the model exactly the way sight does. |
| `GuardVisionCone` | Draws the real visibility polygon on the ground from the same rays and the same config. |
| `BasicMemory` | The last few executed actions, so the guard knows what it was doing. |
| `LLMContextObject` + `SelfObservationModule` | Reports the guard's own health and activity into the prompt. |
| `GuardArgumentOptions` | Derives the valid arguments from the markers in the scene, so the model cannot name a place that does not exist. `patrolBeat` narrows the routes per guard. |
| `GuardAvailability` | Decides which actions are on the menu this turn. |
| `GuardExecutor` | Turns a chosen action into NavMesh movement. Knows nothing about models or prompts. |
| `DecisionTelemetryRecorder` | Writes a report per run under `persistentDataPath/BehaviorLLM/Telemetry`. |

Add a marker to the scene with a `StealthGuardMarker` component and it joins the model's vocabulary on the next schema rebuild, with no code change.

## Measured on this scenario

**Historical offline matrix**, Granite 4.1 3B, Reactive, schema enabled: 16/16 valid, 13/16 expected actions, 278 ms p50 on a Radeon RX 6650 XT. This is not three-guard gameplay latency. The shipped live run `20260910_150511_stealthguard` has 306 decisions and a nearest-rank p50 of 700.073 ms. Reproduce the offline protocol with `Experiments~/run_matrix.py` from the package root.
