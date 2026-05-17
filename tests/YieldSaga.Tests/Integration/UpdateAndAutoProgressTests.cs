namespace YieldSaga.Tests.Integration;

/// <summary>
/// Spec §7 end-to-end:
/// - Runtime.Update が IIntentProducer 経由で Intent を作って Dispatch を回す
/// - Update 完了後に IAutoIntentProducer の自走ループが fixed-point まで回る
/// - Take 中断と Resume が Update 中の残り Intent と自走を引き継ぐ
/// - Tick は外部入力なしで自走だけを回す
/// </summary>
public class UpdateAndAutoProgressTests
{
    private sealed record S(int Value, bool AutoOn);
    private sealed record Pulse(int By);                       // 外部入力
    private sealed record Sys(string Name) : Sender;

    private sealed record AddIntent(int By) : Intent;
    private sealed record AddedEvent(int By) : Event;

    private sealed class AddSaga : ISaga<AddIntent>
    {
        public IEnumerable<Effect> Run(AddIntent intent, Sender sender)
        {
            yield return new AddedEvent(intent.By);
        }
    }

    private sealed class AddApplier : IEventApplier<S, AddedEvent>
    {
        public S Apply(S state, AddedEvent ev) => state with { Value = state.Value + ev.By };
    }

    // 入力 Pulse を AddIntent 2 つに分解する Producer
    private sealed class PulseProducer : IIntentProducer<Pulse, S>
    {
        public bool CanProduce(Pulse input, S state) => true;
        public IEnumerable<Intent> Produce(Pulse input, IStateProvider<S> state)
        {
            yield return new AddIntent(input.By);
            yield return new AddIntent(input.By);
        }
    }

    // Value < 10 のあいだ +1 し続ける Auto Producer
    private sealed class AutoFillUntilTen : IAutoIntentProducer<S>
    {
        public bool CanProduce(S state) => state.AutoOn && state.Value < 10;
        public IEnumerable<Intent> Produce(IStateProvider<S> state)
        {
            yield return new AddIntent(1);
        }
    }

    private static Runtime<S> Build(
        S initial,
        IntentProducerRegistry<S>? producers = null,
        AutoIntentProducerRegistry<S>? autoProducers = null,
        SagaRegistry? extraSagas = null)
    {
        var sagas = extraSagas ?? new SagaRegistry();
        if (extraSagas is null) sagas.Register(new AddSaga());
        var appliers = new EventApplierRegistry<S>();
        appliers.Register(new AddApplier());
        return new Runtime<S>(
            initial,
            new EffectPipeline(sagas),
            appliers,
            producers,
            autoProducers);
    }

    [Fact]
    public void Update_runs_producer_intents_in_order()
    {
        var producers = new IntentProducerRegistry<S>();
        producers.Register(new PulseProducer());
        var rt = Build(new S(0, AutoOn: false), producers);

        var trace = rt.Update(new Pulse(3), new Sys("t"));

        Assert.Equal(2, trace.Count);
        Assert.Equal(new AddedEvent(3), trace[0].Effect);
        Assert.Equal(new AddedEvent(3), trace[1].Effect);
        Assert.Equal(6, rt.Latest.Value);
    }

    [Fact]
    public void Update_without_producer_registry_throws()
    {
        var rt = Build(new S(0, false));
        Assert.Throws<InvalidOperationException>(() => rt.Update(new Pulse(1), new Sys("t")));
    }

    [Fact]
    public void Update_with_unregistered_input_type_throws()
    {
        var producers = new IntentProducerRegistry<S>();
        producers.Register(new PulseProducer());
        var rt = Build(new S(0, false), producers);

        Assert.Throws<InvalidOperationException>(() => rt.Update("not a pulse", new Sys("t")));
    }

    [Fact]
    public void Update_when_no_producer_matches_throws()
    {
        var producers = new IntentProducerRegistry<S>();
        producers.Register(new RejectingProducer());
        var rt = Build(new S(0, false), producers);

        Assert.Throws<InvalidOperationException>(() => rt.Update(new Pulse(1), new Sys("t")));
    }

    private sealed class RejectingProducer : IIntentProducer<Pulse, S>
    {
        public bool CanProduce(Pulse input, S state) => false;
        public IEnumerable<Intent> Produce(Pulse input, IStateProvider<S> state) => Array.Empty<Intent>();
    }

