# BehaviorLLM: Architecture & Workflow Analysis

## 1. High-level overview

**BehaviorLLM** is a Unity framework for game objects whose decisions are made by a local language model. It implements a **Sense-Think-Act** loop tuned for on-device inference: a cached prompt prefix, schema-constrained output, per-object latency profiles and reflex interrupts.

The deciding component is called `DecisionMaker` rather than an agent, because nothing about it is specific to characters: the same component drives an enemy, a companion, or a game system such as a director. Everything that tunes it lives in config assets, listed in section 2.F; what belongs to the project rather than to any one object lives in the settings asset in section 2.G.

```
+------------------+     +-------------------+     +----------------------+     +------------------+
| IObservationModule| --> | PromptBuilder     | --> | ILLMBackend          | --> | DecisionParser   |
| (vision, memory, |     | system (cached) + |     | BehaviorLLMClient -> |     | JSON -> action,  |
|  self, custom)   |     | STATE block       |     | llama-server (HTTP)  |     | arg, reason      |
+------------------+     +-------------------+     +----------------------+     +--------+---------+
                                                                                        |
                                              +-----------------------------------------+
                                              v
                                   actionBindings[action].Invoke(arg)  +  telemetry record
```

## 2. Core components

### A. The orchestrator: `DecisionMaker`

It contains no game logic; it bridges Unity's component and event system to the model.

- **Decision loop.** A timer set by the decision config triggers `Think()`. Every frame it also polls `HasInterrupt()` on its observation modules; a true value bypasses the timer. During inference it cancels the request and discards its result, then requests fresh observations after cancellation completes.
- **Decision profile.** `Reactive` (default) sends `enable_thinking:false`, no `reason` field and a budget of about 50 tokens. `Deliberative` adds a capped `reason` string before the action and optionally a native thinking budget. The profile is expressed per request, so objects with different profiles share one server. Deliberation is opt-in because every token of reasoning is latency a reactive entity cannot afford.
- **Prompt.** `PromptBuilder.BuildSystemPrompt` renders persona, output contract, action menu (with allowed arguments), two examples and the config's `modelInstructions`; it is built at Awake and on `RebuildSchema()`. Dynamic argument providers also refresh it each turn; unchanged values preserve the prefix. `BuildStatePrompt` renders current observations as the per-decision state block. In chat transport the two halves are the system and user messages; in raw transport they are concatenated with the `OUTPUT: ` lead.
- **Prompt budget.** The vision and memory entry caps ask budgeted modules for their most relevant entries; the character cap drops observation lines from the end of the state block. The system prompt is never cut and the cut never lands mid-word (a mid-word cut once made small models complete the template instead of answering).
- **Parsing and dispatch.** `DecisionParser.Parse` strips native-thinking blocks (`<think>…</think>`, `<|channel>thought…<channel|>`, terminated or not), fences and prose, extracts the first balanced JSON object and reads `reason`, `action`, `arg`. The action must exist in ActionConfig, be bound, and still be available; arguments must satisfy current allowed values (or the identifier pattern when unrestricted); an optional `IActionArgumentPolicy` may normalise or reject it. Any failure routes to the optional fallback action.
- **Telemetry.** Every completed attempt publishes a `DecisionTelemetry` record (source, latency, prompt, completion and cached token counts, profile, structured-output flag, result type, action, argument, reason and failure reasons) through `DecisionTelemetryRecorded`. Cancelled requests produce no completed record. Schema status comes from the backend response; backend failures with fallbacks retain both outcomes.

### B. Perception

- **`LLMContextObject`** (passive metadata): name, type, description and reflection-based `dataBindings` (`HealthComponent.currentHealth`). Formats as `ID: Name | Type: Category | Info: ... | field: value`. Discovered by vision strategies through collider lookup; it does not observe itself.
- **`SelfObservationModule`** (active self-status): `[RequireComponent(LLMContextObject)]`; reports the host's context every decision under its topic name.
- **`ModularVisionModule`**: `Sphere` / `Cone` / `Global` strategies, a mask for what can be noticed and a second one for what blocks the view in Cone mode, and a throttled scan. Interrupts fire when a previously unseen instance ID enters view, never when one leaves. Implements `IBudgetedObservation` (nearest N). All of it comes from a shared `PerceptionConfig`.
- **`BasicMemory`**: the last few executed actions, capacity and heading from the same `PerceptionConfig`; `IBudgetedObservation` (most recent N).
- **`BehaviorLLMBlackboard` + `BlackboardObservationModule`**: a shared noticeboard, written through an action binding (`Post(ActionArguments)`) or from code, read as an observation by whichever characters carry the module. Exists so coordination does not grow the observation block with the square of the cast. Retention, staleness, heading and per-decision caps come from a `BlackboardConfig`; the module is `IBudgetedObservation` and its interrupt is opt-in per module, consumed once per note.
- **`IObservationModule` / `IBudgetedObservation`**: the extension points for custom sensors. `ObservationComposer.Compose(modules, maxVision, maxMemory)` assembles the state block, one `--- Topic ---` section per module.

