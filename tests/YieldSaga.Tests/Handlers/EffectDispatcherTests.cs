namespace YieldSaga.Tests.Handlers;

/// <summary>
/// Spec §8: Dispatcher は Batch を順に走査し、各 effect の handler 群を並列発火、
/// 完了後 IObservable&lt;Effect&gt; に通知する。
/// </summary>
public class EffectDispatcherTests
{
    private sealed record S(int V);
    private sealed record AEvent(int N) : Event;
    private sealed record BEvent(int N) : Event;

    [Fact]
    public async Task Dispatch_invokes_matching_handlers_for_each_effect_in_order()
    {
        var reg = new EffectHandlerRegistry<S>();
        var ahandler = new RecordingHandler<AEvent>();
        reg.Register(ahandler);

        var batch = new EffectBatch<S>(new (Effect, S)[]
        {
            (new AEvent(1), new S(1)),
            (new AEvent(2), new S(3)),
        });
        await new EffectDispatcher<S>(reg).DispatchAsync(batch);

        Assert.Equal(new[] { 1, 2 }, ahandler.SeenN);
    }

    [Fact]
    public async Task Dispatch_skips_effect_with_no_handlers()
    {
        var reg = new EffectHandlerRegistry<S>();
        var ahandler = new RecordingHandler<AEvent>();
        reg.Register(ahandler);

        // BEvent には handler なし → 何もせず先へ
        var batch = new EffectBatch<S>(new (Effect, S)[]
        {
            (new AEvent(1), new S(0)),
            (new BEvent(99), new S(0)),
            (new AEvent(2), new S(0)),
        });
        await new EffectDispatcher<S>(reg).DispatchAsync(batch);

        Assert.Equal(new[] { 1, 2 }, ahandler.SeenN);
    }