    [Fact]
    public void Update_drives_auto_loop_to_fixed_point_after_intents()
    {
        var producers = new IntentProducerRegistry<S>();
        producers.Register(new PulseProducer());
        var auto = new AutoIntentProducerRegistry<S>();
        auto.Register(new AutoFillUntilTen());

        // 初期 0、Pulse(1) で +1 +1 → 2、その後 Auto が Value<10 のあいだ +1 を吐き続けて 10 で止まる
        var rt = Build(new S(0, AutoOn: true), producers, auto);
        var trace = rt.Update(new Pulse(1), new Sys("t"));

        Assert.Equal(10, rt.Latest.Value);
        // Pulse(1) で 2 つ + Auto で 8 つ = 10 Effect
        Assert.Equal(10, trace.Count);
        Assert.All(trace, t => Assert.IsType<AddedEvent>(t.Effect));
    }

    [Fact]
    public void Tick_runs_auto_only()
    {
        var auto = new AutoIntentProducerRegistry<S>();
        auto.Register(new AutoFillUntilTen());
        var rt = Build(new S(3, AutoOn: true), producers: null, autoProducers: auto);

        var trace = rt.Tick(new Sys("auto"));

        Assert.Equal(10, rt.Latest.Value);
        Assert.Equal(7, trace.Count); // 3 → 10
    }

    [Fact]
    public void Tick_with_no_auto_registry_is_noop()
    {
        var rt = Build(new S(0, true));
        var trace = rt.Tick(new Sys("auto"));
        Assert.Empty(trace);
        Assert.Equal(0, rt.Latest.Value);
    }

    [Fact]
    public void Auto_loop_stops_when_no_producer_matches()
    {
        var auto = new AutoIntentProducerRegistry<S>();
        auto.Register(new AutoFillUntilTen());
        var rt = Build(new S(5, AutoOn: false), autoProducers: auto);  // AutoOn=false で gate

        var trace = rt.Tick(new Sys("t"));

        Assert.Empty(trace);
        Assert.Equal(5, rt.Latest.Value);
    }

