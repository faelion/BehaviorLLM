<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset=".github/logo_dark.svg">
    <img src=".github/logo.svg" height="240" alt="BehaviorLLM">
  </picture>
</p>

<h3 align="center">Local AI models as game decision makers, inside Unity.</h3>

<p align="center">
  <a href="https://github.com/faelion/BehaviorLLM/releases/latest"><img alt="release" src="https://img.shields.io/github/v/tag/faelion/BehaviorLLM?label=release&color=2F5FA6"></a>
  <img alt="unity" src="https://img.shields.io/badge/unity-6000.0%2B-2F5FA6?logo=unity&logoColor=white">
  <img alt="tests" src="https://img.shields.io/badge/tests-315%20passing-3A8A5E">
  <a href="LICENSE.md"><img alt="license" src="https://img.shields.io/badge/license-MIT-3A8A5E"></a>
  <a href="https://github.com/faelion/BehaviorLLM/stargazers"><img alt="stars" src="https://img.shields.io/github/stars/faelion/BehaviorLLM?color=C8701E"></a>
</p>

<p align="center">
  <a href="https://youtu.be/eMRL88Fxa88">Demo (69 s)</a> ·
  <a href="#install">Install</a> ·
  <a href="#quick-start">Quick start</a> ·
  <a href="Documentation~/USER_GUIDE.md">User guide</a> ·
  <a href="CHANGELOG.md">Changelog</a> ·
  <a href="https://github.com/faelion/BehaviorLLM/discussions">Discussions</a>
</p>

<p align="center">
  <img src=".github/hero.gif" width="800" alt="Three guards patrolling a compound at night; Guard_03 spots the intruder in its vision cone and gives chase">
</p>

**BehaviorLLM** lets a game object choose what to do next using a model running on your own machine. You list the actions it can take and what it can perceive; the package asks the model, makes sure the answer is one of your actions, and calls your code.

It works for characters (an enemy, a companion) and for game systems (a director adjusting difficulty), which is why the component is called a **Decision Maker**.

- **Local.** Runs against [llama.cpp](https://github.com/ggml-org/llama.cpp)'s `llama-server`. No cloud, no API keys.
- **Always a legal action.** The model can only pick from the actions you defined.
- **No package dependencies.** Only the samples need AI Navigation and the Input System.
- **Inspectable.** The Prompt Inspector shows every decision with exactly what was sent and returned.

## Install

In Unity, open **Window > Package Manager > + > Add package from git URL…** and paste:

```text
https://github.com/faelion/BehaviorLLM.git#release
```

That is the stable channel. Use `#main` for the latest development version, or a tag such as `#v0.7.0` to pin a release.

Requires Unity 6. The package downloads the model and the server for you.

## Quick start

**1. Get a model and the server.** Open **BehaviorLLM > Model Manager**:
- In the **Catalog** tab, download **LiquidAI d1 3B** (the default model).
- In the **Server** tab, install `llama-server`.

<p align="center"><img src=".github/model-catalog.png" width="640" alt="The Model Manager window"></p>

**2. Try a sample.** Run **BehaviorLLM > Samples > Build StealthGuard Scene** and press Play.

**3. Make your own decision maker.**
1. Add `DecisionMaker` and `BehaviorLLMClient` to a GameObject.
2. Create an **Action Config** (*Create > BehaviorLLM > Action Config*) and list the actions it can take.
3. Assign it. **Action Bindings** fills in one entry per action; hook each one to your code.
4. Add what it perceives: `ModularVisionModule` for sight, `BasicMemory` for its recent actions. Mark the objects it should see with `LLMContextObject`.
5. Press Play.

<p align="center">
  <img src=".github/action-config.png" width="360" alt="An ActionConfig asset with five actions and the designer's guidance">
  &nbsp;&nbsp;
  <img src=".github/inspector.png" width="360" alt="The DecisionMaker inspector with generated action bindings">
</p>

**4. Watch it decide.** Open **BehaviorLLM > Prompt Inspector** to see each decision, its latency, and the exact request and reply.

<p align="center"><img src=".github/prompt-inspector.png" width="640" alt="The Prompt Inspector during a run"></p>

The [user guide](Documentation~/USER_GUIDE.md) covers every step in detail.

## Two kinds of model

You choose one per project, from the Model Manager.

| | Decision model (default) | Generative LLM |
|---|---|---|
| Examples | LiquidAI d1, Kev | Qwen3.5, Granite |
| How it decides | Scores every legal action and picks the most likely | Writes a JSON answer, held to your actions by a grammar |
| Written reason | No | Optional |

Measured on 262 situations recorded in the two samples (RX 6650 XT 8 GB):

| Model | Kind | Correct action | Median latency | Video memory |
|---|---|---:|---:|---:|
| Kev 9B Artemis | Decision | **86%** | 735 ms | 5.6 GB |
| **LiquidAI d1 3B** (default) | Decision | 79% | **191 ms** | **2.2 GB** |
| Kev 4B | Decision | 79% | 436 ms | 3.9 GB |
| Qwen3.5 4B | Generative | 72% | 552 ms | 3.8 GB |
| Granite 4.1 3B | Generative | 66% | 377 ms | 3.5 GB |

d1 is the default because it is accurate, fast and small: with eight characters on one GPU it still answers in about a quarter of a second. Scripts and data are in the repository's experiment notes.

## Samples

<p align="center">
  <img src=".github/stealthguard.png" width="400" alt="StealthGuard: three guards, vision fans, an intruder">
  &nbsp;&nbsp;
  <img src=".github/prisonyard.png" width="400" alt="PrisonYard: six sections, eight decision makers">
</p>

- **StealthGuard.** Three guards patrol a compound and chase what they see. Start here.
- **PrisonYard.** Eight decision makers on one server: guards, prisoners and a warden.

Build them from **BehaviorLLM > Samples**. They need `com.unity.ai.navigation` and `com.unity.inputsystem`. **BehaviorLLM > Readme** removes them when you are done.

## How it works

<p align="center"><img src=".github/architecture.svg" width="760" alt="Block diagram: observation modules, DecisionMaker, backend, bindings and configuration assets"></p>

1. **Observe.** Observation modules turn what the character perceives into text.
2. **Decide.** `DecisionMaker` sends that, with the legal actions, to the local server.
3. **Act.** The chosen action runs the code bound to it. If something goes wrong, an optional fallback action runs instead.

Settings live in config assets, not on components, so one asset can tune many characters. See the [architecture notes](Documentation~/BehaviorLLM_Architecture_Analysis.md) for the full design.

## Documentation

- [User guide](Documentation~/USER_GUIDE.md): setup, every component, troubleshooting
- [Architecture](Documentation~/BehaviorLLM_Architecture_Analysis.md): design and data flow
- [Samples](Samples/README.md): what each sample shows
- [Experiments](Experiments~/README.md): how the numbers were measured
- [Changelog](CHANGELOG.md)

## Contributing

Questions and showcases go in [Discussions](https://github.com/faelion/BehaviorLLM/discussions), bugs in [Issues](https://github.com/faelion/BehaviorLLM/issues) (the Prompt Inspector's **Copy** button gives everything needed to reproduce a bad decision). Pull requests are welcome; see [CONTRIBUTING.md](CONTRIBUTING.md).

## License

MIT. Sample art is CC0: [KayKit](Samples/PrisonYard/Art/LICENSES) in PrisonYard, [Kenney](Samples/StealthGuard/Art/LICENSES) in StealthGuard.
