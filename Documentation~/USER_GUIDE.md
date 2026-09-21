# BehaviorLLM User Guide

This guide walks through giving a game object the ability to decide for itself with **BehaviorLLM**, from a running local model to a working NPC. The component that does the deciding is called a **Decision Maker**, because it is not limited to characters: it drives an enemy, a companion, or a game system such as a director.

## The concept

A decision maker has three layers:

1. **Backend**: how it talks to the local model (a `llama-server` process, over HTTP).
2. **Sensors**: observation modules that describe the game state as text.
3. **Actions**: a discrete set of things it can do, each bound to your code.

On every tick of the configured interval, or immediately on an interrupt, it composes the observations into a state block, sends it after a cached system prompt, receives one JSON object `{"action":"...","arg":"..."}` constrained by a JSON Schema, and invokes the bound handler.

## Where the settings live

No BehaviorLLM component carries tuning fields. Everything that changes behaviour lives in reusable ScriptableObject assets, so one edit retunes every object sharing an asset, and a project can keep as many variations as it likes without a prefab for each:

| Asset | Holds | Read by |
|---|---|---|
| **Decision Maker Config** | Decision interval, profile, reason and thinking budgets, structured output, prompt budget, console noise | `DecisionMaker` |
| **Server Config** | Address, transport, port, slots, launch flags, timeouts, retries, server log verbosity | `BehaviorLLMClient`, `BehaviorLLMServer` |
| **Model Config** | Model file, context size, GPU layers, sampling, measured behaviour | `BehaviorLLMClient`, `BehaviorLLMServer` |
| **Perception Config** | Vision shape, range, field of view, layers, scan rate, sight interrupts, memory capacity | `ModularVisionModule`, `BasicMemory` |
| **Blackboard Config** | Shared-note retention, staleness and observation caps | `BehaviorLLMBlackboard`, `BlackboardObservationModule` |
| **Settings** | Console noise in the Editor and in builds, telemetry master switch, diagnostics | the whole package |

The package ships presets under `Runtime/Defaults`, and adding a component in the Editor points it at the matching preset automatically, so a new scene works before you author anything. Assets inside a package are read-only, so duplicate a preset into your own project folder before editing it, or create a fresh one with *Create > BehaviorLLM > …*.

### The Settings asset is the odd one out

The domain assets above each tune one *domain* and are meant to exist in several variants: a Reactive preset and a Deliberative one, a perception profile per character type. **Settings** is different. There is one per project, it is loaded from a `Resources` folder rather than wired into a component, and it answers questions that have nothing to do with how any particular decision maker behaves:

| Setting | Default | What it decides |
|---|---|---|
| `editorLogLevel` | `Warnings` | How much the package prints in the Editor and in a development build. `Warnings` already includes anything a config asset asked for, such as `Log Prompts`; `Verbose` adds internal detail nobody asked for; `ErrorsOnly` silences the config switches too, which is how you quieten a project in one place. |
| `playerLogLevel` | `ErrorsOnly` | How much it prints in a shipped build. A released game should not write a console line every time a guard decides something. |
| `telemetryEnabled` | on | Master switch for `DecisionTelemetryRecorder`. Off, a recorder left in the scene records nothing and writes no files. |
| `telemetryInBuilds` | off | Whether telemetry may run outside the Editor. Turn it on for a playtest build you intend to collect numbers from. |
| `dumpAppliedSchema` | on | Whether the Editor writes `_last_applied_schema.json` to StreamingAssets for each outgoing request after availability filtering; an unconstrained request clears the file. With multiple decision makers this is the most recent writer; use Prompt Inspector to identify a specific exchange. |
| `warnOnBindingMismatch` | on | The startup warning when bindings and the Action Config disagree. Leave it on. |
| `warnOnModelProfileMismatch` | on | The startup warning when a profile runs against what the model was measured to do. |