    // ─── Take 中断と Resume の引き継ぎ ───────────────────────────────────────

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
            yield return new AddedEvent((int)take.ResolvedInput!);
        }
    }

    // Pulse(N) → [AskIntent, AddIntent(99)] を吐く Producer
    private sealed class AskThenAddProducer : IIntentProducer<Pulse, S>
    {
        public bool CanProduce(Pulse input, S state) => true;
        public IEnumerable<Intent> Produce(Pulse input, IStateProvider<S> state)
        {
            yield return new AskIntent();
            yield return new AddIntent(99);
        }
    }

    private static Runtime<S> BuildWithAsk(
        S initial,
        IntentProducerRegistry<S>? producers = null,
        AutoIntentProducerRegistry<S>? autoProducers = null)
    {
        var sagas = new SagaRegistry();
        sagas.Register(new AddSaga());
        sagas.Register(new AskSaga());
        var appliers = new EventApplierRegistry<S>();
        appliers.Register(new AddApplier());
        return new Runtime<S>(
            initial,
            new EffectPipeline(sagas),
            appliers,
            producers,
            autoProducers);
    }

    [Fact]
    public void Update_pausing_on_Take_preserves_remaining_intents()
    {
        var producers = new IntentProducerRegistry<S>();
        producers.Register(new AskThenAddProducer());
        var rt = BuildWithAsk(new S(0, false), producers);

        var first = rt.Update(new Pulse(0), new Sys("t"));

        // AskIntent の Take で中断。次の AddIntent(99) は未消化
        Assert.True(rt.IsPaused);
        Assert.Single(first);
        Assert.IsType<Take>(first[0].Effect);
        Assert.Equal(0, rt.Latest.Value);
    }

    [Fact]
    public void Resume_after_Update_pause_continues_remaining_intents()
    {
        var producers = new IntentProducerRegistry<S>();
        producers.Register(new AskThenAddProducer());
        var rt = BuildWithAsk(new S(0, false), producers);
        rt.Update(new Pulse(0), new Sys("t"));

        var resumed = rt.Resume(5);

        Assert.False(rt.IsPaused);
        // Resume の Drain で AddedEvent(5)、その後 pending の AddIntent(99) で AddedEvent(99)
        Assert.Equal(2, resumed.Count);
        Assert.Equal(new AddedEvent(5), resumed[0].Effect);
        Assert.Equal(new AddedEvent(99), resumed[1].Effect);
        Assert.Equal(104, rt.Latest.Value);
    }

    [Fact]
    public void Resume_after_Update_pause_also_runs_auto_loop()
    {
        var producers = new IntentProducerRegistry<S>();
        producers.Register(new AskThenAddProducer());
        var auto = new AutoIntentProducerRegistry<S>();
        auto.Register(new AutoFillUntilTen());
        var rt = BuildWithAsk(new S(0, AutoOn: true), producers, auto);
        rt.Update(new Pulse(0), new Sys("t"));

        var resumed = rt.Resume(3);

        Assert.False(rt.IsPaused);
        // Resume → AddedEvent(3) → Value=3 → AddedEvent(99) → Value=102 → Auto は Value>=10 で gate 閉
        Assert.Equal(2, resumed.Count);
        Assert.Equal(102, rt.Latest.Value);
    }

    // Auto が引き起こした Intent の途中で Take になるパターン
    private sealed class AutoAskOnceProducer : IAutoIntentProducer<S>
    {
        public bool CanProduce(S state) => state.Value == 0;
        public IEnumerable<Intent> Produce(IStateProvider<S> state)
        {
            yield return new AskIntent();
        }
    }

    [Fact]
    public void Resume_after_auto_loop_Take_pause_continues_auto_loop()
    {
        // 自走で AskIntent → Take pause、Resume 後にも自走が CanProduce=true ならまた走る
        var auto = new AutoIntentProducerRegistry<S>();
        auto.Register(new AutoAskOnceProducer());

        var rt = BuildWithAsk(new S(0, false), autoProducers: auto);

        var t1 = rt.Tick(new Sys("t"));
        Assert.True(rt.IsPaused);
        Assert.Single(t1);
        Assert.IsType<Take>(t1[0].Effect);

        // 7 を入れて Resume → AddedEvent(7) → Value=7、Auto は Value!=0 で gate 閉 → 終了
        var t2 = rt.Resume(7);
        Assert.False(rt.IsPaused);
        Assert.Equal(new AddedEvent(7), t2.Single().Effect);
        Assert.Equal(7, rt.Latest.Value);
    }

    [Fact]
    public void Update_while_paused_throws()
    {
        var producers = new IntentProducerRegistry<S>();
        producers.Register(new AskThenAddProducer());
        var rt = BuildWithAsk(new S(0, false), producers);
        rt.Update(new Pulse(0), new Sys("t"));

        Assert.Throws<InvalidOperationException>(() => rt.Update(new Pulse(0), new Sys("t")));
    }

    [Fact]
    public void Tick_while_paused_throws()
    {
        var producers = new IntentProducerRegistry<S>();
        producers.Register(new AskThenAddProducer());
        var rt = BuildWithAsk(new S(0, false), producers);
        rt.Update(new Pulse(0), new Sys("t"));

        Assert.Throws<InvalidOperationException>(() => rt.Tick(new Sys("t")));
    }

    [Fact]
    public void Producer_sees_live_state_via_StateProvider()
    {
        // Producer の Produce が遅延列挙し、間の State 更新を読めるか
        var producers = new IntentProducerRegistry<S>();
        producers.Register(new EchoStateProducer());
        var rt = Build(new S(0, false), producers);

        var trace = rt.Update(new Pulse(0), new Sys("t"));

        // 1 つ目: 入力時点の State.Value=0 → AddedEvent(10)
        // 2 つ目: 1 つ目を反映後の State.Value=10 → AddedEvent(20) (前回 +10)
        Assert.Equal(2, trace.Count);
        Assert.Equal(new AddedEvent(10), trace[0].Effect);
        Assert.Equal(new AddedEvent(20), trace[1].Effect);
        Assert.Equal(30, rt.Latest.Value);
    }

    private sealed class EchoStateProducer : IIntentProducer<Pulse, S>
    {
        public bool CanProduce(Pulse input, S state) => true;
        public IEnumerable<Intent> Produce(Pulse input, IStateProvider<S> state)
        {
            yield return new AddIntent(state.Current.Value + 10);  // 0 → +10
            yield return new AddIntent(state.Current.Value + 10);  // 10 → +20
        }
    }
}
