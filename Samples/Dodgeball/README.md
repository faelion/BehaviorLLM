# Dodgeball

One on one against the machine, seen from above on a wooden court in a full arena. You are blue on the left half; the machine is red on the right, and the interface calls it by its model's name. A hit scores for the thrower, a catch for the catcher, first to 7.

This sample asks what a decision model can do when it is given **the controls rather than a list of behaviours**. The machine has no "fetch the ball" or "throw at the human". It presses a virtual gamepad, as you do, about six times a second, and has to get to a ball, back off behind its throwing line, aim, and dodge or catch what you throw, all by itself.

## Running it

1. In `BehaviorLLM > Model Manager`, download **LiquidAI d1 3B** and install the server. The scene uses d1; it is the only model fast enough to play this at about 0.15 s a decision.
2. Open `Scenes/Dodgeball.unity` and press Play. The server starts on its own.
3. To rebuild the scene: `BehaviorLLM > Samples > Build Dodgeball Scene`.

| | Keyboard and mouse | Gamepad |
|---|---|---|
| Move | WASD | left stick |
| Aim | the mouse: you face it | right stick |
| Throw | hold left click to charge, release to throw; or hold J to throw straight at the machine | hold RT, or hold A |
| Catch (opens for 0.22 s: time it) | right click or K | B |
| Dodge | Space or Shift, the way you are moving | RB |
| Restart / autopilot | R / T | |

**Throwing is charged.** Hold the button and a dashed line grows from your hand along the path the ball will take, bounces off the walls included, as far as it will travel if you let go now; an arc fills around you. While charging you move at under half speed and cannot dodge or catch. A ball travels 6 m released at once and 26 m fully charged, at 16.5 m/s either way, and stays dangerous until that distance runs out, however many walls it bounces off. Then it drops and rolls, harmless. Live and loose balls knock into each other like billiard balls.

**You carry up to two balls.** Throw one and the other is in your throwing hand at once. **Catching works with balls in hand**, but opening the catch drops them, and a dropped ball cannot be picked up again for 1.2 s: a ring fills around it until it can. The catch is open for 0.22 s and then needs 1.8 s to recover.

You can only throw from behind your dashed throwing line, 3 m back from the center: the coloured end of your half. The strip in front of it flashes red while you hold a ball there, a no-throw sign pops over you, and a throw released there is refused. Without it both players rush the balls on the center line and whoever touches one first throws from two metres away, which nobody can react to.

**T** hands your side to a scripted autopilot that fetches, throws straight at the machine, and dodges every on-target throw a quarter of a second after it leaves the hand. It is there to watch the machine play, and to test it without a person.

## What the machine has

- **Its controls** (`DodgeballMachinePad`): `Move` in eight directions in its own frame (Forward is toward the center line, Up toward the top wall), `Stand`, `Throw` with one of eight aims, `Catch`, and `Dodge` up, down or back. A direction stays held until its next decision; a throw, catch or dodge happens once.
- **Eight ways to throw**: `AtOpponent`, `AheadOfOpponent` (where you will be if you keep running), `OffsetUp1`, `OffsetUp2`, `OffsetDown1`, `OffsetDown2` (a metre or two above or below you), and `BankTopWall` / `BankBottomWall` (off a side wall, aimed at your reflection in it). The model chooses where; the pad holds the button exactly as long as the throw needs to get there with 4 m to spare, re-aiming while it charges, and the machine pays the same slow, defenceless wind-up you do. Its path is drawn in red while it charges.
- **What the game already knows is on the menu, not in the prompt.** Throw is offered only while it can throw and is not already charging. Dodge only while it is ready and a ball will hit it. Catch only while it is ready and a catch pressed now, landing one decision later, would be open as the ball arrives.
- **What it sees** (`CourtObservationModule`): on every decision while it holds a ball in its no-throw zone, a first line that says so (`THROWING LINE: you are holding 2 balls but can only throw from behind your throwing line, 1.2 m back from where you stand. Move back to reach it.`); every incoming ball, following its bounces, with when it arrives and whether it will hit (`INCOMING: a ball thrown at you arrives in 0.42 s, 0.27 s after your next input lands, and will hit you.`), where it stands and how many balls it holds, where you are, what you are doing, when your dodge and catch are ready again, how fast you are running and how far a throw has to lead you (`running up at 4.5 m/s, so throw ahead of them, 2.4 m up`), and, while it is empty-handed, every free ball, with how long a dropped one stays locked. The arrival is also given relative to when its next input will land, because a catch pressed now lands one decision later.
- **An interrupt** the moment you throw. The decision in flight, made before the ball was in the air, is thrown away when it returns, and the machine asks again at once (`Decision Model Interrupt: Discard Answer`). It is not cancelled: the server would finish reading it anyway, and a cancelled decision made the fresh answer wait behind it, about 370 ms after the throw instead of about 200-270 ms.

On screen, a chip under each player shows the balls in hand and the dodge and catch, which sweep back like a cooldown dial while they recover. Over the machine, a bubble shows the input it just pressed as an icon (an arrow for a run, chevrons for a dodge, a hand for a catch, a ball for a throw), ringed by the probability the model gave it, with its decision time beside it. The end-of-match panel adds what came of your throws. Every match is written to `match_<time>.csv` under `Application.persistentDataPath/BehaviorLLM/Dodgeball`.

## Fairness

`Data/DodgeballRules.asset` holds every rule for both sides, and one setting for the machine alone: **Machine Reaction Delay**. At 0 it plays at the speed of the model, about 0.15 s from situation to input with d1 on a mid-range GPU, faster than a person reacts. Around 0.15 its total reaction time becomes roughly human, so a match measures choices rather than speed.

