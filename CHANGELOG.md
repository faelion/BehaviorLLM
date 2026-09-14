# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **`DecisionMaker` has a component icon**, so it is identifiable in the inspector header and the
  Add Component list rather than showing the default script glyph. The icon is the brain from the
  logo rather than the whole lockup: at the 16 px Unity actually draws a component icon at, the
  branches of the full mark collapse into a smudge, while the brain still reads. It is tinted
  `#4E86C7` because a black glyph is nearly invisible against the dark Editor theme and Unity does
  not supply a second icon per theme. The texture lives under `Editor/`, so it never reaches a
  player build.
- **A logo.** A stacked lockup: a brain at the root of a tree whose branches end in arrows, over
  the wordmark — the behaviour tree redrawn with the model where the hand-authored logic used to
  sit. `.github/logo.svg` and `logo_dark.svg` are vector and carry no font dependency; `logo.png`
  and `logo_dark.png` sit beside them for contexts that will not take SVG, and `mark.png` /
  `mark_dark.png` are the glyph alone for an avatar. The light and dark variants are the same
  outline with the ink inverted, so they cannot drift apart. The README header now shows it, and
  the `<h1>` beneath it is gone because the lockup already carries the name.

### Fixed
- **The model catalogue did not offer two of the three models the package recommends.**
  `Resources/BehaviorLLMModelCatalog.json` listed seven models and `Runtime/Defaults/Models/` ships
  three presets; they overlapped in exactly one. Granite 4.1-3B — the model the README calls the
  best quality per millisecond for reactive characters, and the one behind all three recorded
  telemetry runs — was absent, as was Qwen3.5-4B, the Deliberative recommendation. Following the
  Quick start therefore led to a download window that could not supply what the next step assumed
  you had. Both are now listed, from IBM's and Unsloth's own GGUF repositories.
- **A downloaded model did not match the preset that expected it.** The catalogue fetched
  Qwen3.5-2B as `Qwen_Qwen3.5-2B-Q4_K_M.gguf` while `Model_Qwen3.5-2B-Q4_K_M` looks for
  `Qwen3.5-2B-Q4_K_M.gguf`, so even the one model present downloaded under a name the preset could
  not find. All three entries now use sources whose file names match the presets exactly. The three
  measured models are now the whole list, and their descriptions carry the figures they were
  measured at. The six entries the project never measured (Hermes 2 Pro, Qwen2.5 Coder, Qwen2.5 3B,
  Mistral 7B, Phi-3 Mini, Gemma 2 9B) were removed: they carried no figures, matched no shipped
  preset, and were the reason the list had gone stale. The quick start in the README named Gemma 4
  E2B, which the catalogue never carried either, and now names the three that ship.
- **The readme's delete buttons and links did nothing unless the package sat at
  `Assets/BehaviorLLM/`.** Every path in `Readme.asset` was stored project-relative and resolved
  against the project root, so on the installation the README recommends - the git URL, which the
  Package Manager resolves into its own cache - all three entries reported themselves as *"Already
  removed"* while the samples were still there taking 26 MB, and both links logged *"Readme link
  points at nothing"*. Copying the package into `Packages/`, or renaming the folder under
  `Assets/`, broke it the same way. Paths are now stored relative to the package root and resolved
  against wherever the readme asset actually is, so every installation shape works; a path still
  written as `Assets/…` or `Packages/…` is honoured unchanged, so an edited readme keeps working.
- `Tests/Editor/ReadmePathResolutionTests.cs`: nine tests pinning the path resolution, including
  that a package-relative path survives the folder being renamed, that `Experiments~` keeps its
  trailing tilde so the System.IO fallback can still find it, and that a legacy `Assets/…` path is
  left alone rather than being re-rooted into `Assets/BehaviorLLM/Assets/BehaviorLLM/…`.
- **The same page now says when deletion is impossible rather than offering it.** A package
  resolved from a registry or a git URL is immutable, unpacked under `Library/PackageCache` which
  Unity rebuilds from the manifest, so a deletion there comes back on the next resolve. The page
  explains that, keeps showing the folder sizes, and points at removing the dependency or
  installing into `Assets/` instead. Embedded and local packages stay deletable.
