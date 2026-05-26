namespace YieldSaga.Tests.Integration;

/// <summary>
/// Spec §7 end-to-end:
/// - Runtime.Update が IIntentProducer 経由で **単一 Intent** を作って Dispatch する
/// - Update 完了後に IAutoIntentProducer の自走ループが fixed-point まで回る
/// - Take 中断と Resume が自走を引き継ぐ
/// - Tick は外部入力なしで自走だけを回す
///
/// 単一 Intent narrow: チェーン展開 (1 個 dispatch → state 見て次) は **Saga が Query + 多 Event yield** で表現する。
/// Producer は起点 Intent を 1 個吐くだけ。
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

    // 入力 Pulse(N) → AddIntent(N) を 1 個吐く (展開は Saga 側ではなく単純 1:1)
    private sealed class PulseProducer : IIntentProducer<Pulse, S>
    {
        public bool CanProduce(Pulse input, S state) => true;
        public Intent Produce(Pulse input, S state) => new AddIntent(input.By);
    }

    // Value < 10 のあいだ +1 し続ける Auto Producer
    private sealed class AutoFillUntilTen : IAutoIntentProducer<S>
    {
        public bool CanProduce(S state) => state.AutoOn && state.Value < 10;
        public Intent Produce(S state) => new AddIntent(1);
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
    public void Update_dispatches_producer_intent()
    {
        var producers = new IntentProducerRegistry<S>();
        producers.Register(new PulseProducer());
        var rt = Build(new S(0, AutoOn: false), producers);

        var trace = rt.Update(new Pulse(3), new Sys("t"));

        Assert.Single(trace);
        Assert.Equal(new AddedEvent(3), trace[0].Effect);
        Assert.Equal(3, rt.Latest.Value);
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
        public Intent Produce(Pulse input, S state) => throw new NotImplementedException();
    }

    [Fact]
    public void Update_drives_auto_loop_to_fixed_point_after_intent()
    {
        var producers = new IntentProducerRegistry<S>();
        producers.Register(new PulseProducer());
        var auto = new AutoIntentProducerRegistry<S>();
        auto.Register(new AutoFillUntilTen());

        // 初期 0、Pulse(1) で +1 → 1、その後 Auto が Value<10 のあいだ +1 を吐き続けて 10 で止まる
        var rt = Build(new S(0, AutoOn: true), producers, auto);
        var trace = rt.Update(new Pulse(1), new Sys("t"));

        Assert.Equal(10, rt.Latest.Value);
        // Pulse(1) で 1 つ + Auto で 9 つ = 10 Effect
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
    // Producer narrow 後は「Saga が Take + 後続 Event を yield する」形になる。
    // Update の Take pause → Resume → autoLoop 引き継ぎ は変わらず動く。

    private sealed record AskIntent : Intent;
    private sealed record AnyIntPrompt : Prompt
    {
        public override bool Accepts(object input) => input is int;
    }

    // Take してから AddedEvent(input) と AddedEvent(99) を yield する Saga
    private sealed class AskThenAddSaga : ISaga<AskIntent>
    {
        public IEnumerable<Effect> Run(AskIntent intent, Sender sender)
        {
            yield return Read.Input<int>(out var v, new AnyIntPrompt());
            yield return new AddedEvent(v.Value);
            yield return new AddedEvent(99);
        }
    }

    private sealed class AskFromPulseProducer : IIntentProducer<Pulse, S>
    {
        public bool CanProduce(Pulse input, S state) => true;
        public Intent Produce(Pulse input, S state) => new AskIntent();
    }

    private static Runtime<S> BuildWithAsk(
        S initial,
        IntentProducerRegistry<S>? producers = null,
        AutoIntentProducerRegistry<S>? autoProducers = null)
    {
        var sagas = new SagaRegistry();
        sagas.Register(new AddSaga());
        sagas.Register(new AskThenAddSaga());
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
    public void Update_pausing_on_Take_inside_saga()
    {
        var producers = new IntentProducerRegistry<S>();
        producers.Register(new AskFromPulseProducer());
        var rt = BuildWithAsk(new S(0, false), producers);

        var first = rt.Update(new Pulse(0), new Sys("t"));

        Assert.True(rt.IsPaused);
        Assert.Single(first);
        Assert.IsType<Take<int>>(first[0].Effect);
        Assert.Equal(0, rt.Latest.Value);
    }

    [Fact]
    public void Resume_after_Update_pause_continues_saga_body()
    {
        var producers = new IntentProducerRegistry<S>();
        producers.Register(new AskFromPulseProducer());
        var rt = BuildWithAsk(new S(0, false), producers);
        rt.Update(new Pulse(0), new Sys("t"));

        var resumed = rt.Resume(5);

        Assert.False(rt.IsPaused);
        // Saga 本体続きで AddedEvent(5) + AddedEvent(99)
        Assert.Equal(2, resumed.Count);
        Assert.Equal(new AddedEvent(5), resumed[0].Effect);
        Assert.Equal(new AddedEvent(99), resumed[1].Effect);
        Assert.Equal(104, rt.Latest.Value);
    }

    [Fact]
    public void Resume_after_Update_pause_also_runs_auto_loop()
    {
        var producers = new IntentProducerRegistry<S>();
        producers.Register(new AskFromPulseProducer());
        var auto = new AutoIntentProducerRegistry<S>();
        auto.Register(new AutoFillUntilTen());
        var rt = BuildWithAsk(new S(0, AutoOn: true), producers, auto);
        rt.Update(new Pulse(0), new Sys("t"));

        var resumed = rt.Resume(3);

        Assert.False(rt.IsPaused);
        // Saga 続き: AddedEvent(3) → Value=3 → AddedEvent(99) → Value=102 → Auto は Value>=10 で gate 閉
        Assert.Equal(2, resumed.Count);
        Assert.Equal(102, rt.Latest.Value);
    }

    // Auto が引き起こした Saga の途中で Take になるパターン
    private sealed class AutoAskOnceProducer : IAutoIntentProducer<S>
    {
        public bool CanProduce(S state) => state.Value == 0;
        public Intent Produce(S state) => new AskOnceIntent();
    }

    private sealed record AskOnceIntent : Intent;

    private sealed class AskOnceSaga : ISaga<AskOnceIntent>
    {
        public IEnumerable<Effect> Run(AskOnceIntent intent, Sender sender)
        {
            yield return Read.Input<int>(out var v, new AnyIntPrompt());
            yield return new AddedEvent(v.Value);
        }
    }

    [Fact]
    public void Resume_after_auto_loop_Take_pause_continues_auto_loop()
    {
        // 自走で AskOnce → Take pause、Resume 後にも自走が CanProduce=true ならまた走る
        var auto = new AutoIntentProducerRegistry<S>();
        auto.Register(new AutoAskOnceProducer());

        var sagas = new SagaRegistry();
        sagas.Register(new AddSaga());
        sagas.Register(new AskOnceSaga());
        var appliers = new EventApplierRegistry<S>();
        appliers.Register(new AddApplier());
        var rt = new Runtime<S>(
            new S(0, false),
            new EffectPipeline(sagas),
            appliers,
            producers: null,
            autoProducers: auto);

        var t1 = rt.Tick(new Sys("t"));
        Assert.True(rt.IsPaused);
        Assert.Single(t1);
        Assert.IsType<Take<int>>(t1[0].Effect);

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
        producers.Register(new AskFromPulseProducer());
        var rt = BuildWithAsk(new S(0, false), producers);
        rt.Update(new Pulse(0), new Sys("t"));

        Assert.Throws<InvalidOperationException>(() => rt.Update(new Pulse(0), new Sys("t")));
    }

    [Fact]
    public void Tick_while_paused_throws()
    {
        var producers = new IntentProducerRegistry<S>();
        producers.Register(new AskFromPulseProducer());
        var rt = BuildWithAsk(new S(0, false), producers);
        rt.Update(new Pulse(0), new Sys("t"));

        Assert.Throws<InvalidOperationException>(() => rt.Tick(new Sys("t")));
    }

    // ─── Saga 内で動的 State read ───────────────────────────────────────
    // 旧 EchoStateProducer (Producer 内で複数 yield + state.Current で live read) は
    // 単一 Intent narrow で不可能になったが、Saga 側で Read.State<T> + 複数 yield で表現できる

    private sealed record EchoStateIntent : Intent;

    private sealed class EchoStateSaga : ISaga<EchoStateIntent>
    {
        public IEnumerable<Effect> Run(EchoStateIntent intent, Sender sender)
        {
            yield return Read.State<S>(out var s1);
            yield return new AddedEvent(s1.Value.Value + 10);   // 0 → +10 → Value=10
            yield return Read.State<S>(out var s2);
            yield return new AddedEvent(s2.Value.Value + 10);   // 10 → +20 → Value=30
        }
    }

    private sealed class EchoFromPulseProducer : IIntentProducer<Pulse, S>
    {
        public bool CanProduce(Pulse input, S state) => true;
        public Intent Produce(Pulse input, S state) => new EchoStateIntent();
    }

    [Fact]
    public void Saga_sees_live_state_via_Read_State_between_yields()
    {
        var producers = new IntentProducerRegistry<S>();
        producers.Register(new EchoFromPulseProducer());
        var sagas = new SagaRegistry();
        sagas.Register(new AddSaga());
        sagas.Register(new EchoStateSaga());
        var rt = Build(new S(0, false), producers, extraSagas: sagas);

        var trace = rt.Update(new Pulse(0), new Sys("t"));

        Assert.Equal(2, trace.Count);
        Assert.Equal(new AddedEvent(10), trace[0].Effect);
        Assert.Equal(new AddedEvent(20), trace[1].Effect);
        Assert.Equal(30, rt.Latest.Value);
    }
}