The shipped default lives at `Resources/BehaviorLLM/Defaults/Settings.asset`. It works automatically in every scene. To override it, create *BehaviorLLM > Settings* in your project at `Assets/Resources/BehaviorLLMSettings.asset`. Lookup uses this project override first, then the shipped default, then transient field defaults if neither exists. Keep only one project override at that resource path. Domain configs remain independently assignable: duplicate any shipped preset into `Assets/BehaviorLLMConfigs/`, edit it, and assign it to the relevant components; objects sharing an asset share its values.

**It is a ceiling, not an override.** A decision maker whose config has `logPrompts` off stays quiet whatever the level is; what the level can do is silence one that has it on. That is what makes a single switch enough to quieten a whole project before a build, without visiting every config asset.

Everything the package prints goes through `BehaviorLLMLog`, so if you are wondering why a message stopped appearing, this asset is the first place to look.

---

To vary one object without affecting the others sharing an asset, clone it at runtime and apply the clone:

```csharp
DecisionMakerConfig mine = Instantiate(decisionMaker.Config);
mine.decisionInterval = 0.75f;
decisionMaker.ApplyConfig(mine);
```

---

## Step 1. Backend setup (once per scene)

### Option A: BehaviorLLM manages the server

1. Create an empty GameObject named `LLMManager` and add **`BehaviorLLMServer`**.
2. Open `BehaviorLLM > Model Manager`. The **Catalog** tab downloads; the **Installed** tab is where a downloaded file is put to use. Download one of the three measured models, switch to Installed and click **Set active in current scene**. That assigns the model's config asset to every `BehaviorLLMServer` and `BehaviorLLMClient` in the open scene (creating the asset first if none exists, under `Assets/BehaviorLLMConfigs/Models`) and writes the same choice to `Assets/StreamingAssets/behaviorllm_backend_config.json` for scenes that leave Model Config empty. Save the scene to keep it. The status strip at the top says which model the open scene will run and which the config file names, because the two can differ.
3. Open `BehaviorLLM > Model Manager (Server tab)` and click **Download Server** (Windows), or install llama.cpp with `winget install llama.cpp` / `brew install llama.cpp`. A `llama-server` on your `PATH` is found automatically when none is under StreamingAssets.
4. Check `BehaviorLLM > Model Manager (Config tab)` (executable path, port, context size, GPU layers). On AMD GPUs use the Vulkan or ROCm build; on NVIDIA, CUDA; otherwise CPU.
5. Tick **Auto Start On Awake** on the server config asset, or call `StartServer()` yourself. Set **Parallel Slots** there to at least the number of decision makers that pin a slot.

### Picking a model with a model config

The package ships three **model config** assets under `Runtime/Defaults/Models`, each carrying the settings for one model plus what it was measured to do:

| Preset | Recommended profile | Valid actions | Expected action | p50 latency |
|---|---|---:|---:|---:|
| Qwen3.5 2B (Q4_K_M) | Reactive | 100% | 62% | 189 ms |
| Granite 4.1 3B (Q4_K_M) | Reactive | 100% | 81% | 278 ms |
| Qwen3.5 4B (Q4_K_M) | Deliberative | 100% | 94% | 574 ms |

Measured on a Radeon RX 6650 XT over 16 labelled scenarios; reproduce with `Experiments~/run_matrix.py`, which ships with the package. Choose by what the object is: Granite 4.1 3B gives the best quality per millisecond for reactive NPCs, Qwen3.5 2B is the fastest, and Qwen3.5 4B suits low-frequency deciders such as a director or a squad commander.

Assign the asset to **both** `BehaviorLLMServer > Model Config` (which model to launch, with what context and GPU layers) and `BehaviorLLMClient > Model Config` (sampling settings). It wins over the StreamingAssets config, so swapping an explicit preset requires updating both references. Newly added components use `Model_ActiveCatalog`, whose empty filename follows the active model in the StreamingAssets config; an explicitly assigned model filename takes priority. Use `Create > BehaviorLLM > Model Config` for your own; copy a preset as a starting point, since assets inside a package are read-only.