- **The StealthGuard telemetry run was credited to the wrong model.** `Experiments~/README.md`
  described it as three guards on Qwen3.5-2B; `runs_summary.csv`, written by the package's own
  recorder, records `granite-4.1-3b-Q4_K_M.gguf` for all three shipped runs. The decision count,
  the duration and the cache figure in that row were already right.
- **`CONTRIBUTING.md` was written for the development harness and shipped unchanged.** The copy
  people fork pointed at `Docs/Experiments/run_matrix.py`, at `PLAN.md`, and at an absolute path
  on the author's machine, none of which exist in the package repository; the paths are now
  relative to the package root (`Experiments~/run_matrix.py`, `Experiments~/matrix_results.csv`).
  Its issue template asked for an `OutputFormat` setting and a `useGrammar` flag that no longer
  exist, and
  for `[Agent] Thought:` log lines the runtime has never printed, and it named
  `BehaviorLLMClient.url` rather than **Base Url** on the server config. It now asks for a Prompt
  Inspector exchange instead. The PR checklist gained the three rules the README already said it
  carried: a tooltip on every inspector field, no bare `Debug.Log*` in `Runtime/`, and a changelog
  entry for anything a consumer sees.
- **The architecture document predated the settings asset.** It still said four ScriptableObject
  types hold everything that changes behaviour and never mentioned `BehaviorLLMSettings` or
  `BehaviorLLMLog`, both of which landed the day after it was written. A new section 2.G covers
  the project-wide settings asset, why it is a ceiling rather than an override, and the log gate
  that enforces it.

## [0.5.0] - 2026-09-12

First release published as a package in its own right, at
<https://github.com/faelion/BehaviorLLM>. The package id is now `com.faelion.behaviorllm`;
it was `com.alexdelgado.behaviorllm` in every version before this one, so a project already
depending on the old id has to change that line in its manifest.

### Added
- **The two gameplay samples now live inside the package**, at `Samples/StealthGuard` and `Samples/PrisonYard`, instead of in the consuming project. Each was already self-contained, art and licence included, so the move is a relocation rather than a restructure and every asset GUID is preserved. This is what makes the package shippable as one folder: someone who takes it from GitHub gets the samples with it.
- **`Readme.asset`, the package's landing page** (`Tools > BehaviorLLM > Readme`). A ScriptableObject with a custom inspector, in the spirit of the readme Unity's own templates ship: what the package does, how to get a model running, what the samples show, and where the documentation is. Its second job is disposal. The samples are by far the largest thing in the package, so the page measures each one on disk and offers to delete it, and offers a second button that removes both samples, the readme asset and the two scripts that draw it, leaving no trace behind. Every deletion names the folders and says what is lost before it happens. Nothing in the package references the samples or the readme, so removing them cannot break a project.
- **Sample assemblies are gated on the packages they need.** `Project.Samples.StealthGuard` and `Project.Samples.PrisonYard` declare version defines for `com.unity.ai.navigation` and `com.unity.inputsystem` and constrain themselves to those defines. A project without either package installs and uses BehaviorLLM normally; the samples are skipped rather than broken. The package core still has no package dependencies of any kind.
- **Dynamic argument values moved out of the cached prompt prefix.** An action whose arguments come from an `IArgumentOptionsProvider` no longer prints its values in the action menu; the menu says `[arg: listed under ARGUMENTS below]` and the current values travel in the per-decision state block. Actions with authored `allowedArguments` are unaffected, because a list that cannot change costs nothing in a cached prefix. Controlled by `DecisionMakerConfig.dynamicArgumentsInState` (default on); turning it off restores the previous layout byte for byte, which is how the two were compared.

  Measured on PrisonYard, two five-minute runs on the same build, eight decision makers on one server, one flag apart:

  | | Values in the menu | Values in the state block |
  |---|---:|---:|
  | Prompt cache hit rate | 0.547 | **0.710** |
  | Median decision latency | 2,250 ms | **1,686 ms** |
  | p95 decision latency | 4,284 ms | **3,444 ms** |
  | Decisions per minute | 84.8 | **88.8** |
  | Mean prompt tokens | 568 | 605 |

  The cause was found with the Prompt Inspector: with the values in the menu, the four prisoners - whose arguments are *other prisoners*, and which hiding place is free - produced a different system prompt on essentially every decision, so four of the eight decision makers had no usable cache at all. With them in the state block, all eight hold a single stable prefix. The 37 extra prompt tokens are the values moving from the cached half to the uncached half, and they buy back roughly a quarter of the median latency.
