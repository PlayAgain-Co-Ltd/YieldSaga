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
or [samples/TicTacToe](samples/TicTacToe/Program.cs) for a runnable walkthrough
(Saga / Take / Interceptor / AutoIntentProducer hand-wired without a DI container).
The same domain rewired through `Microsoft.Extensions.DependencyInjection` lives in
[samples/TicTacToe.Di](samples/TicTacToe.Di/Program.cs).

## Install

Not yet published. Once `0.1.0` ships:

```bash
dotnet add package YieldSaga
dotnet add package YieldSaga.Extensions.DependencyInjection   # optional DI integration
```

## DI integration (optional)

The core `YieldSaga` package has zero external dependencies. If you use
`Microsoft.Extensions.DependencyInjection`, the `YieldSaga.Extensions.DependencyInjection`
package adds a builder API with assembly-scan support:

```csharp
services.AddYieldSagaRuntime<TicTacToeState>(b => b
    .WithInitialState(TicTacToeState.Initial)
    .ScanFromAssemblyOf<HumanTurnSaga>());

using var sp = services.BuildServiceProvider();
var runtime = sp.GetRequiredService<Runtime<TicTacToeState>>();
```

`ScanFromAssemblyOf<T>()` picks up every concrete type in the assembly that implements any
participant interface (`ISaga<>`, `IEventApplier<TState,>`, `IInterceptor<TState,>`,
`ISagaDecorator<>`, `IIntentProducer<,TState>`, `IAutoIntentProducer<TState>`,
`IEffectHandler<,TState>`) and registers every matching generic closure on each type
(a class implementing multiple `ISaga<>` is registered for each). Types whose state argument
does not match `TState` are silently skipped, so multi-runtime apps share one scan call per
runtime. For finer-grained control, the explicit `AddSaga<T>()` / `AddEventApplier<T>()` / etc.
overloads remain available alongside `ScanFromAssembly(Assembly)`.

Sagas / Appliers / Interceptors / Producers / EffectHandlers are registered as
**Singletons** (state-less per spec §3). Scoped lifetimes are intentionally deferred to a
future version. `IBridge` is wired through `AddBridge<TFromEvent, TToInput, TToState>(...)`
and resolves the destination `Runtime<TToState>` lazily, so cyclic A↔B bridges are safe.

## Build

```bash
dotnet build
dotnet test
```

## Status

Core framework feature-complete; 190 tests passing (159 core + 31 DI).

- [x] Specification draft
- [x] Core types (`Effect` / `Event` / `Signal` / `Intent` / `Sender` / `Prompt`)
- [x] `EffectPipeline` (Call expansion + `ISagaDecorator` chain + Interceptor wiring + `[RunBefore]`/`[RunAfter]` topological sort)
- [x] `SignalScope` (per-Dispatch + per-Call の `OnTake` / `OnQuery<T>` / `OnCall<T>` 統合 handler、immutable + Merge)
- [x] `Runtime` (`Dispatch` / `Update` / `Tick` / `Resume`, Take pause/resume, `IIntentProducer`, `IAutoIntentProducer` 自走ループ)
- [x] `IInterceptor` + 4-mode auto-discrimination (Skip / Drop / Decorate / Transform / Replace) + double-fire prevention
- [x] `EffectDispatcher` / `EffectBatch` (`IObservable<Effect>` notification, `TakeWhileAndMark` 先読み)
- [x] `IBridge` (multi-Runtime composition, `BridgeHandler` 自動配線拡張)
- [x] Samples: hand-wired [tic-tac-toe](samples/TicTacToe/Program.cs) + DI version [tic-tac-toe.di](samples/TicTacToe.Di/Program.cs)
- [x] DI integration (`Microsoft.Extensions.DependencyInjection`) — Singleton lifetimes only in v0.1.0
- [ ] NuGet publish (0.1.0)

## License

[MIT](LICENSE)