`DecisionMaker` reads the measured fields and warns at Awake when its own config contradicts them, for example a thinking budget on a model whose native thinking was measured to be unusable. The warnings are advisory and never override your settings: measure your own scenarios before trusting either.

### Finding more models

The Catalog tab searches Hugging Face live, below the three measured models. The search asks for GGUF repositories listed under **text generation** only, and reads each repository's GGUF header as Hugging Face has parsed it, so what you see has already been filtered on three facts a decision maker depends on:

- **Task.** Speech recognisers, embedding models and vision towers are GGUF files too and download without complaint, then answer nothing. They are never listed.
- **Architecture.** The header's `general.architecture` is checked against the list of text decoders the shipped llama-server build loads. A known one shows as a blue pill; one the list has not seen is hidden until you tick **unverified arch**, since llama.cpp adds architectures every few weeks and a newer model may well work.
- **Thinking.** A model whose chat template opens every answer with a thinking block and has no `enable_thinking` switch gets a **REASONING MODEL** pill, online and in the Installed tab. The package asks every request not to think; such a template cannot hear that, so in the Reactive profile the model spends its 48-token budget reasoning and never answers. The client then retries on raw completion, where a model forbidden to think and stripped of its template picks the cheapest legal action every time. Run a reasoning model with the **Deliberative** profile and a **Thinking Budget Tokens** of a few hundred, or use one of the measured models, none of which reason first. The config the window creates for such a model ticks **Reasoning Model** and recommends Deliberative, and the decision maker warns at start when the profile does not fit.
- **Size.** The **Model size** range filters on the header's real parameter count, not on the number in the name. It is the cap that keeps results runnable on one machine: at Q4 a model needs roughly 0.6 GB of memory per billion parameters, plus room for context. Every file row and every installed model also says **fits GPU** or **over GPU memory** against the video memory Unity reports on this machine.

A pasted `owner/repo` id or a direct `.gguf` link bypasses the filters and shows the repository with its warnings instead, because you asked for that one by name. Anything found online is unmeasured: it installs and runs, but carries none of the accuracy or latency figures the three measured models do.

The Installed tab reads the header of every file on disk and disables **Set active in current scene** for one that is not a text model, so the mistake the filters prevent online cannot be made from a file that arrived another way.

### Option B: you run the server

```
llama-server -m models/my-model.gguf --port 8080 --jinja -np 4 --cache-reuse 256
```

Then leave the scene without a `BehaviorLLMServer` and set **Base Url** on the server config asset to `http://localhost:8080`. Any server that speaks the OpenAI chat-completions API with `response_format: json_schema` should work; llama-server is the one that is tested.

### Transport, and models that think first

The client talks to llama-server one of two ways, chosen by **Transport** on the server config:

- **ChatCompletions** (default) posts to `/v1/chat/completions`. The server applies the model's chat template, the system prompt and state block travel as proper messages, and the request carries `enable_thinking: false` so a model that can think is told not to. This is the transport every measured number was taken on.
- **RawCompletion** posts to `/completion` with the system prompt, state block and `OUTPUT: ` concatenated as plain text. No template runs, and the JSON schema constrains the reply from its very first token, so there is no room for any preamble. Use it for a base model, for a chat template that is broken, or for experiments.

**Auto Switch Transport On Thinking** (on by default) handles one specific failure: the model spends its whole token budget reasoning and returns no answer, which means its template ignored the request not to think. The client logs a warning once and switches itself to RawCompletion for the rest of the session. That is the right fix for a template that is merely out of date. It is the wrong fix for a **reasoning model**, one whose template opens every answer with a thinking block and has no `enable_thinking` switch: forbidden to think and stripped of its template, a small reasoning model answers with the first action and no argument, decision after decision, and every one of them counts as valid. The Model Manager marks such models with a **REASONING MODEL** pill and ticks **Reasoning Model** on the config it creates; the decision maker warns at start when one runs anything but Deliberative with a thinking budget. Run it that way, or use a model that does not reason first. If you know your template only needs the switch, set Transport to RawCompletion yourself and skip the wasted first request.

