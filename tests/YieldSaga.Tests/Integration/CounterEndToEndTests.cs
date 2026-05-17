namespace YieldSaga.Tests.Integration;

/// <summary>
/// End-to-end: Intent → Saga → Effect → State 更新まで通っているかの最小デモ。
/// </summary>
public class CounterEndToEndTests
{
    private sealed record CounterState(int Value);
    private sealed record IncrementIntent(int By) : Intent;
    private sealed record IncrementedEvent(int By) : Event;
    private sealed record System(string Name) : Sender;

    private sealed class IncrementSaga : ISaga<IncrementIntent>
    {
        public IEnumerable<Effect> Run(IncrementIntent intent, Sender sender)
        {
            yield return new IncrementedEvent(intent.By);
        }
    }

    private sealed class IncrementApplier : IEventApplier<CounterState, IncrementedEvent>
    {
        public CounterState Apply(CounterState state, IncrementedEvent ev) =>
            state with { Value = state.Value + ev.By };
    }

    private static Runtime<CounterState> BuildRuntime(int initial = 0)
    {
        var sagas = new SagaRegistry();
        sagas.Register(new IncrementSaga());

        var appliers = new EventApplierRegistry<CounterState>();
        appliers.Register(new IncrementApplier());

        return new Runtime<CounterState>(new CounterState(initial), new EffectPipeline(sagas), appliers);
    }

    [Fact]
    public void Dispatch_runs_saga_and_updates_state()
    {
        var rt = BuildRuntime();

        var trace = rt.Dispatch(new IncrementIntent(3), new System("test"));

        var single = Assert.Single(trace);
        var (effect, state) = single;
        Assert.Equal(new IncrementedEvent(3), effect);
        Assert.Equal(3, state.Value);
        Assert.Equal(3, rt.Latest.Value);
    }

    [Fact]
    public void Sequential_dispatches_accumulate()
    {
        var rt = BuildRuntime();

        rt.Dispatch(new IncrementIntent(2), new System("t"));
        rt.Dispatch(new IncrementIntent(5), new System("t"));

        Assert.Equal(7, rt.Latest.Value);
    }

    [Fact]
    public void Dispatch_returns_state_snapshot_per_effect()
    {
        // Saga が 3 つ Event を吐くケース
        var sagas = new SagaRegistry();
        sagas.Register(new MultiIncrementSaga());
        var appliers = new EventApplierRegistry<CounterState>();
        appliers.Register(new IncrementApplier());
        var rt = new Runtime<CounterState>(new CounterState(0), new EffectPipeline(sagas), appliers);

        var trace = rt.Dispatch(new MultiIncrementIntent(), new System("t"));

        Assert.Equal(3, trace.Count);
        Assert.Equal(1, trace[0].State.Value);
        Assert.Equal(2, trace[1].State.Value);
        Assert.Equal(3, trace[2].State.Value);
    }

    [Fact]
    public void Unregistered_intent_throws()
    {
        var rt = BuildRuntime();
        Assert.Throws<InvalidOperationException>(() =>
            rt.Dispatch(new UnknownIntent(), new System("t")));
    }

    private sealed record MultiIncrementIntent : Intent;

    private sealed class MultiIncrementSaga : ISaga<MultiIncrementIntent>
    {
        public IEnumerable<Effect> Run(MultiIncrementIntent intent, Sender sender)
        {
            yield return new IncrementedEvent(1);
            yield return new IncrementedEvent(1);
            yield return new IncrementedEvent(1);
        }
    }

    private sealed record UnknownIntent : Intent;
}
