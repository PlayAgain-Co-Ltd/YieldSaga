namespace YieldSaga.Tests.Runtime;

/// <summary>
/// Spec §7: IIntentProducer のレジストリ動作。
/// 入力型ごとに複数登録でき、CanProduce を満たす最初の Producer が選ばれる。
/// </summary>
public class IntentProducerRegistryTests
{
    private sealed record State(int Value);
    private sealed record FooInput(int N);
    private sealed record BarInput(string S);
    private sealed record TaggedIntent(string Tag) : Intent;

    private sealed class FooProducer : IIntentProducer<FooInput, State>
    {
        private readonly Func<FooInput, State, bool> _gate;
        private readonly string _tag;
        public FooProducer(string tag, Func<FooInput, State, bool> gate)
        {
            _tag = tag; _gate = gate;
        }
        public bool CanProduce(FooInput input, State state) => _gate(input, state);
        public Intent Produce(FooInput input, State state) => new TaggedIntent($"{_tag}:{input.N}");
    }

    private sealed class BarProducer : IIntentProducer<BarInput, State>
    {
        public bool CanProduce(BarInput input, State state) => true;
        public Intent Produce(BarInput input, State state) => new TaggedIntent($"bar:{input.S}");
    }

    [Fact]
    public void TryProduce_returns_intent_from_matching_producer()
    {
        var reg = new IntentProducerRegistry<State>();
        reg.Register(new FooProducer("foo", (_, _) => true));

        var ok = reg.TryProduce(new FooInput(7), new State(0), out var intent);

        Assert.True(ok);
        Assert.Equal(new TaggedIntent("foo:7"), intent);
    }

    [Fact]
    public void First_producer_whose_CanProduce_is_true_wins()
    {
        var reg = new IntentProducerRegistry<State>();
        reg.Register(new FooProducer("first", (i, _) => i.N > 10));
        reg.Register(new FooProducer("second", (_, _) => true));

        Assert.True(reg.TryProduce(new FooInput(20), new State(0), out var hi));
        Assert.Equal(new TaggedIntent("first:20"), hi);

        Assert.True(reg.TryProduce(new FooInput(5), new State(0), out var lo));
        Assert.Equal(new TaggedIntent("second:5"), lo);
    }

    [Fact]
    public void CanProduce_can_consult_state()
    {
        var reg = new IntentProducerRegistry<State>();
        reg.Register(new FooProducer("gated", (_, s) => s.Value >= 100));

        Assert.False(reg.TryProduce(new FooInput(1), new State(0), out _));
        Assert.True(reg.TryProduce(new FooInput(1), new State(200), out _));
    }

    [Fact]
    public void Different_input_types_route_independently()
    {
        var reg = new IntentProducerRegistry<State>();
        reg.Register(new FooProducer("foo", (_, _) => true));
        reg.Register(new BarProducer());

        Assert.True(reg.TryProduce(new FooInput(1), new State(0), out var fooIntent));
        Assert.Equal(new TaggedIntent("foo:1"), fooIntent);

        Assert.True(reg.TryProduce(new BarInput("hi"), new State(0), out var barIntent));
        Assert.Equal(new TaggedIntent("bar:hi"), barIntent);
    }

    [Fact]
    public void Unregistered_input_type_returns_false()
    {
        var reg = new IntentProducerRegistry<State>();
        reg.Register(new BarProducer());

        Assert.False(reg.TryProduce(new FooInput(1), new State(0), out var intent));
        Assert.Null(intent);
    }

    [Fact]
    public void All_producers_rejecting_returns_false()
    {
        var reg = new IntentProducerRegistry<State>();
        reg.Register(new FooProducer("a", (_, _) => false));
        reg.Register(new FooProducer("b", (_, _) => false));

        Assert.False(reg.TryProduce(new FooInput(0), new State(0), out var intent));
        Assert.Null(intent);
    }
}
