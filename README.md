# YieldSaga

> **Status**: pre-alpha / under design.

Intent → Saga → Effect → State framework for turn-based / step-based logic in C#.

Inspired by Redux-saga, algebraic effects, and workflow engines (Temporal, etc.).
Designed for card games, board games, simulators, dialogue agents — anything with turn-like progression.

## Concept

```
Intent ──▶ EffectPipeline ──▶ Effect column ──▶ Runtime ──▶ State
                                                   │
                                                   └──▶ EffectDispatcher ──▶ EffectHandler (side effects)
```

Three layers:

| Layer | Role |
|-------|------|
| **Rewrite** (inner) | Expand `Intent` to `Event` stream via `ISaga`; intercept with `IInterceptor` |
| **Apply** (middle) | Reduce `Event` to `State` via `IEventApplier`; pause on `Take`, resume on input |
| **Observe** (outer) | Broadcast effects to `IEffectHandler` for rendering, audio, persistence |

See [Docs/Specification.md](Docs/Specification.md) for the full design,
or [samples/TicTacToe](samples/TicTacToe/Program.cs) for a runnable 230-line walkthrough
(Saga / Take / Interceptor / AutoIntentProducer all in one file).

## Install

Not yet published. Once `0.1.0` ships:

```bash
dotnet add package YieldSaga
```

## Build

```bash
dotnet build
dotnet test
```

## Status

Core framework feature-complete; 159 tests passing.

- [x] Specification draft
- [x] Core types (`Effect` / `Event` / `Signal` / `Intent` / `Sender` / `Prompt`)
- [x] `EffectPipeline` (Call expansion + `ISagaDecorator` chain + Interceptor wiring + `[RunBefore]`/`[RunAfter]` topological sort)
- [x] `SignalScope` (per-Dispatch + per-Call の `OnTake` / `OnQuery<T>` / `OnCall<T>` 統合 handler、immutable + Merge)
- [x] `Runtime` (`Dispatch` / `Update` / `Tick` / `Resume`, Take pause/resume, `IIntentProducer`, `IAutoIntentProducer` 自走ループ)
- [x] `IInterceptor` + 4-mode auto-discrimination (Skip / Drop / Decorate / Transform / Replace) + double-fire prevention
- [x] `EffectDispatcher` / `EffectBatch` (`IObservable<Effect>` notification, `TakeWhileAndMark` 先読み)
- [x] `IBridge` (multi-Runtime composition, `BridgeHandler` 自動配線拡張)
- [x] Sample: [tic-tac-toe](samples/TicTacToe/Program.cs)
- [ ] DI integration (`Microsoft.Extensions.DependencyInjection`)
- [ ] NuGet publish (0.1.0)

## License

[MIT](LICENSE)
