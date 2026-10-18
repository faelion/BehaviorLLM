# Dodgeball

One on one against the machine, seen from above, drawn in flat shapes. You are blue on the left half; the machine is red on the right. A hit scores for the thrower, a catch for the catcher, first to 7.

This sample asks what a decision model can do when it is given **the controls rather than a list of behaviours**. The machine has no "fetch the ball" or "throw at the human". It presses a virtual gamepad, as you do, about six times a second, and has to get to a ball, back off behind its throwing line, aim, and dodge or catch what you throw, all by itself.

## Running it

1. In `BehaviorLLM > Model Manager`, download **LiquidAI d1 3B** and install the server. The scene uses d1; it is the only model fast enough to play this at about 0.15 s a decision.
2. Open `Scenes/Dodgeball.unity` and press Play. The server starts on its own.
3. To rebuild the scene: `BehaviorLLM > Samples > Build Dodgeball Scene`.

| | Keyboard and mouse | Gamepad |
|---|---|---|
| Move | WASD | left stick |
| Throw | left click on a spot, or J at the machine | A |
| Catch (opens for 0.35 s: time it) | right click or K | B |
| Dodge | Space or Shift, the way you are moving | RB |
| Restart / autopilot | R / T | |

You can only throw from behind your dashed throwing line, 3 m back from the center. Without it both players rush the balls on the center line and whoever touches one first throws from two metres away, which nobody can react to.

**T** hands your side to a scripted autopilot that fetches, throws straight at the machine, and dodges every on-target throw a quarter of a second after it leaves the hand. It is there to watch the machine play, and to test it without a person.

## What the machine has

- **Its controls** (`DodgeballMachinePad`): `Move` in eight directions in its own frame (Forward is toward the center line, Up toward the top wall), `Stand`, `Throw` at you or ahead of you up or down, `Catch`, and `Dodge` up, down or back. A direction stays held until its next decision; a throw, catch or dodge happens once.
- **What the game already knows is on the menu, not in the prompt.** Throw is offered only while it holds a ball behind its line, Catch only while empty-handed and ready, Dodge only while ready.
- **What it sees** (`CourtObservationModule`): every incoming ball with when it arrives and whether it will hit (`INCOMING: a ball thrown at you arrives in 0.42 s, 0.27 s after your next input lands, and will hit you.`), where it stands, where you are and what you are doing, and every free ball. The arrival is also given relative to when its next input will land, because a catch pressed now lands one decision later.
- **An interrupt** the moment you throw, which cancels whatever decision is in flight and asks again with the ball in the air.

On screen, the caption above the machine shows its last input and the probability the model gave it; the panel at the bottom right shows its decision time, its reaction time to your throws, and what came of them. Every match is written to `match_<time>.csv` under `Application.persistentDataPath/BehaviorLLM/Dodgeball`.

## Fairness

`Data/DodgeballRules.asset` holds every rule for both sides, and one setting for the machine alone: **Machine Reaction Delay**. At 0 it plays at the speed of the model, about 0.15 s from situation to input with d1 on a mid-range GPU, faster than a person reacts. Around 0.15 its total reaction time becomes roughly human, so a match measures choices rather than speed.

## What was measured

Against the autopilot, LiquidAI d1 at 0.15 s a decision (llama-server b11630, RX 6650 XT):

- It goes for free balls, backs behind its line before throwing, and throws whenever it can.
- With a ball about to hit it, it catches or dodges about half the time (7 of 14 in the last run) and otherwise mostly steps up or down; offline, on recorded states, the final guide caught or dodged 26 of 27.
- It loses, 3 to 7 in the run behind this file: the autopilot never misses a dodge, which a person does.

Getting there taught two things worth knowing before you write your own guide for a decision model. Both were measured by replaying recorded court states through the model with variants of the text:

- **A word in the guide pulls the model to the option that contains it.** The first guide said "after picking up a ball near the center, move back first", and the machine answered `Move Back` to 26 of 27 balls about to hit it. With the word gone it caught or dodged 26 of 27, and went for the nearest ball 83% of the time instead of 41%.
- **Descriptions replace option names for d1.** Its template shows an option's description instead of its key, so describing `Move` once made all eight directions read the same, and the model could no longer tell them apart. The options carry no descriptions here.

## Files

| | |
|---|---|
| `Scripts/DodgeballCourt.cs` | The whole game as plain C#: players, balls, throws, catches, dodges, scoring. Unit-tested in `Tests/`. |
| `Scripts/DodgeballMatch.cs` | Runs a match: your controls, the autopilot, points, drawing, the HUD and the report. |
| `Scripts/DodgeballMachinePad.cs` | The machine's gamepad, its reaction delay, and which buttons are on the menu. |
| `Scripts/CourtObservationModule.cs` | What the machine is told. |
| `Scripts/Editor/DodgeballSceneBuilder.cs` | Builds the scene and its config assets. The guide text and the reasons behind it are here. |