## What was measured

With the charged throw, two-ball hands and bouncing balls, LiquidAI d1 at a 140 ms median decision (llama-server b11630, RX 6650 XT):

- It fetches balls, backs behind its line with them and throws: against an idle opponent it won 7 to 0 in under a minute.
- Against the autopilot it lost 3 to 7 and 5 to 7, answering a throw in about 200-270 ms. When the interrupt cancelled the decision in flight, the fresh answer took about 370 ms and it lost 0 to 7 twice; with no interrupt at all it lost 3 to 7 twice, but its first input after a throw was then the stale decision. It usually chooses the right answer, Catch, but the autopilot throws from its line at a machine waiting near the center, so its balls arrive in a quarter to half a second and many land first.
- Before the rework (instant throws at 15 m/s, one ball per hand) it beat the autopilot 7 to 5, getting out of the way of 16 of 22 throws on target. The faster ball and the shorter catch window are what turned that round; slowing `Throw Speed` back toward 15 in the rules asset gives it the time again.

Getting there taught two things worth knowing before you write your own guide for a decision model. Both were measured by replaying recorded court states through the model with variants of the text:

- **A word in the guide pulls the model to the option that contains it.** The first guide said "after picking up a ball near the center, move back first", and the machine answered `Move Back` to 26 of 27 balls about to hit it. With the word gone it caught or dodged 26 of 27, and went for the nearest ball 83% of the time instead of 41%.
- **Descriptions replace option names for d1.** Its template shows an option's description instead of its key, so describing `Move` once made all eight directions read the same, and the model could no longer tell them apart. The options carry no descriptions here.

- **Offer a button only when it can work.** With Catch on the menu and named in the text whenever a ball was in the air, d1 pressed it for 57 of 60 balls coming at it, however far off, and for all 20 that would miss. Offering Catch only when it would be open in time, and Dodge only when the ball will hit, took its share of good answers from 0.64 to 0.94 over 160 generated situations, with the owner's guide and the opponent's cooldowns in both (`Logs/Bench/coords_tune.py`). The same replays tried coordinates: every object's position and velocity added to the text changed nothing (0.63), and coordinates instead of the relative sentences fell to 0.47, fetching the nearest ball 8 times in 20 instead of 16 and backing off to throw 2 times in 20 instead of 20. Live, against the autopilot's throws from 3 to 5 m, it chooses Catch or Dodge in time but the answer, about 200 ms after the throw, often lands after the ball: 3 to 7 and 4 to 7.
- **Leave out what it should not act on.** With two balls in hand and the throwing line ahead, d1 was stuck: replayed on 40 such states generated by `Describe`, it moved back in 0 of them. "Catch ready" was in the text, and it caught, which drops the balls, 36 times; with Catch gone it walked forward, toward the free balls the text still listed. Mentioning and offering Catch and Dodge only while a ball is coming, and leaving the free balls out while it holds one, took it to 40 of 40. The same replays chose the throw's aim: it takes the first aim listed, `AtOpponent`, unless the text says otherwise; "so throw ahead of them, 2.4 m up" on a running opponent got it to lead 5 throws in 20 rather than none. The replays are `Logs/Bench/zone_tune.py` and `fetch_tune.py` in the development repository.
- **Say what is wrong where it cannot be missed.** The machine kept walking to the center line with a ball it could not throw. Its observation said "You are holding a ball", and the rule was in the guide. A line of its own at the top, `THROWING LINE: you are holding a ball but can only throw from behind your throwing line, 1.4 m back from where you stand. Move back to reach it.`, took it from 5 to 34 moves back out of 40 recorded states. "Move back" is safe there because the line only appears while it applies.

## Look and feel

Everything is drawn from sprites `DodgeballArt` and `DodgeballIcons` paint when the scene is built (`Data/Art/`), so there is nothing to download or license: the interface's icons, the seven-segment score, keycaps and mice included. `DodgeballView` gives every event a response: the throwing hand swings and the ball lifts and trails in the thrower's colour; a hit freezes the game for 80 ms, shakes the camera, bursts sparks and glances the ball off; a catch slows time briefly; a dodge leaves afterimages; and the scorer's supporters jump. The sounds are synthesised by `DodgeballSound`. Shake, hit-stop, slow motion and volume are on the View component. None of it touches the rules: the court is stepped by the match, and the view reads it afterwards.

## Files

| | |
|---|---|
| `Scripts/DodgeballCourt.cs` | The whole game as plain C#: players, balls, throws, catches, dodges, scoring. Unit-tested in `Tests/`. |
| `Scripts/DodgeballMatch.cs` | Runs a match: your controls and aim, the autopilot, points, the events the view and HUD listen to, and the report. |
| `Scripts/DodgeballView.cs` | Draws the court and all the feedback. |
| `Scripts/DodgeballHud.cs` | The interface, built in code at start. |
| `Scripts/DodgeballSound.cs` | Synthesised sound effects. |
| `Scripts/DodgeballMachinePad.cs` | The machine's gamepad, its reaction delay, and which buttons are on the menu. |
| `Scripts/CourtObservationModule.cs` | What the machine is told. |
| `Scripts/Editor/DodgeballArt.cs` | Paints the court, players and balls, and makes the materials. |
| `Scripts/Editor/DodgeballIcons.cs` | Paints the interface's icons and plates. |
| `Scripts/Editor/DodgeballSceneBuilder.cs` | Builds the scene and its config assets. The guide text and the reasons behind it are here. |
