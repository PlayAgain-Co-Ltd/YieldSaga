namespace YieldSaga.Tests.Runtime;

/// <summary>
/// Spec §7: 自走進行用 Producer のレジストリ動作。
/// CanProduce を満たす最初の Producer が選ばれる。
/// </summary>
public class AutoIntentProducerRegistryTests
{
    private sealed record State(int Counter);
    private sealed record TickIntent(string Tag) : Intent;

    private sealed class CounterAuto : IAutoIntentProducer<State>
    {
        private readonly Func<State, bool> _gate;
        private readonly string _tag;
        public CounterAuto(string tag, Func<State, bool> gate) { _tag = tag; _gate = gate; }
        public bool CanProduce(State state) => _gate(state);
        public IEnumerable<Intent> Produce(IStateProvider<State> state)
        {
            yield return new TickIntent($"{_tag}@{state.Current.Counter}");
        }
    }

    [Fact]
    public void TryProduce_returns_first_matching_producer_intents()
    {
        var reg = new AutoIntentProducerRegistry<State>();
        reg.Register(new CounterAuto("low", s => s.Counter < 5));
        reg.Register(new CounterAuto("hi", s => s.Counter >= 5));

        Assert.True(reg.TryProduce(new Snapshot(new State(0)), out var below));
        Assert.Equal(new TickIntent("low@0"), below.Single());

        Assert.True(reg.TryProduce(new Snapshot(new State(7)), out var above));
        Assert.Equal(new TickIntent("hi@7"), above.Single());
    }

    [Fact]
    public void Returns_false_when_no_producer_matches()
    {
        var reg = new AutoIntentProducerRegistry<State>();
        reg.Register(new CounterAuto("x", _ => false));

        Assert.False(reg.TryProduce(new Snapshot(new State(0)), out var intents));
        Assert.Empty(intents);
    }

    [Fact]
    public void Empty_registry_returns_false()
    {
        var reg = new AutoIntentProducerRegistry<State>();
        Assert.False(reg.TryProduce(new Snapshot(new State(0)), out _));
    }

    private sealed record Snapshot(State Current) : IStateProvider<State>;
}