    [Fact]
    public async Task Dispatch_runs_handlers_for_same_effect_in_parallel()
    {
        // 2 つの handler が「相手も入ってくるまで終わらない」barrier を共有して、
        // 直列では deadlock するが並列なら通る、というセットアップ。
        var reg = new EffectHandlerRegistry<S>();
        var barrier = new TaskCompletionSource();
        var arrived = 0;
        var arrivedLock = new object();
        var bothArrived = new TaskCompletionSource();

        async Task Wait()
        {
            int n;
            lock (arrivedLock) n = ++arrived;
            if (n == 2) bothArrived.SetResult();
            await bothArrived.Task;
        }

        reg.Register(new ActionHandler<AEvent>(async (_, _, _) => await Wait()));
        reg.Register(new ActionHandler<AEvent>(async (_, _, _) => await Wait()));

        var batch = new EffectBatch<S>(new (Effect, S)[] { (new AEvent(1), new S(0)) });
        var dispatchTask = new EffectDispatcher<S>(reg).DispatchAsync(batch);

        // 並列でなければ bothArrived は永久に来ない → タイムアウト
        await dispatchTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Dispatch_calls_inheritance_chain_handlers()
    {
        var reg = new EffectHandlerRegistry<S>();
        var aHandler = new RecordingHandler<AEvent>();
        var eventHandler = new RecordingHandler<Event>();
        reg.Register(aHandler);
        reg.Register(eventHandler);

        var batch = new EffectBatch<S>(new (Effect, S)[] { (new AEvent(7), new S(0)) });
        await new EffectDispatcher<S>(reg).DispatchAsync(batch);

        Assert.Equal(new[] { 7 }, aHandler.SeenN);
        Assert.Single(eventHandler.SeenRaw);
        Assert.IsType<AEvent>(eventHandler.SeenRaw[0]);
    }

    [Fact]
    public async Task OnEffect_observer_receives_each_effect_after_handlers_complete()
    {
        var reg = new EffectHandlerRegistry<S>();
        var seenInHandler = new List<int>();
        reg.Register(new ActionHandler<AEvent>((e, _, _) => { seenInHandler.Add(e.N); return Task.CompletedTask; }));
        var dispatcher = new EffectDispatcher<S>(reg);

        var observed = new List<Effect>();
        using var sub = dispatcher.OnEffect.Subscribe(new ListObserver(observed));

        var batch = new EffectBatch<S>(new (Effect, S)[]
        {
            (new AEvent(1), new S(0)),
            (new AEvent(2), new S(0)),
        });
        await dispatcher.DispatchAsync(batch);

        Assert.Equal(new[] { 1, 2 }, seenInHandler);
        Assert.Equal(new Effect[] { new AEvent(1), new AEvent(2) }, observed);
    }

    [Fact]
    public async Task Unsubscribe_stops_further_notifications()
    {
        var reg = new EffectHandlerRegistry<S>();
        reg.Register(new ActionHandler<AEvent>((_, _, _) => Task.CompletedTask));
        var dispatcher = new EffectDispatcher<S>(reg);

        var observed = new List<Effect>();
        var sub = dispatcher.OnEffect.Subscribe(new ListObserver(observed));
        sub.Dispose();

        var batch = new EffectBatch<S>(new (Effect, S)[] { (new AEvent(1), new S(0)) });
        await dispatcher.DispatchAsync(batch);

        Assert.Empty(observed);
    }

    [Fact]
    public async Task Handler_can_consume_consecutive_via_TakeWhileAndMark()
    {
        // damage handler が連続 AEvent を 1 回でまとめて読み、後続呼び出しで IsProcessed=true なら自前 skip
        var reg = new EffectHandlerRegistry<S>();
        var combos = new List<int[]>();
        var solo = new List<int>();
        var h = new ComboHandler(combos, solo);
        reg.Register(h);

        var batch = new EffectBatch<S>(new (Effect, S)[]
        {
            (new AEvent(1), new S(0)),
            (new AEvent(2), new S(0)),
            (new AEvent(3), new S(0)),
            (new BEvent(99), new S(0)),  // 不一致型: handler 未登録、対象外
            (new AEvent(4), new S(0)),
        });
        await new EffectDispatcher<S>(reg).DispatchAsync(batch);

        // 1,2,3 は 1 つの combo に丸められて 1 回だけ出力。4 は単独。
        Assert.Single(combos);
        Assert.Equal(new[] { 1, 2, 3 }, combos[0]);
        Assert.Equal(new[] { 4 }, solo);
    }

    private sealed class ComboHandler : IEffectHandler<AEvent, S>
    {
        private readonly List<int[]> _combos;
        private readonly List<int> _solos;
        public ComboHandler(List<int[]> combos, List<int> solos) { _combos = combos; _solos = solos; }
        public Task OnEffectAsync(AEvent effect, S state, EffectBatch<S> batch)
        {
            if (batch.IsProcessed(this, effect)) return Task.CompletedTask;
            var run = batch.TakeWhileAndMark<AEvent>(this, _ => true);
            if (run.Count > 1)
                _combos.Add(run.Select(r => r.Effect.N).ToArray());
            else
                _solos.Add(effect.N);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHandler<T> : IEffectHandler<T, S> where T : Effect
    {
        public List<int> SeenN { get; } = new();
        public List<Effect> SeenRaw { get; } = new();
        public Task OnEffectAsync(T effect, S state, EffectBatch<S> batch)
        {
            SeenRaw.Add(effect);
            if (effect is AEvent a) SeenN.Add(a.N);
            else if (effect is BEvent b) SeenN.Add(b.N);
            return Task.CompletedTask;
        }
    }

    private sealed class ActionHandler<T> : IEffectHandler<T, S> where T : Effect
    {
        private readonly Func<T, S, EffectBatch<S>, Task> _fn;
        public ActionHandler(Func<T, S, EffectBatch<S>, Task> fn) { _fn = fn; }
        public Task OnEffectAsync(T effect, S state, EffectBatch<S> batch) => _fn(effect, state, batch);
    }

    private sealed class ListObserver : IObserver<Effect>
    {
        private readonly List<Effect> _list;
        public ListObserver(List<Effect> list) { _list = list; }
        public void OnNext(Effect value) => _list.Add(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}