- `PromptBuilder.BuildStatePrompt(observations, availableActions, argumentOptions)` and an `ARGUMENTS:` block in the state prompt.
- `ActionConfig.GetPromptDescription(optionsFor, inlineProviderOptions, deferred)`, which reports the actions whose values it left out.
- `Tests/Runtime/DynamicArgumentPlacementTests.cs`: nine tests, the load-bearing one being that the menu is byte-identical when the provider's values change *and* when it runs out of targets entirely.
- **Prompt Inspector** (`Tools > BehaviorLLM > Prompt Inspector`). A live table of every decision as it happens - source, action, latency, tokens with the cached count, result - and a detail pane for the selected row showing the exact system prompt, the STATE block for that turn, the raw answer, the reasoning channel, the applied JSON Schema and the failure reason. Filter by decision maker or to failures only, pause, and copy a whole exchange to the clipboard in a form that can be replayed against `llama-cli`. Answers "what happened in *that* decision", which the telemetry CSV cannot: it is all numbers and short strings by design.
- `DecisionMaker.DecisionInspected` and `DecisionMaker.CaptureExchanges` (both Editor-only, both static), plus Editor-only `LastSystemPrompt` / `LastStatePrompt` / `LastSchema` / `LastResponseText` / `LastReasoningText`. The prompt text travels on its own channel rather than in `DecisionTelemetry`, so run reports stay small, and it is only retained while the inspector window is open.
- **`BehaviorLLMSettings`, a project-wide settings asset.** The other four config assets each tune one domain and are meant to exist in several variants; this one answers the questions that belong to the project rather than to any decision maker - how loud the package is, whether it records anything, and which diagnostics it writes. Create it with `Assets > Create > BehaviorLLM > Settings` and keep it in a `Resources` folder; the package ships one at `Resources/BehaviorLLMSettings.asset`. With no asset at all it falls back to the documented defaults, so nothing breaks in a project that never makes one.
  - `editorLogLevel` / `playerLogLevel` - `Off | ErrorsOnly | Warnings | Verbose`, separately for the Editor and for a non-development build. Defaults: `Warnings` and `ErrorsOnly`.
  - `telemetryEnabled` / `telemetryInBuilds` - a master switch for `DecisionTelemetryRecorder`, so a scene shipped with a recorder still in it writes nothing without a scene edit.
  - `dumpAppliedSchema` - whether the Editor writes `_last_applied_schema.json` to StreamingAssets on every schema rebuild.
  - `warnOnBindingMismatch`, `warnOnModelProfileMismatch` - the two advisory startup warnings, for projects that are knowingly running against them.
- `BehaviorLLMLog`, the gate every console line in the runtime now goes through. Messages are passed as `Func<string>`, so a suppressed line costs no string building - which matters because the verbose lines interpolate whole prompts and response bodies. Four entry points: `Error`, `Warn`, `Requested` (a line a config asset explicitly asked for, such as `logPrompts`; prints at the normal level, because needing a second project-wide switch before the first one works is a trap) and `Info` (internal detail nobody asked for; needs `Verbose`). `BehaviorLLMLog.Allows(level)` is public for callers that would do extra work purely to log.
- `DecisionTelemetryRecorder.Recording`, the effective answer to "is this recorder writing anything", combining its own switch with the project's.
- `Tests/Runtime/BehaviorLLMSettingsTests.cs`: ten tests covering the no-asset fallback, each log level, that a suppressed message is never built, that the telemetry master switch vetoes rather than overrides, and that a config's own `logPrompts` still works at the default project level.
- **`Experiments~/`, the measurement harness and its data, shipped inside the package.** The offline matrix scripts, the exact prompts and schemas they send, the per-configuration and per-decision CSVs, and three recorded telemetry runs (the StealthGuard run and the two PrisonYard prompt-layout runs), with a README explaining each file and how to re-run them. The folder name ends in `~` so Unity never imports it, and it is listed in `Readme.asset` so it can be removed from the Editor like the samples.
- **`CONTRIBUTING.md` now travels with the package**, so the checklist a pull request is measured against is in the repository people fork.
- **A "Responsible use" section in the user guide**: keep personal data out of prompts and logs, keep the action set explicit and let the schema reject, and always set a fallback action.
- **README rewritten for release**: header with badges and a link bar, a measured-results table, requirements, screenshots of the Model Catalog, the ActionConfig asset, the DecisionMaker inspector and the Prompt Inspector, the architecture diagram, a comparison with neighbouring tools, a collapsible advanced section and a how-to-help section. The images live under `.github/`, which Unity ignores.

