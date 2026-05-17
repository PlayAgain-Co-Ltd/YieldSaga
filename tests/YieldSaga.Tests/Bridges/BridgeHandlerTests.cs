namespace YieldSaga.Tests.Bridges;

/// <summary>
/// Spec §10: BridgeHandler 単体の挙動。
/// 出処の effect を受けたら bridge.Bridge を呼び、各出力を宛先 Runtime.Update に流す。
/// 宛先 Dispatcher を渡したら宛先の trace も配信する。
/// </summary>
public class BridgeHandlerTests
{
    // 出処側はテストでは effect を投げる箱でしかない（state も使われない）
    private sealed record FromS;
    private sealed record FromEvent(int N) : Event;

    // 宛先側
    private sealed record ToS(int Sum);
    private sealed record AddInput(int N);
    private sealed record AddIntent(int N) : Intent;
    private sealed record AddedEvent(int N) : Event;
    private sealed record Sys(string Name) : Sender;

    private sealed class AddInputProducer : IIntentProducer<AddInput, ToS>
    {
        public bool CanProduce(AddInput input, ToS state) => true;
        public IEnumerable<Intent> Produce(AddInput input, IStateProvider<ToS> state)
        {
            yield return new AddIntent(input.N);
        }
    }

    private sealed class AddSaga : ISaga<AddIntent>
    {
        public IEnumerable<Effect> Run(AddIntent intent, Sender sender)
        {
            yield return new AddedEvent(intent.N);
        }
    }

    private sealed class AddApplier : IEventApplier<ToS, AddedEvent>
    {
        public ToS Apply(ToS state, AddedEvent ev) => state with { Sum = state.Sum + ev.N };
    }

    private sealed class DoubleBridge : IBridge<FromEvent, AddInput>
    {
        public IEnumerable<AddInput> Bridge(FromEvent effect)
        {
            // 1 effect → 2 input に増幅
            yield return new AddInput(effect.N);
            yield return new AddInput(effect.N);
        }
    }

    private sealed class EmptyBridge : IBridge<FromEvent, AddInput>
    {
        public IEnumerable<AddInput> Bridge(FromEvent effect) => Array.Empty<AddInput>();
    }

    private static Runtime<ToS> BuildToRuntime()
    {
        var sagas = new SagaRegistry();
        sagas.Register(new AddSaga());
        var appliers = new EventApplierRegistry<ToS>();
        appliers.Register(new AddApplier());
        var producers = new IntentProducerRegistry<ToS>();
        producers.Register(new AddInputProducer());
        return new Runtime<ToS>(new ToS(0), new EffectPipeline(sagas), appliers, producers);
    }

    [Fact]
    public async Task Bridge_forwards_each_output_to_destination_Update()
    {
        var to = BuildToRuntime();
        var bridge = new BridgeHandler<FromEvent, AddInput, FromS, ToS>(
            new DoubleBridge(), to, new Sys("bridge"));

        // 直接 OnEffectAsync を叩く（出処側を組み立てないテスト）
        var batch = new EffectBatch<FromS>(new (Effect, FromS)[] { (new FromEvent(7), new FromS()) });
        await bridge.OnEffectAsync(new FromEvent(7), new FromS(), batch);

        // 1 effect → 2 input → 各々 +7 → Sum=14
        Assert.Equal(14, to.Latest.Sum);
    }

    [Fact]
    public async Task Empty_bridge_output_makes_no_Update_call()
    {
        var to = BuildToRuntime();
        var bridge = new BridgeHandler<FromEvent, AddInput, FromS, ToS>(
            new EmptyBridge(), to, new Sys("bridge"));

        var batch = new EffectBatch<FromS>(new (Effect, FromS)[] { (new FromEvent(7), new FromS()) });
        await bridge.OnEffectAsync(new FromEvent(7), new FromS(), batch);

        Assert.Equal(0, to.Latest.Sum);
    }

    [Fact]
    public async Task When_destination_dispatcher_provided_destination_trace_is_dispatched()
    {
        var to = BuildToRuntime();
        var toHandlers = new EffectHandlerRegistry<ToS>();
        var observed = new List<int>();
        toHandlers.Register(new RecordingAddedHandler(observed));
        var toDispatcher = new EffectDispatcher<ToS>(toHandlers);

        var bridge = new BridgeHandler<FromEvent, AddInput, FromS, ToS>(
            new DoubleBridge(), to, new Sys("bridge"), toDispatcher);

        var batch = new EffectBatch<FromS>(new (Effect, FromS)[] { (new FromEvent(5), new FromS()) });
        await bridge.OnEffectAsync(new FromEvent(5), new FromS(), batch);

        // 2 つの AddedEvent が宛先 Dispatcher 経由で観測される
        Assert.Equal(new[] { 5, 5 }, observed);
        Assert.Equal(10, to.Latest.Sum);
    }

    private sealed class RecordingAddedHandler : IEffectHandler<AddedEvent, ToS>
    {
        private readonly List<int> _list;
        public RecordingAddedHandler(List<int> list) { _list = list; }
        public Task OnEffectAsync(AddedEvent effect, ToS state, EffectBatch<ToS> batch)
        {
            _list.Add(effect.N);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Registry_extension_infers_all_generic_args()
    {
        // RegisterBridge は registry → TFromState、bridge → TFromEffect/TToInput、
        // toRuntime → TToState を全部 inference させる
        var fromHandlers = new EffectHandlerRegistry<FromS>();
        var to = BuildToRuntime();

        fromHandlers.RegisterBridge(new DoubleBridge(), to, new Sys("b"));

        // 出処側ハンドラとして登録されているはず
        var entries = fromHandlers.Resolve(typeof(FromEvent));
        Assert.Single(entries);
        Assert.IsType<BridgeHandler<FromEvent, AddInput, FromS, ToS>>(entries[0].Handler);
    }
}
