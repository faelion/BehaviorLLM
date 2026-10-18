# Arena Brawler

A one-on-one bare-knuckle fight against the machine in a round arena, first to 3 rounds. You are blue; the machine is red. Both fighters are Quaternius' mannequin with motion-captured animations.

Like Dodgeball, this sample gives a decision model **the controls rather than a list of behaviours**. The machine holds a direction, punches, blocks and rolls, about seven times a second, and the fight comes out of the timing. It is the harder test of the two: a jab lands 0.15 s after the button.

## Running it

1. In `BehaviorLLM > Model Manager`, download **LiquidAI d1 3B** and install the server. The scene uses d1; at about 0.12 s a decision it is the only model fast enough to box.
2. Open `Scenes/ArenaBrawler.unity` and press Play. The server starts on its own.
3. To rebuild the scene: `BehaviorLLM > Samples > Build Arena Brawler Scene`.

| | Keyboard and mouse | Gamepad |
|---|---|---|
| Move | WASD | left stick |
| Jab: fast, short, blocked without harm | J or left click | X |
| Hook: slow, long, breaks a block, punishable when it misses | K or right click | Y |
| Block (hold) | L or Shift | RB or LT |
| Dash: a roll, untouchable for most of it | Space, the way you are moving | A |
| Restart / autopilot | R / T | |

The ring under a fighter shows what it is doing: yellow while a jab winds up, red while a hook winds up, white as a punch lands, blue while blocking, purple while stunned. A hook's 0.45 s wind-up is long enough to see and answer; a jab's 0.15 s mostly is not. A jab that lands during the other fighter's wind-up interrupts it and does a quarter more damage.

**T** hands your side to a scripted autopilot that closes in, jabs, hooks now and then, punishes a missed hook, and answers a punch it sees a quarter of a second after it starts: it rolls away from a hook and blocks a jab. It is there to watch the machine fight, and to test it without a person.

## What the machine has

- **Its controls** (`BrawlerMachinePad`): `Move` toward you, away, or circling left or right, `Stand`, `Jab`, `Hook`, `Block`, and `Evade`. A direction or a block stays held until its next decision; a punch or an evade happens once, and a punch pressed while the fighter is still busy comes out as soon as it is free.
- **One constraint the human does not have:** `Evade` is a roll the game aims, sideways out of the punch's line and toward the roomier side, where your dash goes wherever your stick points. With four dash directions on the menu, LiquidAI d1 picked the first one listed, straight into the punch, for most incoming hooks.
- **What it sees** (`FightObservationModule`): a punch coming at it first, with when it lands, when that is relative to its own next input, and whether it is inside the punch's reach; then the distance in terms of its own reach; then what each fighter is doing. Your punches are called a "quick strike" and a "slow swing", never "jab" and "hook", for a reason given below.
- **An interrupt** the moment you start a punch or a roll, which cancels whatever decision is in flight and asks again with the wind-up in view.

The caption above the machine shows its last input and the probability the model gave it; the panel at the bottom right shows its decision time, its reaction time to your hooks, and how many of your punches landed. Every match is written to `match_<time>.csv` under `Application.persistentDataPath/BehaviorLLM/ArenaBrawler`.

## Fairness

`Data/BrawlerRules.asset` holds every number for both fighters, and one setting for the machine alone: **Machine Reaction Delay**. At 0 the machine plays at the speed of the model, about 0.12 s from situation to input with d1 on a mid-range GPU, faster than a person reacts. Around 0.1 to 0.15 its total reaction time becomes roughly human, so a match measures choices rather than speed.

## What was measured

Against the autopilot, LiquidAI d1 at a 117 ms median per decision (llama-server b11630, RX 6650 XT), over one 95-second match the machine won 3 to 0:

| Situation | What the machine chose |
|---|---|
| A quick strike coming, inside its reach | Block, 30 of 30 |
| Its jab reaches, nothing happening | Jab 111, Move Toward 41, Hook 39, Evade 30 of 256 |
| Out of reach | Move Toward, 246 of 268 |
| The opponent open after a miss | Jab 31, Hook 11 of 45 |
| A slow swing coming, inside its reach | Evade 13, Block 11, Hook 7 of 31 |

The guide took the most work, measured by replaying recorded fight states through the model with variants of the text and scoring whether each choice fit the situation:

- **The model leans toward the options the text names most.** A guide that said "hook" five times and "dash" once made it hook into 38 of 40 incoming hooks. The final guide names every button exactly once.
- **It leans toward an option that shares a word with the situation.** With "the opponent's hook" in the text it answered `Hook`. Calling the opponent's punches a "quick strike" and a "slow swing" removed that.
- **It takes the first of several near-identical options.** That is what the `Evade` constraint above works around.

Together these took its share of choices that fit the situation from 0.55 to 0.70, and the match against the autopilot from 0–3 to 3–0. Hooks are still its weak spot: it blocks them about a third of the time, and a block does not stop a hook.

## Files

| | |
|---|---|
| `Scripts/BrawlerArena.cs` | The whole fight as plain C#: states, timings, reach, blocks, guard breaks, rolls, the input buffer. Unit-tested in `Tests/`. |
| `Scripts/BrawlerMatch.cs` | Runs a match: your controls, the autopilot, rounds, the HUD and the report. |
| `Scripts/BrawlerFighterView.cs` | Draws a fighter: animation states and speeds matched to the move timings, the telegraph ring, and a primitive stand-in when the art is missing. |
| `Scripts/BrawlerMachinePad.cs` | The machine's gamepad, its reaction delay, and when Evade is on the menu. |
| `Scripts/FightObservationModule.cs` | What the machine is told. |
| `Scripts/Editor/ArenaBrawlerSceneBuilder.cs` | Builds the scene, the animator controller and the config assets. The guide text and the reasons behind it are here. |

## The art

Quaternius' [Universal Animation Library](https://quaternius.itch.io/universal-animation-library) and [Universal Animation Library 2](https://quaternius.itch.io/universal-animation-library-2), both CC0: the female mannequin, and 18 of the libraries' clips copied out as Unity animation assets so the sample does not ship the 42 MB of source files. The licence is in `Art/LICENSES`. Delete `Art/` and the builder makes primitive stand-ins instead; the fight plays the same.
