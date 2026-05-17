namespace YieldSaga.Tests.Integration;

/// <summary>
/// End-to-end §7+§8: Runtime.Update が出した trace を EffectBatch にくるんで Dispatcher に通す。
/// Handler は state を観測できる（変えられないけど）。
/// </summary>
public class DispatcherEndToEndTests
{
    private sealed record Counter(int Value);
    private sealed record Sys(string N) : Sender;

    private sealed record AddIntent(int By) : Intent;
    private sealed record AddedEvent(int By) : Event;

    private sealed class AddSaga : ISaga<AddIntent>
    {
        public IEnumerable<Effect> Run(AddIntent intent, Sender sender)
        {
            yield return new AddedEvent(intent.By);
        }
    }

    private sealed class AddApplier : IEventApplier<Counter, AddedEvent>
    {
        public Counter Apply(Counter state, AddedEvent ev) => state with { Value = state.Value + ev.By };
    }

    private sealed record Pulse(int By);
    private sealed class PulseProducer : IIntentProducer<Pulse, Counter>
    {
        public bool CanProduce(Pulse input, Counter state) => true;
        public IEnumerable<Intent> Produce(Pulse input, IStateProvider<Counter> state)
        {
            yield return new AddIntent(input.By);
            yield return new AddIntent(input.By * 2);
        }
    }

    private sealed class LoggingHandler : IEffectHandler<AddedEvent, Counter>
    {
        public List<(int By, int StateAfter)> Log { get; } = new();
        public Task OnEffectAsync(AddedEvent effect, Counter state, EffectBatch<Counter> batch)
        {
            Log.Add((effect.By, state.Value));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Update_trace_drives_dispatcher_handlers_with_post_apply_state()
    {
        var sagas = new SagaRegistry();
        sagas.Register(new AddSaga());
        var appliers = new EventApplierRegistry<Counter>();
        appliers.Register(new AddApplier());
        var producers = new IntentProducerRegistry<Counter>();
        producers.Register(new PulseProducer());
        var rt = new Runtime<Counter>(new Counter(0), new EffectPipeline(sagas), appliers, producers);

        var log = new LoggingHandler();
        var hreg = new EffectHandlerRegistry<Counter>();
        hreg.Register(log);
        var dispatcher = new EffectDispatcher<Counter>(hreg);

        var trace = rt.Update(new Pulse(3), new Sys("t"));
        await dispatcher.DispatchAsync(new EffectBatch<Counter>(trace));

        Assert.Equal(9, rt.Latest.Value);
        Assert.Equal(2, log.Log.Count);
        // Handler は **適用後** の State を見る
        Assert.Equal((3, 3), log.Log[0]);
        Assert.Equal((6, 9), log.Log[1]);
    }

    [Fact]
    public async Task Take_in_trace_is_dispatchable_for_UI_handlers()
    {
        // Take も Signal として trace に乗る。Handler を Take に登録すれば UI 通知のフックに使える。
        var sagas = new SagaRegistry();
        sagas.Register(new AskSaga());
        var appliers = new EventApplierRegistry<Counter>();
        appliers.Register(new AddApplier());
        var rt = new Runtime<Counter>(new Counter(0), new EffectPipeline(sagas), appliers);

        var prompts = new List<Prompt>();
        var hreg = new EffectHandlerRegistry<Counter>();
        hreg.Register(new TakeWatcher(prompts));
        var dispatcher = new EffectDispatcher<Counter>(hreg);

        var trace = rt.Dispatch(new AskIntent(), new Sys("t"));
        await dispatcher.DispatchAsync(new EffectBatch<Counter>(trace));

        Assert.True(rt.IsPaused);
        Assert.Single(prompts);
        Assert.IsType<AnyIntPrompt>(prompts[0]);
    }

    private sealed record AskIntent : Intent;
    private sealed record AnyIntPrompt : Prompt
    {
        public override bool Accepts(object input) => input is int;
    }

    private sealed class AskSaga : ISaga<AskIntent>
    {
        public IEnumerable<Effect> Run(AskIntent intent, Sender sender)
        {
            var take = new Take(new AnyIntPrompt());
            yield return take;
        }
    }

    private sealed class TakeWatcher : IEffectHandler<Take, Counter>
    {
        private readonly List<Prompt> _prompts;
        public TakeWatcher(List<Prompt> prompts) { _prompts = prompts; }
        public Task OnEffectAsync(Take effect, Counter state, EffectBatch<Counter> batch)
        {
            _prompts.Add(effect.Prompt);
            return Task.CompletedTask;
        }
    }
}
