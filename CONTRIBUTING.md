# Contributing to BehaviorLLM

Thanks for taking the time to look at the source. This file is the short version: enough
to get a working environment, land a PR, and not surprise anyone reviewing it. Anything
not covered here, ask in the PR.

## Repository layout

The repo is a Unity 6000.3 project that contains the BehaviorLLM UPM package plus its
samples.

```
Assets/
  BehaviorLLM/                          UPM package (com.faelion.behaviorllm)
    Runtime/                            Runtime asmdef + core code
    Editor/                             Editor-only tooling (asset menus, builders)
    Editor/Readme/                      The Readme asset's type and inspector
    Tests/
      Runtime/                          EditMode + PlayMode tests
      Editor/                           Editor-only tests
    Samples/StealthGuard/               Three decision makers, start here
    Samples/PrisonYard/                 Eight decision makers, three roles
    Samples~/SmokeTest/                 Hidden sample (imported via Package Manager)
    Readme.asset                        Landing page, and the button that deletes the samples
  !Project/                             Harness scratch: a test scene, a demo navigator,
                                        and two unimplemented sample placeholders
```

Anything outside `Assets/BehaviorLLM/` is *not* shipped in the UPM package, so keep
package code self-contained. The two samples moved inside it on 2026-09-10 for exactly
that reason. They are the only part of the package allowed to need another Unity package,
and they ask for it through `versionDefines` plus `defineConstraints` rather than through
`package.json`, so a consumer without AI Navigation or the Input System still gets a
working package with the samples simply not compiled.

## Local setup

- **Unity 6000.3.1f1** (newer 6000.x patches likely work; older LTS versions are not
  supported because of API drift in the Test Framework and `FindFirstObjectByType`).
- **llama-server binary + a GGUF model.** Drop both under
  `Assets/StreamingAssets/models/` and point
  `Assets/StreamingAssets/behaviorllm_backend_config.json` (or the inspector fields on
  `BehaviorLLMServer`) at them. Without a model, the agent runs but every call fails the
  retry budget and the smoke sample logs a clear error.
- Add a `BehaviorLLMServer` component to a scene if you want BehaviorLLM to manage the
  process; otherwise launch llama-server yourself and point `BehaviorLLMClient.url` at it.

## Branch + commit conventions

- Branch off `main`. Use a short prefixed name: `phase-N/<topic>`, `fix/<topic>`,
  `feat/<topic>`. One feature per branch.
- Conventional Commits subjects (`feat: ...`, `fix(scope): ...`, `refactor: ...`,
  `docs: ...`, `chore: ...`). Subject under 72 chars. Body explains *why*, not *what* -
  the diff already shows the *what*.
- Never amend a commit after pushing. Add a new one and let the PR squash if reviewers
  want a clean history.

## Tests

Open `Window > General > Test Runner`, or use the Unity CLI (`unity test D:\repos\TFG --mode
EditMode`, `unity test D:\repos\TFG --mode PlayMode --filter BehaviorLLM.Tests.Runtime`; with
the Editor open, `unity command run_tests --mode ...`). The package ships two test assemblies:

- `BehaviorLLM.Tests.Runtime` - PlayMode (the assembly has no platform restriction, so the
  runner lists it under PlayMode only). Covers the pure helpers: `ActionSchemaBuilder`,
  `DecisionParser`, `PromptBuilder`, `LlamaRequestWriter`, response parsing. No model needed.
- `BehaviorLLM.Tests.Editor` - EditMode. Verifies the Runtime + Editor reference graph
  compiles cleanly.

Both assemblies use `defineConstraints: ["UNITY_INCLUDE_TESTS"]`, so they only compile
when the Test Framework is enabled (the default in the Editor; opt-in for player builds).
That keeps the package's runtime footprint tests-free when consumed via UPM.

Add a test for any new pure helper you ship. Backend-touching paths (`BehaviorLLMClient`,
`DecisionMaker.Think`) are validated end-to-end by the **experiment matrix** below, not by
unit tests, because the failure modes that matter live in the model output.

## Measured acceptance bar

`Docs/Experiments/run_matrix.py` drives llama-server over 16 hand-labelled guard scenarios,
across three models and both decision profiles, with and without the schema. It uses the
prompts and JSON Schemas the package's own `PromptBuilder` and `ActionSchemaBuilder`
produce, so it measures what actually ships.

The 2026-09-04 baseline is **100% structurally valid actions everywhere**, with
expected-action rates of 62% (Qwen3.5-2B), 81% (Granite 4.1-3B) and 81% Reactive / 94%
Deliberative (Qwen3.5-4B), at p50 latencies of 189, 278 and 382 ms. The full table and what
it means is in `PLAN.md` section 8.

A PR that changes prompt construction, output format, argument policy or the schema must
hold those numbers: re-run the matrix and put the affected rows in the PR description.

For a change you want to see rather than measure, play `BehaviorLLM/Samples/StealthGuard` and
let `DecisionTelemetryRecorder` write a run summary.

If you do not have the models, say so in the PR and a maintainer will run it for you.

## PR checklist

- [ ] One feature per branch; commits squash to a clean story.
- [ ] New runtime code has at least one test in `Tests/Runtime` or a telemetry-run
      justification.
- [ ] No new `BehaviorLLMServer.Instance`-style global singletons. Inject dependencies
      via serialized fields or scene lookups (see `BehaviorLLMClient.ResolveServer` for
      the pattern we settled on in phase 3).
- [ ] No regressions on the telemetry baseline (or explicit rationale + new baseline).
- [ ] Public API additions documented with a `<summary>` XML doc.
- [ ] `package.json` `version` bumped if the change is consumer-visible.

## Filing issues

When reporting an agent-behavior bug, include:
- The model GGUF name + quant.
- The agent's `OutputFormat` setting and whether `useGrammar` is on.
- A `runs_summary.csv` row from the failing run.
- The relevant `[Agent] Thought:` log lines that demonstrate the failure mode.

That triple - model + config + decision sample - is what we need to reproduce the bug;
without it we can usually only guess.