### C. Actions: `ActionConfig`

A ScriptableObject that is the object's API definition: a list of `ActionDefinition` (name, description, and a list of named `ActionParameter`s, each with a type, an example and **allowed values**; a pre-parameters config folds its three legacy fields into one parameter named `arg` on load, which keeps its prompt prefix byte-identical) plus `modelInstructions`. It drives three things at once: the action menu in the prompt, the few-shot examples, and the JSON Schema.

Source-of-truth split: `ActionConfig.validActions` is the **schema** (what exists), `DecisionMaker.actionBindings` is the **wiring** (who handles it), `bindingsByName` is the dispatch **cache** built at Awake. The `DecisionMaker` inspector keeps the wiring list a mirror of the schema, so a typo cannot be entered; divergence that reaches runtime anyway (bindings built in code) is still warned at Awake.

### D. Structured output: `ActionSchemaBuilder`

Builds `{"oneOf":[ ... ]}` with one branch per action:

```json
{"type":"object","properties":{
   "reason":{"type":"string","maxLength":120},          // Deliberative only
   "action":{"const":"MoveTo"},
   "arg":{"enum":["Zone_A","Zone_B"]}},                  // or {"const":""} / {"pattern":"^[A-Za-z0-9_]+$"}
 "required":["reason","action","arg"],"additionalProperties":false}
```

llama-server compiles the schema to a grammar and applies it during sampling, so the model cannot emit an unknown action or, when options are known, an unknown argument. Argument options come from `ActionDefinition.allowedArguments` or, at runtime, from a component implementing `IArgumentOptionsProvider` (scene-derived IDs). A component implementing `IActionAvailabilityProvider` can additionally drop whole actions from the schema for one decision, which is how a conditional rule is enforced rather than merely requested. Property order is deliberate: `reason` first so a deliberative decision is reasoned before it is made, `action` before `arg` so the action can be dispatched early from a stream in the future. A top-level `oneOf` is required because llama.cpp's schema converter ignores `properties` placed next to `oneOf`.

### E. Backend: `ILLMBackend`, `BehaviorLLMClient`, `BehaviorLLMServer`

- **Contract.** `Task<LLMResponse> CompleteAsync(LLMRequest, CancellationToken)`. The request carries the prompt halves, the schema, the token budget, the slot and the thinking policy; the response carries text, separated reasoning, token counts (prompt, completion, cached) and latency. Because every constraint travels with the request, one client component can serve any number of decision makers.
- **`BehaviorLLMClient`.** `BackendTransport.ChatCompletions` (default) posts to `/v1/chat/completions` with `response_format: {type: json_schema}`, `cache_prompt: true`, optional `id_slot`, `chat_template_kwargs: {enable_thinking}` and `thinking_budget_tokens`; the server applies the model's chat template and returns `reasoning_content` separately. `BackendTransport.RawCompletion` posts a raw prompt to `/completion` with `json_schema` for base models or A/B measurements. Bodies are written by `LlamaRequestWriter` by hand so the schema inlines as a nested object and empty fields are omitted. `UnityWebRequest` is wrapped in a `Task`; a `CancellationToken` tied to the component's lifetime aborts in-flight calls. Startup connection errors and `503 loading model` are retried.
- **`BehaviorLLMServer`.** Optional child-process manager for `llama-server`: reads the StreamingAssets config, resolves the binary (StreamingAssets, then `PATH`), launches with `-c`, `-ngl`, `-np`, `--cache-reuse` and `--jinja` from the server and model configs, forwards logs with severity filtering and detects readiness. `GrammarLoadFailed` latches when the server rejects a schema; the client then fails constrained requests until the process is restarted. Not a singleton; persistence across scenes is the consumer's choice. It never passes `--reasoning-budget`: a server-wide budget would disable the per-request thinking control.

### F. Configuration domains

No component carries a tuning field. Four ScriptableObject types hold everything that tunes a decision maker, and each is shared by as many objects as the project likes:

| Asset | Holds | Read by |
|---|---|---|
| `DecisionMakerConfig` | Decision interval, profile, reason and thinking budgets, token ceiling, structured output and few-shot toggles, prompt budget, console noise | `DecisionMaker` |
| `BehaviorLLMServerConfig` | Base URL, transport, port, parallel slots, cache reuse, jinja, extra arguments, executable, StreamingAssets source, timeouts, retry policy, log verbosity | `BehaviorLLMClient`, `BehaviorLLMServer` |
| `BehaviorLLMModelConfig` | Model file, context size, GPU layers, sampling, and what that model was measured to do well | `BehaviorLLMClient`, `BehaviorLLMServer` |
| `PerceptionConfig` | Vision shape, range, field of view, perception and occluder layers, scan interval, sight interrupts, prompt headings, memory capacity | `ModularVisionModule`, `BasicMemory` |