### Changed
- Every `Debug.Log*` call in the runtime now routes through `BehaviorLLMLog`. In a non-development player build the package prints errors only by default, where previously every warning and every decision line shipped with the game.
- **Telemetry no longer records in player builds by default.** Set `telemetryInBuilds` on the settings asset for a playtest build you intend to collect numbers from. Editor behaviour is unchanged.
- The per-config `logPrompts` and `logDecisions` switches are unchanged and still apply. The project level is a **ceiling**, not an override: it can silence a decision maker that asks to be loud, and never makes one loud that asked to be quiet.
- **The readme uninstaller can remove folders Unity does not import.** `BehaviorLLMReadmeEditor` now checks for and deletes trailing-`~` folders through `System.IO`, since the AssetDatabase does not know they exist; imported folders still go through `AssetDatabase.DeleteAssets` as before.

## [0.3.1] - 2026-09-08

### Added
- `ModularVisionModule.VisibleObjects` and `ModularVisionModule.Sees(Transform)`: a read-only view of what the last scan found. Game code can now answer "is anyone watching this?" itself, instead of asking the model to reason about it. Added for the PrisonYard sample, whose prisoners only get Fight, Hide and Sneak on the menu while no guard sees them.

### Fixed
- A rejected server schema now fails constrained requests until the server is restarted; it no longer silently disables constraints. Backends report successful schema use through `LLMResponse.StructuredOutput`, which drives telemetry instead of the configured schema.
- Dispatch revalidates action membership, current availability, and current argument options, including when structured output is disabled. Optional argument policies cannot bypass the allowed values. Fallback actions undergo the same availability and argument checks.
- Dynamic argument providers refresh each decision, including their prompt descriptions. Schemas are rebuilt per turn instead of cached only by action names; PrisonYard no longer needs to invalidate every decision maker after a lockdown.
- Observation interrupts cancel in-flight requests, discard stale answers, and schedule a fresh observation after cancellation completes. Cancellation and re-enable cannot let an older request dispatch or clear a newer request's state.
- Backend failures execute a configured, currently valid fallback. Telemetry retains the backend error and aggregators count both the failure and fallback.
- Editor prompt logging respects `DecisionMakerConfig.logPrompts`.

## [0.3.0] - 2026-09-07

### Added
- **Action bindings mirror the Action Config.** A custom inspector for `DecisionMaker` keeps the bindings list in sync with the assigned config every time it is drawn: one entry per action, in the config's order, names read-only, only `On Execute` editable. Nobody types an action name twice any more, so a binding can no longer silently fail to match its action. A binding whose action left the config is dropped only if nothing is wired to it; one with listeners is kept, flagged as orphaned, and offered a Remove button, so a rename never discards wiring unseen. The fallback action is a dropdown of the config's actions for the same reason. Covered by `Tests/Editor/DecisionMakerEditorTests.cs`.
- **Configuration domains.** Four reusable ScriptableObject assets now hold everything that tunes behaviour, so components carry only references, scene wiring and read-only status:
  - `DecisionMakerConfig` - decision interval, profile, reason and thinking budgets, token ceiling, structured-output and few-shot toggles, prompt budget, and three new console-noise switches (`logPrompts`, `logDecisions`, `logFallbackUsage`).
  - `BehaviorLLMServerConfig` - base URL, transport, managed-process flags (port, slots, cache reuse, jinja, extra arguments, executable), StreamingAssets source, request timeouts and retry policy, and server log verbosity. Read by both the client and the server component.
  - `PerceptionConfig` - vision mode, range, field of view, perception and occluder layers, scan interval, sight interrupts, prompt section headings and memory capacity. Read by both `ModularVisionModule` and `BasicMemory`.
  - `BehaviorLLMModelConfig` - unchanged, now the only source of sampling settings and of the model file, context size and GPU layers.