### Common errors

- `Executable not found at ...StreamingAssets/llama-server(.exe)`: download the server in the Model Manager's Server tab, install llama.cpp system-wide, or set the executable path in its Config tab.
- `Model file not found at ...`: download a model in the Catalog tab and set it active from the Installed tab. Check the status strip: a scene whose server carries a Model Config runs that one, whatever the config file says.
- **Decisions take seconds on a machine that measured hundreds of milliseconds.** The server warns at start when llama-server offloads fewer layers than the model has; with the log level at Verbose it also prints the `offloaded N/N layers to GPU` line on a full offload. If every layer is offloaded and it is still slow, the card's memory is taken by something else and the driver is paging the model over the bus, which runs slower than the CPU while looking healthy. The Installed tab's **fits GPU** reading uses the card's total memory, not what is free; close other GPU-heavy programs, or sign out and back in if the desktop compositor has grown, and start again. Measured on 2026-09-14: 6.5 tokens a second on the GPU against 24 on the CPU with the card's memory oversubscribed.
- `Server is still loading the model`: normal for the first seconds after start; the client retries automatically, as many times as **Model Loading Retry Count** on the server config allows.
- `llama-server rejected the supplied JSON Schema`: inspect `StreamingAssets/_last_applied_schema.json`; an action or argument name outside `[A-Za-z0-9_]` is the usual cause.

---

## Step 2. Define the actions (ActionConfig)

1. `Create > BehaviorLLM > Action Config` in the Project view; name it (e.g. `GuardActions`).
2. Add actions:
   - **Name**: `Patrol` | **Description**: "Walk the corridor" | **Parameters**: none
   - **Name**: `Attack` | **Description**: "Shoot at a target" | **Parameters**: one, named `target`, type `String`
3. For each parameter, optionally fill **Example Value** (used in the few-shot example) and **Allowed Values** (a closed list; the schema then restricts that field to these values and the prompt lists them). Leave the list empty to accept any single word.
4. Write **Model Instructions** for behaviour rules ("If you see the player, Attack. Otherwise Patrol.").
5. Assign the asset to `DecisionMaker > Action Config`.

### Actions with several values

An action carries a list of **Parameters**, each with a name, a type (`String` or `Int`), an example and an optional list of allowed values. Most actions take none or one. When an action needs more, add more, and name each for what it means, because the name is what the model reads:

| Parameter | Type | Allowed values |
|---|---|---|
| `destination` | String | `Gate`, `Yard`, `Cells` |
| `speed` | Int | (any number) |

The action menu prints it as `MoveTo(destination, speed)`, the schema gets one property per parameter under the action, and the model answers with the fields by name:

```json
{"action":"MoveTo","destination":"Yard","speed":"2"}
```

An `Int` value travels as a string constrained to digits, which is why `GetInt` exists; the model is told to answer with a number and cannot answer with anything else.

The handler reads them by name too:

```csharp
public void OnMoveTo(ActionArguments args)
{
    string where = args["destination"];
    int speed = args.GetInt("speed", fallback: 1);
}
```

Three things follow from the design:

- **The single-parameter case is unchanged.** A parameter named `arg`, which is what every config authored before parameters existed becomes on load, prints as `Attack(String)` in the menu exactly as it always did, preserving the legacy parameter name. Current examples now omit argument fields for parameterless actions and include every named parameter; prompt bytes therefore differ from the historical benchmark fixtures. `args.First` reads it. Name the parameter something better only when you are ready to lose the cached prefix once.
- **Every parameter is validated on its own.** A value outside its allowed list, or a non-number for an `Int`, rejects the decision and runs the fallback with the reason in the telemetry record.
- **Runtime values are per parameter.** An `IArgumentOptionsProvider` is asked for each parameter of each action by name, so `destination` can come from the scene while `speed` keeps its authored range.

An action with no parameters emits no argument field at all. It used to send an empty `"arg":""`, which cost tokens and misled models into filling it.

