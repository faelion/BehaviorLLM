# BehaviorLLM samples

Four runnable samples ship inside the package. Each is self-contained: its scenes, scripts, config
assets and art all live in its own folder, with the art licence beside the art, and none
references anything outside itself and the package.

| Sample | What it shows | Animated bodies |
|---|---|---|
| [StealthGuard](StealthGuard/README.md) | Three guards patrolling a compound. Cone vision with real line of sight, reflex interrupts, per-guard argument lists, and the lesson that a condition the game already knows should gate the action menu rather than be written into the prompt. | 3 guards + player |
| [PrisonYard](PrisonYard/README.md) | Eight decision makers on one server: three guards, four prisoners and a warden. Custom observation modules (a radio and a status board), decision makers changing each other's world, and a decision maker with no body at all. | 3 guards + 4 prisoners; warden has no body |
| [Dodgeball](Dodgeball/README.md) | You against the machine, top-down in flat shapes. The machine is given a gamepad instead of behaviours: it moves, throws, catches and dodges about six times a second, and reacts to your throws as they leave your hand. | 2 discs, no art |
| [ArenaBrawler](ArenaBrawler/README.md) | You against the machine in a fist fight with motion-captured animations. Punch timing, blocks and rolls at about seven decisions a second, and what a decision model's guide has to look like for that to work. | 2 fighters |

Build any scene from its menu item under `BehaviorLLM > Samples`; each regenerates its scene, animator
controllers and dressing from source.

## Requirements

The package core has no package dependencies. The samples have two:

- `com.unity.ai.navigation` for movement, in StealthGuard and PrisonYard
- `com.unity.inputsystem` for the player, in all four

Every sample assembly is gated on the packages it needs being present, so a project without them still
installs and uses BehaviorLLM normally; those samples are simply not compiled. Dodgeball and
ArenaBrawler need only the Input System.

## Removing them

The samples exist to be read once. `BehaviorLLM > Readme` measures each one and has a
button for deleting it, and a button for deleting all of them along with the readme itself. Nothing in the
package references them, so removing them cannot break a project.

Those buttons delete in place, which works where the package is writable: copied into your own
`Assets` folder, or embedded in `Packages/`. Installed from the git URL it is immutable and sits
under `Library/PackageCache`, which Unity rebuilds from your manifest, so a deletion there would
come straight back. The page says so and shows the sizes anyway; to drop the samples for good,
remove the dependency from `Packages/manifest.json`.
