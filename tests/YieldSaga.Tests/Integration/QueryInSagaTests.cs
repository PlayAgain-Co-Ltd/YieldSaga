namespace YieldSaga.Tests.Integration;

/// <summary>
/// End-to-end §2 / §9: Saga が <c>Read.State&lt;TState&gt;(out var s)</c> で現 State を読み、
/// その値で分岐できる。マーカーは trace に乗らない（Handler にも届かない）。
/// </summary>
public class QueryInSagaTests
{
    private sealed record Player(int Hp);
    private sealed record Sys(string Name) : Sender;

    private sealed record DamageIntent(int Amt) : Intent;
    private sealed record DamagedEvent(int Amt) : Event;
    private sealed record LowHpEvent : Event;

    private sealed class DamageApplier : IEventApplier<Player, DamagedEvent>
    {
        public Player Apply(Player s, DamagedEvent ev) => s with { Hp = s.Hp - ev.Amt };
    }

    private sealed class LowHpApplier : IEventApplier<Player, LowHpEvent>
    {
        public Player Apply(Player s, LowHpEvent ev) => s;  // no-op (フラグ役)
    }

    /// <summary>
    /// Saga は damage を 1 回吐いたあと state を読み、Hp &lt; 10 なら LowHpEvent も吐く。
    /// 「event 適用後の最新 State」が読めることが要点。
    /// </summary>
    private sealed class DamageWithThresholdSaga : ISaga<DamageIntent>
    {
        public IEnumerable<Effect> Run(DamageIntent intent, Sender sender)
        {
            yield return new DamagedEvent(intent.Amt);
            yield return Read.State<Player>(out var s);
            if (s.Value.Hp < 10)
                yield return new LowHpEvent();
        }
    }

    private static Runtime<Player> Build(int initialHp)
    {
        var sagas = new SagaRegistry();
        sagas.Register(new DamageWithThresholdSaga());
        var appliers = new EventApplierRegistry<Player>();
        appliers.Register(new DamageApplier());
        appliers.Register(new LowHpApplier());
        return new Runtime<Player>(new Player(initialHp), new EffectPipeline(sagas), appliers);
    }

    [Fact]
    public void Saga_reads_post_event_state_via_Query()
    {
        var rt = Build(15);
        // Hp 15 → -10 で 5 → LowHp が出る
        var trace = rt.Dispatch(new DamageIntent(10), new Sys("t"));

        Assert.Equal(2, trace.Count);
        Assert.Equal(new DamagedEvent(10), trace[0].Effect);
        Assert.Equal(5, trace[0].State.Hp);
        Assert.IsType<LowHpEvent>(trace[1].Effect);
        Assert.Equal(5, rt.Latest.Hp);
    }

    [Fact]
    public void Saga_branch_takes_other_path_when_threshold_not_met()
    {
        var rt = Build(100);
        // Hp 100 → -10 で 90 → LowHp 出ない
        var trace = rt.Dispatch(new DamageIntent(10), new Sys("t"));

        Assert.Single(trace);
        Assert.Equal(new DamagedEvent(10), trace[0].Effect);
        Assert.Equal(90, rt.Latest.Hp);
    }

    [Fact]
    public void Query_marker_does_not_appear_in_trace()
    {
        var rt = Build(15);
        var trace = rt.Dispatch(new DamageIntent(10), new Sys("t"));

        Assert.DoesNotContain(trace, t => t.Effect is Query);
    }

    [Fact]
    public async Task Query_marker_is_not_dispatched_to_Handlers()
    {
        var rt = Build(15);

        // Query<Player> や Query 全般のハンドラを登録してみて、呼ばれないことを確認
        var queryHandlerCalls = 0;
        var hreg = new EffectHandlerRegistry<Player>();
        hreg.Register(new CountingHandler<Query<Player>>(() => queryHandlerCalls++));
        var dispatcher = new EffectDispatcher<Player>(hreg);

        var trace = rt.Dispatch(new DamageIntent(10), new Sys("t"));
        await dispatcher.DispatchAsync(new EffectBatch<Player>(trace));

        Assert.Equal(0, queryHandlerCalls);
    }

    private sealed class CountingHandler<T> : IEffectHandler<T, Player> where T : Effect
    {
        private readonly Action _inc;
        public CountingHandler(Action inc) { _inc = inc; }
        public Task OnEffectAsync(T effect, Player state, EffectBatch<Player> batch)
        {
            _inc();
            return Task.CompletedTask;
        }
    }

    // ─── 不一致 TState の guardrail ─────────────────────────────────────────

    private sealed record OtherState(int X);

    private sealed record WrongQueryIntent : Intent;

    private sealed class WrongQuerySaga : ISaga<WrongQueryIntent>
    {
        public IEnumerable<Effect> Run(WrongQueryIntent intent, Sender sender)
        {
            // Runtime<Player> 内で Read.State<OtherState> を yield → scope.OnQuery<Player> が拾わない
            yield return Read.State<OtherState>(out var _);
        }
    }

    [Fact]
    public void Mismatched_Query_TState_throws_with_clear_message()
    {
        var sagas = new SagaRegistry();
        sagas.Register(new WrongQuerySaga());
        var appliers = new EventApplierRegistry<Player>();
        var rt = new Runtime<Player>(new Player(100), new EffectPipeline(sagas), appliers);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            rt.Dispatch(new WrongQueryIntent(), new Sys("t")));

        Assert.Contains("Query", ex.Message);
        Assert.Contains("Player", ex.Message);
    }

    // ─── ネストした Call (深い位置から yield された Query も解決される) ───────

    private sealed record OuterIntent : Intent;
    private sealed record InnerIntent : Intent;

    private sealed class OuterSaga : ISaga<OuterIntent>
    {
        public IEnumerable<Effect> Run(OuterIntent intent, Sender sender)
        {
            yield return new DamagedEvent(5);
            yield return new Call(new InnerIntent(), sender);
        }
    }

    private sealed class InnerSaga : ISaga<InnerIntent>
    {
        public IEnumerable<Effect> Run(InnerIntent intent, Sender sender)
        {
            // ネスト先で Query を発射しても、Runtime が仕込む scope.OnQuery<Player> が再帰経由で拾う
            yield return Read.State<Player>(out var s);
            yield return new DamagedEvent(s.Value.Hp);  // 残 Hp 全部を damage に
        }
    }

    [Fact]
    public void Nested_Call_Query_is_still_resolved_by_outer_scope()
    {
        var sagas = new SagaRegistry();
        sagas.Register(new OuterSaga());
        sagas.Register(new InnerSaga());
        var appliers = new EventApplierRegistry<Player>();
        appliers.Register(new DamageApplier());
        var rt = new Runtime<Player>(new Player(20), new EffectPipeline(sagas), appliers);

        // 20 - 5 = 15、Inner で残 Hp 15 を damage に → 15 - 15 = 0
        var trace = rt.Dispatch(new OuterIntent(), new Sys("t"));

        Assert.Equal(0, rt.Latest.Hp);
        Assert.DoesNotContain(trace, t => t.Effect is Query);
        Assert.Equal(2, trace.Count);  // DamagedEvent(5), DamagedEvent(15)
    }
}