**Keep conditional logic out of Model Instructions.** Measured across Qwen3.5-2B, Granite 4.1-3B and Qwen3.5-4B on 16 labelled scenarios, single-condition rules ("if you see an intruder, chase it"; "if a disturbance was reported, investigate it") were followed almost perfectly, while a two-condition rule ("if your health is below half **and** an intruder is visible, retreat instead of chasing") failed in 15 of 18 cases. Restating the rule first and more explicitly did not help the smallest model at all and made the largest one worse. Rewording is not the fix.

Instead, do not offer the action. The schema is enforced during sampling while the guide is only a suggestion, so a decision you can already make in code should be removed from the menu rather than explained. Reserve Model Instructions for preferences and tone, and let the action set carry the rules.

### Gating the menu per turn

Add a component implementing `IActionAvailabilityProvider` and assign it to `DecisionMaker > Action Availability Provider Component`:

```csharp
public class GuardAvailability : MonoBehaviour, IActionAvailabilityProvider
{
    [SerializeField] private Health health;

    public bool IsActionAvailable(string actionName)
    {
        // Below half health, chasing is never the right answer: do not offer it.
        if (actionName == "Chase") return health.Current >= health.Max / 2;
        return true;
    }
}
```

It calls this once per decision, rebuilds the JSON Schema for the actions that survive, and prefixes the state block with `AVAILABLE THIS TURN: ...`. The model cannot emit a filtered-out action, because the branch is not in the schema it is sampling against.

Two details worth knowing:

- **It does not cost you the prompt cache.** The full action menu stays in the system prompt, which is what the server caches between decisions. Availability travels in the per-decision message and in the schema, both of which sit outside the cached prefix. Schemas are rebuilt each turn to include current argument options. When dynamic argument values change, their descriptions in the system prompt also refresh, which can reduce prefix reuse.
- **Return `true` for names you do not recognise**, so adding an action to the `ActionConfig` later does not silently disable it. If the provider allows nothing at all, it logs a warning and skips the decision rather than sending an empty menu.

Names must match `[A-Za-z0-9_]+`; anything else is skipped from the schema.

---

## Step 3. Add sensors (observation modules)

### Vision

1. Add **`ModularVisionModule`** to the object. It arrives pointing at the shipped `Perception_Default` asset; duplicate that into your own folder to change it.
2. On the perception config: **Mode** is `Sphere` (radius), `Cone` (field of view, can be blocked by walls) or `Global` (the whole scene).
3. **Range**, **Fov Angle** and **Perception Layers**, which decide what counts as a target at all.
4. **Occluder Layers**, Cone only: the layers that block the line of sight. Leave at `Nothing` and walls are seen through.
5. **Trigger Interrupt On New Object**: decide immediately when something not seen before comes into view, instead of waiting for the next tick.
6. **Scan Interval**: how often the surroundings are checked. 0.2 seconds is five looks per second, which is plenty for an object deciding every two seconds.

### Making objects perceivable

1. Add **`LLMContextObject`** to any object it should see (enemy, item, cover).
2. Set **Object Name** ("Skeleton") and **Object Type** ("Enemy").
3. **Data Bindings**: drag a component, pick a field or property (`currentHealth`); its live value is appended to the description: `ID: Skeleton | Type: Enemy | currentHealth: 50`.

### Self-status

1. Add `LLMContextObject` to the deciding object itself and bind its own stats.
2. Add **`SelfObservationModule`** next to it; set **Topic Name** (default `Self-Status`).

### Memory

Add **`BasicMemory`** and give it the same perception config as the vision module, so one asset describes everything this object notices. **Memory Capacity** on that asset decides how many recent actions it remembers, and **Memory Topic Name** the heading they appear under.

### Shared notes: the blackboard

Characters that coordinate need to know what the others know, and the expensive way to get that is for each of them to perceive everything the others perceive. The observation block is the fastest-growing part of a prompt as a scene fills up, and that approach grows it with the square of the cast. A **blackboard** is the cheap way: one character reports "the west gate is open" once, and the others read one short line.

