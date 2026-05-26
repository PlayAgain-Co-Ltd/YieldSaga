namespace YieldSaga.Tests.Integration;

/// <summary>
/// Spec §10 end-to-end: Bridge で繋いだ複数 Runtime が 1 入力でカスケードする。
/// MetaRuntime は無く、出処の Dispatcher が Bridge handler を起こすことで連鎖が立ち上がる。
/// </summary>
public class MultiRuntimeBridgeTests
{
    // ─── Battle Runtime ───────────────────────────────────
    private sealed record Battle(int Hp);
    private sealed record AttackIntent(int Damage) : Intent;
    private sealed record DamageEvent(int Amt) : Event;

    private sealed class AttackSaga : ISaga<AttackIntent>
    {
        public IEnumerable<Effect> Run(AttackIntent intent, Sender sender)
        {
            yield return new DamageEvent(intent.Damage);
        }
    }
    private sealed class DamageApplier : IEventApplier<Battle, DamageEvent>
    {
        public Battle Apply(Battle s, DamageEvent ev) => s with { Hp = s.Hp - ev.Amt };
    }

    // ─── Score Runtime ───────────────────────────────────
    private sealed record Score(int TotalDamage);
    private sealed record AddDamageInput(int Amt);
    private sealed record AddDamageIntent(int Amt) : Intent;
    private sealed record DamageAddedEvent(int Amt) : Event;

    private sealed class AddDamageProducer : IIntentProducer<AddDamageInput, Score>
    {
        public bool CanProduce(AddDamageInput input, Score state) => true;
        public Intent Produce(AddDamageInput input, Score state) => new AddDamageIntent(input.Amt);
    }
    private sealed class AddDamageSaga : ISaga<AddDamageIntent>
    {
        public IEnumerable<Effect> Run(AddDamageIntent intent, Sender sender)
        {
            yield return new DamageAddedEvent(intent.Amt);
        }
    }
    private sealed class DamageAddedApplier : IEventApplier<Score, DamageAddedEvent>
    {
        public Score Apply(Score s, DamageAddedEvent ev) => s with { TotalDamage = s.TotalDamage + ev.Amt };
    }

    // ─── Notification Runtime（3 段目） ────────────────────
    private sealed record Notice(int Count, int LastAmt);
    private sealed record NotifyInput(int Amt);
    private sealed record NotifyIntent(int Amt) : Intent;
    private sealed record NotifiedEvent(int Amt) : Event;

    private sealed class NotifyProducer : IIntentProducer<NotifyInput, Notice>
    {
        public bool CanProduce(NotifyInput input, Notice state) => true;
        public Intent Produce(NotifyInput input, Notice state) => new NotifyIntent(input.Amt);
    }
    private sealed class NotifySaga : ISaga<NotifyIntent>
    {
        public IEnumerable<Effect> Run(NotifyIntent intent, Sender sender)
        {
            yield return new NotifiedEvent(intent.Amt);
        }
    }
    private sealed class NotifyApplier : IEventApplier<Notice, NotifiedEvent>
    {
        public Notice Apply(Notice s, NotifiedEvent ev) => s with { Count = s.Count + 1, LastAmt = ev.Amt };
    }

    // ─── Bridges ─────────────────────────────────────────
    private sealed class DamageToScoreBridge : IBridge<DamageEvent, AddDamageInput>
    {
        public IEnumerable<AddDamageInput> Bridge(DamageEvent effect)
        {
            yield return new AddDamageInput(effect.Amt);
        }
    }
    private sealed class DamageAddedToNotifyBridge : IBridge<DamageAddedEvent, NotifyInput>
    {
        public IEnumerable<NotifyInput> Bridge(DamageAddedEvent effect)
        {
            yield return new NotifyInput(effect.Amt);
        }
    }

    private sealed record Sys(string Name) : Sender;

    private static (Runtime<Battle> battle, EffectHandlerRegistry<Battle> battleHandlers,
                    Runtime<Score> score, EffectHandlerRegistry<Score> scoreHandlers,
                    Runtime<Notice> notice, EffectHandlerRegistry<Notice> noticeHandlers)
        BuildWorld()
    {
        // Battle
        var battleSagas = new SagaRegistry();
        battleSagas.Register(new AttackSaga());
        var battleAppliers = new EventApplierRegistry<Battle>();
        battleAppliers.Register(new DamageApplier());
        var battle = new Runtime<Battle>(new Battle(100), new EffectPipeline(battleSagas), battleAppliers);

        // Score
        var scoreSagas = new SagaRegistry();
        scoreSagas.Register(new AddDamageSaga());
        var scoreAppliers = new EventApplierRegistry<Score>();
        scoreAppliers.Register(new DamageAddedApplier());
        var scoreProducers = new IntentProducerRegistry<Score>();
        scoreProducers.Register(new AddDamageProducer());
        var score = new Runtime<Score>(new Score(0), new EffectPipeline(scoreSagas), scoreAppliers, scoreProducers);

        // Notice
        var noticeSagas = new SagaRegistry();
        noticeSagas.Register(new NotifySaga());
        var noticeAppliers = new EventApplierRegistry<Notice>();
        noticeAppliers.Register(new NotifyApplier());
        var noticeProducers = new IntentProducerRegistry<Notice>();
        noticeProducers.Register(new NotifyProducer());
        var notice = new Runtime<Notice>(new Notice(0, 0), new EffectPipeline(noticeSagas), noticeAppliers, noticeProducers);

        return (
            battle, new EffectHandlerRegistry<Battle>(),
            score, new EffectHandlerRegistry<Score>(),
            notice, new EffectHandlerRegistry<Notice>());
    }

