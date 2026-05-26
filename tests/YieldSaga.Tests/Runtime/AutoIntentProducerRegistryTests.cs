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
        public Intent Produce(State state) => new TickIntent($"{_tag}@{state.Counter}");
    }

    [Fact]
    public void TryProduce_returns_intent_from_first_matching_producer()
    {
        var reg = new AutoIntentProducerRegistry<State>();
        reg.Register(new CounterAuto("low", s => s.Counter < 5));
        reg.Register(new CounterAuto("hi", s => s.Counter >= 5));

        Assert.True(reg.TryProduce(new State(0), out var below));
        Assert.Equal(new TickIntent("low@0"), below);

        Assert.True(reg.TryProduce(new State(7), out var above));
        Assert.Equal(new TickIntent("hi@7"), above);
    }

    [Fact]
    public void Returns_false_when_no_producer_matches()
    {
        var reg = new AutoIntentProducerRegistry<State>();
        reg.Register(new CounterAuto("x", _ => false));

        Assert.False(reg.TryProduce(new State(0), out var intent));
        Assert.Null(intent);
    }

    [Fact]
    public void Empty_registry_returns_false()
    {
        var reg = new AutoIntentProducerRegistry<State>();
        Assert.False(reg.TryProduce(new State(0), out _));
    }
}