1. Put a **`BehaviorLLMBlackboard`** on any object in the scene and give it a **`BlackboardConfig`** (`Create > BehaviorLLM > Blackboard Config`, or the shipped `Blackboard_Default`). The config sets how many notes the board keeps, after how many seconds a note stops being reported, the heading the notes appear under, and how many reach a prompt.
2. **Writing** is an ordinary action. Add an action such as `Report` with one `String` parameter to the Action Config, and bind its **On Execute** to the board's `Post (ActionArguments)`: the model's value becomes the note. Game code can also post directly with `board.Post(text, author)`, which is how a status system or a director announces something.
3. **Reading** is a **`BlackboardObservationModule`** on every character that should hear the board. Leave **Board** empty to use the one in the scene, or point it at a specific board; a character can carry several modules, one per board, which is how a scene models a radio net every guard hears and a set of orders only officers read. Give it the same config asset as the board, so one asset describes the whole channel.

The notes arrive in the state block under the config's **Topic Name**, newest first, each with its author and age when the config asks for them:

```
--- Radio ---
- [12.0s ago] Guard_02: west gate is open
- [40.5s ago] Control: lockdown lifted in Yard
```

Settings that matter:

- **Stale After Seconds** is the one to tune. A note stays on the board but stops being reported after this long, because a character acting on old news is the same failure as a stale action menu. Set it to roughly how long it takes a character to act on something.
- **Max Reported Entries** and **Entry Max Chars** cap what a single decision is told. Both multiply by every reader on every decision, so keep notes to headlines: the point of the board is to replace perceiving everything with being told the short version. The module implements `IBudgetedObservation`, so under **Max Prompt Chars** notes are trimmed on the same terms as vision and memory.
- **Interrupt On New Note** is off by default and should stay off for a board many characters write to, or every post interrupts everybody, which is a decision storm. Turn it on for a channel that carries urgent calls, and set **Self Author Id** to the character's own name so it does not interrupt itself when it posts.

The board is not a singleton, like everything else in the package: a scene may hold several, and a character reads whichever ones it is pointed at.

### Custom modules

Implement `IObservationModule` (`GetObservation`, `TopicName`, `HasInterrupt`) on any component under the deciding object. Implement `IBudgetedObservation` too if the output is a list that may be truncated under a prompt budget.

---

## Step 4. Wire the actions

1. Assign the Action Config to the decision maker. **Action Bindings** fills itself with one entry per action, in the config's order, with the name and description shown read-only. Each action is a foldout: ones with nothing wired start open, wired ones start collapsed, and the header says how many listeners each has. Hook a function up to each **On Execute**, the same way you would for a UI button. The event is a `UnityEvent<ActionArguments>`, so a handler that wants the value is `void FireAt(ActionArguments args)` and reads `args.First` for a single-parameter action or `args["speed"]` / `args.GetInt("speed")` for a named one; a handler that takes nothing (`Mover.Patrol()`) can be bound as a static call. `ActionArguments.Action` names the action, so one handler can serve several bindings.
2. Edit the Action Config at any time; the list follows it the next time the inspector draws. An entry whose action was removed disappears if nothing was wired to it, and otherwise stays flagged as orphaned with a Remove button, so a renamed action never loses its wiring without you seeing it.
3. Optional **Fallback Action**: what to run when the model's answer cannot be used. Pick one of the config's actions from the dropdown; None means no fallback. Make it the boring, safe choice, and set one for anything you ship.

---

## Step 5. Pick the decision profile

| Field | Reactive (default) | Deliberative |
|---|---|---|
| Native thinking | off | on when **Thinking Budget Tokens** > 0 |
| `reason` field | none | up to **Reason Max Chars** characters before the action |
| Token budget | ~50 | ~50 + reason + thinking budget |
| Use for | enemies, allies, anything that must react | game managers, directors, planners on a slow tick |

The choice lives in the decision config and travels with every request, so reactive and deliberative objects share one server.