- Default presets under `Runtime/Defaults`: `Config_Reactive`, `Config_Deliberative`, `Server_LocalLlama`, `Perception_Default`. Adding any BehaviorLLM component in the Editor wires the matching preset automatically through `Reset()`, so a new scene works before the user authors a single asset.
- `ApplyConfig(...)` on `DecisionMaker`, `BehaviorLLMClient`, `BehaviorLLMServer`, `ModularVisionModule` and `BasicMemory`, for swapping configuration at runtime. Clone a shared asset with `Instantiate`, change what you need, and pass it in: this is how a scene gives one character a different cadence without a prefab of its own.
- `Tests/Runtime/DecisionMakerDispatchTests.cs`: fourteen tests covering everything between "the model answered" and "the game did something" - dispatch, think-block stripping, the fallback paths (parse failure, unbound action, missing argument, empty or unbound fallback name) and the telemetry each produces. Eleven of them were the retired UrbanIncidentResponse sample's reliability suite, which turned out to be testing the package rather than the scenario.
- `BehaviorLLMDefaults`, the small helper that finds the shipped presets and supplies documented defaults when a reference is left empty. No component ever dereferences a null config.
- `BehaviorLLMModelConfig`, the first configuration domain split out of the components: model file, launch settings (context size, GPU layers), sampling, and what the model was *measured* to be good at. `BehaviorLLMClient` takes its sampling from it, `BehaviorLLMServer` takes the model file, context size and GPU layers, and `DecisionMaker` warns at Awake when it is set up against the measurement (deliberation on a model that did worse with it, or a thinking budget on a model that does not decide while thinking). Swapping models is one reference change instead of editing several components.
- Three presets under `Runtime/Defaults/Models`, carrying the 2026-09-04 measurements: **Qwen3.5 2B** (Reactive, 100% valid, 62% expected, 189 ms p50), **Granite 4.1 3B** (Reactive, 81% expected, 278 ms; the only model measured *worse* with deliberation), **Qwen3.5 4B** (Deliberative, 94% expected, 574 ms). Native thinking is marked unusable on all three.
- `IActionAvailabilityProvider`: a component can decide which actions may be chosen each decision. Unavailable actions are dropped from the per-request JSON Schema, so the model cannot emit them at all, and the turn's menu is announced in the state block. Measured motivation: a two-condition rule written in `modelInstructions` failed in 15 of 18 cases across Qwen3.5-2B, Granite 4.1-3B and Qwen3.5-4B, and rewording it did not help.
- `ActionSchemaBuilder.Options.IsActionAvailable` and `PromptBuilder.BuildStatePrompt(observations, availableActions)`.

### Changed
- **BREAKING -** `LLMAgent` is now `DecisionMaker`, in namespace `BehaviorLLM.Core.Decisions` (was `BehaviorLLM.Core.Agent`), under `Runtime/Core/Decisions`. The word "agent" has drifted toward autonomous tool-using LLM agents, and the component was never limited to characters: it drives a manager, a director or any system that needs a decision. Scene and prefab references survive because the script GUID is unchanged.
- **BREAKING -** `LLMAgentDecisionTelemetry` is now `DecisionTelemetry`, and its `agentId` field is now `sourceId`. `AgentActionDef` is now `ActionDefinition`.
- **BREAKING -** Every tuning field was removed from the components and now lives in the config assets listed above. Values previously set in a scene are not migrated: assign a config asset, or accept the documented defaults. `BehaviorLLMServer` also loses its own `modelPath`, `contextSize` and `gpuLayers`; the model file and its launch settings come from the model config or the StreamingAssets file.
- **BREAKING -** `BehaviorLLMClient.Transport` moved out to `BehaviorLLM.Core.Config.BackendTransport`.
- **BREAKING -** The fallback action is enabled by naming one. `FallbackActionConfig.enabled` and `logFallbackUsage` are gone; an empty name means no fallback, and the logging switch moved to `DecisionMakerConfig`.
- The telemetry recorder writes `<runId>_sources.csv` instead of `<runId>_agents.csv`, with a `source_id` column. Its `trackAllAgentsInScene`, `trackedAgents` and `writeAgentCsv` fields were renamed and keep `[FormerlySerializedAs]`.
- Prompt logging is off by default. Previously every decision printed its full prompt to the console in the Editor, which was unreadable with more than one decision maker; turn `logPrompts` on in the config when wiring a new scene.
- The action menu in the system prompt stays complete even when a provider gates it, so the prefix the server keeps in its KV cache never changes between decisions. Availability travels in the per-decision message and in the schema, both of which are outside the cached prefix. Schemas are now rebuilt each turn; dynamic argument changes also refresh their prompt descriptions.
- With structured output off, an action that is unavailable this turn is rejected on the same path as an unbound one, with `parseFailureReason` naming the reason.