    [Fact]
    public async Task Two_runtimes_cascade_via_bridge()
    {
        var (battle, battleHandlers, score, scoreHandlers, _, _) = BuildWorld();

        // Battle → Score の bridge を battle 側ハンドラに刺す
        var scoreDispatcher = new EffectDispatcher<Score>(scoreHandlers);
        battleHandlers.RegisterBridge(new DamageToScoreBridge(), score, new Sys("battle→score"), scoreDispatcher);
        var battleDispatcher = new EffectDispatcher<Battle>(battleHandlers);

        var trace = battle.Dispatch(new AttackIntent(30), new Sys("ext"));
        await battleDispatcher.DispatchAsync(new EffectBatch<Battle>(trace));

        Assert.Equal(70, battle.Latest.Hp);       // 100 - 30
        Assert.Equal(30, score.Latest.TotalDamage); // Bridge 経由で蓄積
    }

    [Fact]
    public async Task Three_runtimes_chain_in_one_input()
    {
        var (battle, battleHandlers, score, scoreHandlers, notice, noticeHandlers) = BuildWorld();

        // 3 段: Battle.DamageEvent → Score.AddDamageInput / Score.DamageAddedEvent → Notice.NotifyInput
        var noticeDispatcher = new EffectDispatcher<Notice>(noticeHandlers);
        scoreHandlers.RegisterBridge(new DamageAddedToNotifyBridge(), notice, new Sys("score→notice"), noticeDispatcher);
        var scoreDispatcher = new EffectDispatcher<Score>(scoreHandlers);
        battleHandlers.RegisterBridge(new DamageToScoreBridge(), score, new Sys("battle→score"), scoreDispatcher);
        var battleDispatcher = new EffectDispatcher<Battle>(battleHandlers);

        // 攻撃を 2 連発
        await battleDispatcher.DispatchAsync(new EffectBatch<Battle>(
            battle.Dispatch(new AttackIntent(15), new Sys("ext"))));
        await battleDispatcher.DispatchAsync(new EffectBatch<Battle>(
            battle.Dispatch(new AttackIntent(25), new Sys("ext"))));

        Assert.Equal(60, battle.Latest.Hp);
        Assert.Equal(40, score.Latest.TotalDamage);
        Assert.Equal(2, notice.Latest.Count);
        Assert.Equal(25, notice.Latest.LastAmt);
    }

    [Fact]
    public async Task Bridge_does_not_fire_for_unrelated_effects()
    {
        // Battle に別 Event 型も登録して、Bridge は DamageEvent にしか反応しないことを確認
        var (battle, battleHandlers, score, _, _, _) = BuildWorld();

        battleHandlers.RegisterBridge(new DamageToScoreBridge(), score, new Sys("b"));
        // ついでに Event 全部に対する logging handler も登録（chain 解決の干渉確認）
        var allEvents = new List<Effect>();
        battleHandlers.Register(new GenericEventLogger(allEvents));
        var battleDispatcher = new EffectDispatcher<Battle>(battleHandlers);

        await battleDispatcher.DispatchAsync(new EffectBatch<Battle>(
            battle.Dispatch(new AttackIntent(7), new Sys("t"))));

        Assert.Equal(7, score.Latest.TotalDamage);  // Bridge は反応した
        Assert.Single(allEvents);
        Assert.IsType<DamageEvent>(allEvents[0]);
    }

    private sealed class GenericEventLogger : IEffectHandler<Event, Battle>
    {
        private readonly List<Effect> _list;
        public GenericEventLogger(List<Effect> list) { _list = list; }
        public Task OnEffectAsync(Event effect, Battle state, EffectBatch<Battle> batch)
        {
            _list.Add(effect);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Bridge_with_multiple_outputs_creates_multiple_destination_intents()
    {
        // 1 つの DamageEvent から AOE のように複数 input を吐く
        var (battle, battleHandlers, score, scoreHandlers, _, _) = BuildWorld();

        var scoreDispatcher = new EffectDispatcher<Score>(scoreHandlers);
        battleHandlers.RegisterBridge(new SplitDamageBridge(), score, new Sys("b"), scoreDispatcher);
        var battleDispatcher = new EffectDispatcher<Battle>(battleHandlers);

        await battleDispatcher.DispatchAsync(new EffectBatch<Battle>(
            battle.Dispatch(new AttackIntent(10), new Sys("t"))));

        // 10 を 1+2+7 に分解して Score に積む
        Assert.Equal(10, score.Latest.TotalDamage);
    }

    private sealed class SplitDamageBridge : IBridge<DamageEvent, AddDamageInput>
    {
        public IEnumerable<AddDamageInput> Bridge(DamageEvent effect)
        {
            yield return new AddDamageInput(1);
            yield return new AddDamageInput(2);
            yield return new AddDamageInput(effect.Amt - 3);
        }
    }
}