**Prefer the `reason` field over native thinking on small models.** Measured on Qwen3.5-2B-Q4_K_M through llama-server build 10775: a schema with a 120-character `reason` field and native thinking off produced `{"reason":"Intruder detected, initiating chase to eliminate threat.","action":"Chase","arg":"Intruder"}` in 26 tokens and 188 ms. The same prompt with native thinking on made the model echo the prompt instead of deciding, and it ran out of tokens without emitting a decision. Keep **Thinking Budget Tokens** at 0 unless a measured run on your model says otherwise.

Other settings on the decision config:

- **Use Structured Output**: keep it on. Turn it off only to measure what unconstrained output does.
- **Include Examples In Prompt**: two worked examples in the system prompt. They cost a few hundred cached tokens once and markedly help small models get the shape right.
- **Decision Interval** is the main cost control: every decision is one request, so 0.5 s is four times the work of 2 s. Sight interrupts still decide immediately regardless.
- **Decision Interval Jitter** spreads decisions out in time so characters sharing one config do not all ask the model on the same frame. It is a fraction of the interval, 0.15 by default: each decision lands up to 15% early or late, and the first one is offset too. The spread is centred on zero, so the average rate is exactly the interval and jitter costs no decisions per minute; what it buys is a steady load instead of bursts of contention on the server's slots. Measured on eight characters, the slow tail shortens while the median stays put. Set it to 0 to put every character back in lockstep, which is only useful when comparing runs.
- **Prompt Budget**: **Max Vision Entries** and **Max Memory Entries** keep only the nearest and most recent few, and **Max Prompt Chars** drops observation lines from the end of the state block. Availability metadata, deferred argument lists, `STATE:`, and its first line are retained. If those cannot fit, the request is rejected and the configured fallback is considered, with the budget error recorded in telemetry. The system prompt is never cut. 0 means unlimited. **Min State Prompt Chars** (256 by default) is the floor under that: if Max Prompt Chars leaves less room than this once the persona and the action menu are accounted for, the decision maker disables itself at start with an error naming the numbers, rather than asking the character what to do while telling it almost nothing about the situation. That failure is invisible otherwise, because the reply is still a valid action and nothing in the telemetry looks wrong. Raise Max Prompt Chars above the figure in the error, lower the floor, or set Max Prompt Chars to 0.
- **Diagnostics**: **Log Prompts** prints the whole prompt before every request, which is invaluable when wiring a scene and unreadable afterwards, so it is off by default. **Log Decisions** prints the answer and the dispatched action. **Log Fallback Usage** names the reason each time the fallback runs; keep it on, because a rising fallback rate is the first sign something broke.

And on the component itself, because they cannot be shared:

- **Argument Options Provider**: a component implementing `IArgumentOptionsProvider` that returns the valid argument values per action at runtime (zone names, unit IDs). Values refresh automatically each decision and are checked again before dispatch. Returning false or an empty list falls back to static allowed arguments; withhold actions with no valid targets through an availability provider.
- **Request Slot**: which of the server's slots this object owns, so its cached prompt survives the others' requests. Give each one a different number and set Parallel Slots to match.

---

## Step 6. Play

1. Press Play. The console shows `Server started`, then, with Log Decisions enabled, the model output (`[DecisionMaker] Answer: {...}`) and `[DecisionMaker] Executing Attack('Orc')`.
2. Subscribe to `DecisionMaker.DecisionTelemetryRecorded` to log latency, token counts, parse failures and fallbacks.

Enable Log Prompts to print the full prompt in the Editor. Interrupts cancel in-flight inference; a fresh decision follows cancellation, and the old answer is discarded. Backend failures use a configured fallback only while its action and argument are allowed. Cancelled requests do not run fallbacks or emit completed-decision telemetry.

## Seeing what was actually sent

`BehaviorLLM > Prompt Inspector` lists every decision as it is made and shows the exact exchange behind the one you select: the cached system prompt, the STATE block for that turn, the raw answer before parsing, the model's reasoning channel if it has one, the applied JSON Schema, and the failure reason when there is one.