`BehaviorLLMDefaults` resolves them. In the Editor, `Reset()` on each component finds the preset the package ships under `Runtime/Defaults`, so a component added to a scene arrives working. At runtime a cleared reference falls back to a throwaway instance carrying the type's declared defaults, so no component ever dereferences null. Every component also exposes `ApplyConfig(...)`, which swaps the asset and rebuilds whatever depended on it; cloning a shared asset with `Instantiate` and applying the clone is how one object is varied without touching the others.

### G. Project settings, and the gate every console line goes through

The four assets above each tune one domain and are meant to exist in variants: a Reactive preset and a Deliberative one, a perception profile per character type. `BehaviorLLMSettings` is the fifth configuration asset and the odd one out. There is one per project, it is loaded by name from a `Resources` folder through `BehaviorLLMSettings.Current` rather than being wired into a component, and it is never null: with no asset anywhere, the declared defaults apply. It answers the questions that belong to the project rather than to any decision maker.

| Setting | Default | What it decides |
|---|---|---|
| `editorLogLevel` / `playerLogLevel` | `Warnings` / `ErrorsOnly` | How much the package prints, separately in the Editor and in a non-development build |
| `telemetryEnabled` / `telemetryInBuilds` | on / off | Master switch for `DecisionTelemetryRecorder`, and whether it may record outside the Editor |
| `dumpAppliedSchema` | on | Whether the Editor writes `_last_applied_schema.json` to StreamingAssets on every schema rebuild |
| `warnOnBindingMismatch` / `warnOnModelProfileMismatch` | on | The two advisory startup warnings |

**It is a ceiling, never an override.** A decision maker whose config asks to be quiet stays quiet whatever the project level says; what the level can do is silence one that asked to be loud. That asymmetry is what makes a single switch enough to quieten a whole project before a build without visiting every config asset, and it is why the setting cannot be used to force diagnostics on.

Every console line in `Runtime/` goes through `BehaviorLLMLog`, which is what enforces the ceiling. Messages are passed as `Func<string>` rather than as strings, so a suppressed line costs nothing to build; that matters because the verbose lines interpolate whole prompts and response bodies. There are four entry points. `Error` and `Warn` are the obvious two. `Requested` is a line a config asset explicitly asked for, such as `logPrompts`, and prints at the normal level, because needing a second project-wide switch before the first one works is a trap. `Info` is internal detail nobody asked for and needs `Verbose`. `BehaviorLLMLog.Allows(level)` is public for a caller that would do real work purely to log. A bare `Debug.Log*` in `Runtime/` would be a line the project cannot turn off, which is why there are none.

## 3. Step-by-step workflow

1. **Observe.** `ObservationComposer` iterates the modules found under the object at Awake. Vision returns `- [5.0m] ID: Orc | Type: Enemy`, memory returns `- [12.3s] Executed Action: Patrol()`, self-status returns `ID: Hero | Type: Self | Health: 100`.
2. **Prompt.** Cached system prompt + `STATE:` block, trimmed to budget.
3. **Request.** `LLMRequest` with schema, slot, budget and thinking policy; `BehaviorLLMClient` posts it; llama-server samples under the schema and reuses the cached prefix.
4. **Decision.** The response is `{"action":"Attack","arg":"Orc"}` (or with a leading `reason` in Deliberative mode).
5. **Act.** `DecisionParser` extracts the decision; the component validates it against the bindings and the action definition, applies the optional argument policy, invokes the bound `UnityEvent<ActionArguments>` (one value per named parameter; `args.First` for the single-parameter case), records `Executed Action: Attack(Orc)` in memory and publishes the telemetry record. Invalid output runs the fallback action instead, with the reason in the record.

## 4. Design notes

- **Why JSON only.** Free-form `Action(Arg)` text needed two regex parsers and a dozen toggles to reach ~95% validity; a schema-constrained JSON object reaches it structurally and is what small instruct models are trained to produce.
- **Why constraints per request.** A shared backend holding one schema meant the last object to start won. Moving the schema into the request removed the foot-gun and made slot pinning possible.
- **Why the static/dynamic split.** Prefix reuse (Gim et al., *Prompt Cache*, MLSys 2024; SGLang's RadixAttention) turns a 600-token system prompt into a cache hit per decision; only the state block is re-processed.
- **Why thinking is per object.** Reactive entities need the shortest response the model can produce; a director deciding every few seconds can afford a reasoned one. A server-wide setting could not express both.
- **Why settings live in assets.** A component's inspector is the first thing a new user sees, and twenty numbers on it is a wall rather than an invitation. Moving them into config assets makes the component readable, lets one edit retune a whole level, and gives a project unlimited variations without a prefab for each. What stays on the component is only what cannot be shared: its persona, its action bindings, the scene objects it talks to and the server slot it owns.