## [0.2.0] - 2026-09-04

Relaunch release: one backend contract, one output format, no third-party package dependencies.

### Added
- `LLMRequest` / `LLMResponse`: the backend contract now carries prompt sections, JSON Schema, slot id, token budget and thinking policy per request. One `BehaviorLLMClient` can serve any number of agents with different constraints; the old shared-backend "last writer wins" problem is gone.
- `DecisionProfile` on `LLMAgent`: `Reactive` (default: no thinking, no reason field, small token budget) or `Deliberative` (capped `reason` field before the action and/or a native thinking token budget). Thinking is decided per agent and per request, never server-wide.
- `ActionSchemaBuilder`: one `oneOf` branch per action, so each action can constrain its argument to an enum (`AgentActionDef.allowedArguments`, or runtime values from an `IArgumentOptionsProvider` component). Property order is `reason` (optional), `action`, `arg`.
- `BehaviorLLMClient.Transport`: `ChatCompletions` (default, `/v1/chat/completions` with `response_format: json_schema`, `chat_template_kwargs.enable_thinking`, `id_slot`, reasoning returned separately) or `RawCompletion` (`/completion`, no chat template).
- `BehaviorLLMServer`: `parallelSlots` (`-np`), `cacheReuse` (`--cache-reuse`), `useJinja` (`--jinja`), `extraArguments`; falls back to a `llama-server` found on `PATH` when none is installed under StreamingAssets; `BaseUrl` property.
- `IBudgetedObservation` (`Kind` + `GetObservation(maxEntries)`), implemented by `ModularVisionModule` (nearest N) and `BasicMemory` (most recent N). `ObservationComposer.Compose` budgets by kind instead of topic-name string matching.
- `DecisionParser` strips both native-thinking tag families (`<think>` and Gemma 4's `<|channel>thought`), tolerates prose and fences, and handles braces inside strings.
- Telemetry: `promptTokens`, `completionTokens`, `cachedTokens`, `reason`, `profile`, `structuredOutput`.
- `LLMAgent.RebuildSchema()` to refresh prompt and schema when the ActionConfig or argument options change at runtime.
- Tests: `ActionSchemaBuilderTests`, `DecisionParserTests`, `PromptBuilderTests`, `LlamaRequestWriterTests`, `ClientResponseParsingTests` (Runtime), `EditorAssemblyGraphTests` (Editor).

### Changed
- **BREAKING -** `ILLMBackend.CompleteAsync(string, CancellationToken)` is now `CompleteAsync(LLMRequest, CancellationToken)` returning `LLMResponse`.
- **BREAKING -** `LLMAgent` output is JSON only. `OutputFormat`, `DecisionParseMode`, `useGrammar`, `compactPromptEnabled` and every `strict*` toggle are gone; `useStructuredOutput` (default on) replaces `useGrammar`, and the prompt budget fields (`maxVisionEntries`, `maxMemoryEntries`, `maxPromptChars`, 0 = unlimited) replace compact mode.
- **BREAKING -** `BehaviorLLMClient.url` is now `baseUrl` (server root, e.g. `http://localhost:8080`; a legacy full endpoint URL is normalised at Awake). `grammar` / `jsonSchema` fields removed. `temperature` default 0.2, `nPredict` is now a ceiling (default 256).
- **BREAKING -** `BehaviorLLMServer.CompletionEndpoint` now returns the chat-completions URL.
- `LLMAgentDecisionTelemetry.parseMode` / `strictModeEnabled` replaced by `profile` / `structuredOutput`.
- `ActionConfig.modelInstructions` default text no longer references the `Action(ID)` signature.
- Prompt: the system prompt (persona, contract, action menu with allowed arguments, examples, guide) is built once and cached; the STATE block is the only per-decision part. Trimming drops observation lines from the end and never touches the system prompt.
- The code comment that cited arXiv 2505.15146 for prefix caching now cites Prompt Cache (Gim et al., MLSys 2024).

### Removed
- **BREAKING -** `IGrammarCapableBackend`, `ActionGrammarBuilder` (GBNF) and `ActionJsonSchemaBuilder` (replaced by `ActionSchemaBuilder`).
- **BREAKING -** Plain output mode and both regex parsers (`LegacyFlexible`, `StrictSignature`).

## [0.1.1] - 2026-09-03

### Added
- `ActionJsonSchemaBuilder` and `IGrammarCapableBackend.SetActionJsonSchema` for JSON-Schema-constrained decoding. When `OutputFormat=Json` and `useGrammar=true`, `LLMAgent` builds a schema from `ActionConfig` and pushes it to the backend, which forwards it as llama-server's `json_schema` field. `BehaviorLLMClient` switched to a hand-rolled JSON request builder so the schema inlines as a nested object rather than a stringified field. Pushed the urban-incident-response sample to ~95% strict parse on `Qwen_Qwen3.5-2B-Q4_K_M.gguf`.
- `OutputFormat.Json` end-to-end path on `LLMAgent`: `JsonDecision` payload, `ExecuteActionJson`, `TryExtractJsonObject`. The few-shot block and prompt lead (`OUTPUT: `) are derived from this format.
- `SelfObservationModule` component for explicit self-perception. Add it next to `LLMContextObject` on the agent root (`[RequireComponent]` handles the wiring).
- `LLMAgent.RebuildBindingLookup()` public API. Call after mutating `actionBindings` at runtime so the cached dispatch dictionary picks up the change.
- `BehaviorLLMServer.GrammarLoadFailed` latch. Set the first time llama-server logs `failed to parse grammar`; clients then drop the `grammar` / `json_schema` field for subsequent requests to avoid the per-call parse-failure cost. Resets on `StartServer()`.
- `WarnOnBindingSchemaMismatch` diagnostic. Logs at `Awake` when `actionBindings` and `ActionConfig.validActions` diverge, in either direction.
- `ModularVisionModule.occluderLayers` LayerMask field. Cone mode raycasts against this mask for line-of-sight; leave at `Nothing` to skip LOS entirely.
- `IVisionStrategy.Scan` signature: now takes `perceptionLayers` and `occluderLayers` separately.
- `ActionGrammarBuilder` GBNF builder (Plain output mode), `IGrammarCapableBackend.SetActionGrammar`, dump-to-StreamingAssets helpers for out-of-band debugging.
- `AgentActionDef.exampleArgument` so per-action sample arguments in the few-shot block come from the schema instead of a hard-coded sample-specific list.
- O(1) action dispatch via `bindingsByName` cache (built in `Awake`, refreshable via `RebuildBindingLookup`).
- `CancellationToken` plumbed through `ILLMBackend.CompleteAsync`; in-flight `UnityWebRequest`s are aborted when the agent is disabled or destroyed.
- Identity-set interrupts on `ModularVisionModule` (was: count-change heuristic). New `scanInterval` throttle (default 0.2s) for the underlying `Physics.OverlapSphere`.
- Test assemblies: `BehaviorLLM.Tests.Runtime` (EditMode + PlayMode, covers `ActionJsonSchemaBuilder`) and `BehaviorLLM.Tests.Editor` (EditMode only, covers `ActionGrammarBuilder` and verifies the Runtime + Editor reference graph). Both gated by `UNITY_INCLUDE_TESTS`. `package.json` now lists `testables`.

### Changed
- **BREAKING — `LLMContextObject` no longer implements `IObservationModule`.** The `isObservationSource` boolean and `topicName` field are removed. Self-perception moved to the new `SelfObservationModule`. Existing prefabs/scenes that toggled the boolean need a `SelfObservationModule` added next to the context object.
- **BREAKING — `ModularVisionModule.layers` renamed to `perceptionLayers`** (`[FormerlySerializedAs("layers")]` covers the migration; resave scenes before that attribute is removed in a future release).
- **BREAKING — `BehaviorLLMServer.Instance` static singleton removed.** Clients resolve the server via a serialized `server` field (set in inspector or auto-found at `Awake` via `FindFirstObjectByType<BehaviorLLMServer>`). `DontDestroyOnLoad` is no longer called by the server — persistence across scenes is now an explicit decision.
- **BREAKING — Cone vision LOS now uses the explicit `occluderLayers` mask.** Previously raycasted against `~perceptionLayers`, which treated every collider outside the perception mask (including triggers) as an occluder. Set `occluderLayers` to your wall/world geometry to restore LOS; leave at `Nothing` for pure FoV behavior.
- `ConstructPrompt` now ends with `OUTPUT: ` (Json mode) or `What is your action?: ` (Plain mode). `DECISION:` survives only as a legacy fallback in `TrimPromptToMaxChars` and in few-shot example bodies.
- `TrimPromptToMaxChars` walks back to the last newline before the completion lead, so a budget-trim never leaves the model staring at a mid-word `OUTPUT` token.
- `BehaviorLLMClient`: request body is now built manually as JSON. The `grammar` field is only sent when non-empty; the `json_schema` field takes precedence over `grammar` when both are present. `[Serializable]` attribute on the MonoBehaviour kept for inspector clarity.
- `BehaviorLLMServer` Awake no longer calls `DontDestroyOnLoad`. Place the component on a root GameObject and add your own `DontDestroyOnLoad` wrapper if you want it to survive scene reloads.
- Restructured package into UPM-compliant layout: `package.json`, separate `Runtime/` and `Editor/` assembly definitions (`BehaviorLLM.Runtime`, `BehaviorLLM.Editor`).
- Moved `BackendTester` from `Runtime/Core/Backend` into `Samples~/SmokeTest` so it ships as an opt-in sample instead of always-compiled runtime code. Sample now uses `FindFirstObjectByType<BehaviorLLMServer>` instead of the removed singleton.
- Renamed `Documentation/` to `Documentation~/` so design docs ship with the package but are excluded from Unity's asset import.

### Removed
- **BREAKING -** LLMUnity integration: `LLMUnityClient`, the `LLMUNITY_AVAILABLE` define, the Runtime asmdef reference to `undream.llmunity.Runtime`, and the `Tools > BehaviorLLM > Migrate LLMUnity v3 Serialization` menu. The package now has no third-party package dependencies; use `BehaviorLLMClient` against a local `llama-server` (managed by `BehaviorLLMServer` or started manually).
- `BehaviorLLMServer.Instance` static singleton (see Changed).
- `LLMContextObject.isObservationSource`, `LLMContextObject.topicName`, `LLMContextObject` implementing `IObservationModule` (see Changed).
- Dead `LLMAgent.BuildNoArgExampleAction()` / `BuildWithArgExampleAction()` private helpers (replaced by the `ExampleParts` builders).

### Fixed
- llama-server `failed to parse grammar` rejection for the JSON GBNF (single-letter rule names, embedded escaped quotes). Replaced by the JSON-Schema path entirely.
- Pre-existing `UrbanSetAReliabilityTests` regression after the O(1) lookup refactor: the fixture mutated `actionBindings` post-`Awake` so dispatch saw an empty cache. Fixed by calling `RebuildBindingLookup()` from the fixture.

## [0.1.0] - Initial pre-release

### Added
- `LLMAgent` orchestrator with Sense–Think–Act loop and reflex interrupt system.
- `ActionConfig` ScriptableObject for declaring discrete agent action sets.
- `ModularVisionModule` with sphere/cone/global vision strategies, line-of-sight checks, and interrupt-on-new-object detection.
- `LLMContextObject` for tagging perceivable game objects with semantic data and reflection-based dynamic bindings.
- `BasicMemory` short-term event log module.
- `BehaviorLLMClient` HTTP backend implementing `ILLMBackend` for any OpenAI- or llama.cpp-compatible server.
- `BehaviorLLMServer` optional component that manages a `llama-server` child process.
- `LLMUnityClient` adapter for the [LLMUnity](https://github.com/undreamai/LLMUnity) package as backend.
- Editor migration utility for LLMUnity v3 serialization (`Tools > BehaviorLLM > Migrate LLMUnity v3 Serialization`).