Use it when a decision maker does something you did not expect. The run report (`DecisionTelemetryRecorder`) answers *how did the last hundred decisions go*; this answers *what happened in that one*. Filter to a single decision maker, or to failures only, when several are running at once.

**Copy** puts the whole exchange on the clipboard as plain text with the prompt, the state and the answer under headings, which is the form to paste at `llama-cli` when reproducing a decision outside Unity.

The window captures only while it is open, so a project that never opens it pays nothing for it. That also means it starts empty: open it before you press Play.

---

## Responsible use

Three rules, in order of how much trouble ignoring them causes.

- **Keep personal data out of prompts and logs.** Everything an observation module returns is sent
  to the model and is visible in the editor Prompt Inspector. Ordinary telemetry records token counts and outcomes, not the full observation block; prompt logging and copied exchanges may preserve that text. A module
  that reports a player's name, their chat messages or anything they typed puts that text into files
  on disk. Report what the character can perceive, not who the player is.
- **Keep the action set explicit, and let the schema do the rejecting.** The point of deriving the
  JSON Schema from `ActionConfig` is that an action the game has not defined cannot be named. Do not
  add a catch-all action that forwards free text to game code, because that reintroduces exactly the
  class of failure the constraint removes.
- **Set a fallback action.** When a reply cannot be used, or has gone stale between being requested
  and being executed, the fallback is what keeps the character behaving instead of stopping. A
  fallback that is safe in every situation is worth more than one that is clever in most.

Inference is local, so none of the above leaves the player's machine. That is a property of the
deployment rather than of the package: point `BehaviorLLMClient` at a remote server and it stops
being true.

## Troubleshooting

- **Server rejected a JSON Schema**: constrained requests stop with a backend error. Correct the schema and restart the server. Constraints are never silently removed. Custom backends must set `LLMResponse.StructuredOutput` only when they successfully apply the supplied schema.

- **Output contains `<think>` text or is cut off**: the model is thinking despite `Reactive`. Check the model card for how thinking is disabled; the parser strips both `<think>` and `<|channel>thought` blocks, but a truncated block wastes the token budget. Raise **Max Tokens** on the model config, or switch models.
- **The model keeps choosing the wrong argument**: list the valid values in **Allowed Arguments** or provide them through `IArgumentOptionsProvider`; the schema then makes wrong values impossible.
- **Every decision falls back**: read `parseFailureReason` in the telemetry. Unbound action means the binding name differs from the config; missing argument means the action has a parameter type but the model sent `""`.
- **Latency too high**: reduce **Max Prompt Chars** and the vision entries, pin slots, use a smaller quantised model, and make sure the GPU build of llama-server is in use (`-ngl` above 0).
- **Every decision re-processes the whole prompt**: the server is not reusing its cache. Check that the system prompt is identical between decisions, since it is cached at Awake and a script rewriting `systemPersona` every frame defeats it, that `-np` is at least the number of decision makers, and that each has its own **Request Slot**. On the same machine this is the difference between 1677 ms and 22 ms of prompt processing.
- **`cache_reuse is not supported by this context`** in the server log: harmless. That flag covers reuse of non-leading prompt chunks and some builds disable it; ordinary prefix caching still works.
- **Context too small with several decision makers**: `-c` is divided across `-np` slots. With `-c 4096 -np 4` each slot gets 1024 tokens. Multiply the context by the number of slots you need.

### Installed sample builders

When installed under `Packages/`, the sample builders write scenes and configuration into `Assets/BehaviorLLMSamples/<sample>/`. They copy missing optional art there before changing importers or generating controllers, including new files absent from an older generated copy. PrisonYard uses baked native Unity character assets and requires no glTF importer. Keep its license files with it. An `Assets/` installation generates beside its source. Missing art still produces primitive scenes. Perceivable objects need a collider on the same GameObject as `LLMContextObject`, because vision resolves metadata from that collider.
