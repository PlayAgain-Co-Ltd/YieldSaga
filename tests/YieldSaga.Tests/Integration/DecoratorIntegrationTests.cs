namespace YieldSaga.Tests.Integration;

/// <summary>
/// Runtime + Pipeline + Decorator 統合: Saga 出力に Decorator が被さって State 更新まで通る。
/// </summary>
public class DecoratorIntegrationTests
{
    private sealed record S(List<string> Log);

    private record BattleIntent(string Move) : Intent;
    private sealed record AttackIntent(string Move) : BattleIntent(Move);

    private sealed record LogEvent(string Text) : Event;
    private sealed record System(string Name) : Sender;

    private sealed class AttackSaga : ISaga<AttackIntent>
    {
        public IEnumerable<Effect> Run(AttackIntent intent, Sender sender)
        {
            yield return new LogEvent($"attack:{intent.Move}");
        }
    }

    private sealed class LogApplier : IEventApplier<S, LogEvent>
    {
        public S Apply(S state, LogEvent ev)
        {
            state.Log.Add(ev.Text);
            return state;
        }
    }

    // 全 BattleIntent に "turn-begin"/"turn-end" を被せる
    private sealed class TurnBoundary : ISagaDecorator<BattleIntent>
    {
        public IEnumerable<Effect> Decorate(IEnumerable<Effect> effects)
        {
            yield return new LogEvent("turn-begin");
            foreach (var e in effects) yield return e;
            yield return new LogEvent("turn-end");
        }
    }

    // AttackIntent 専用に "attack-prep"/"attack-cleanup" を被せる
    private sealed class AttackBracket : ISagaDecorator<AttackIntent>
    {
        public IEnumerable<Effect> Decorate(IEnumerable<Effect> effects)
        {
            yield return new LogEvent("attack-prep");
            foreach (var e in effects) yield return e;
            yield return new LogEvent("attack-cleanup");
        }
    }

    [Fact]
    public void Decorator_chain_runs_in_inheritance_order_through_runtime()
    {
        var sagas = new SagaRegistry();
        sagas.Register(new AttackSaga());

        var appliers = new EventApplierRegistry<S>();
        appliers.Register(new LogApplier());

        var decorators = new SagaDecoratorRegistry();
        decorators.Register(new AttackBracket()); // sub (inner)
        decorators.Register(new TurnBoundary());  // base (outer)

        var rt = new Runtime<S>(new S(new List<string>()), sagas, appliers, interceptors: null, decorators: decorators);
        rt.Dispatch(new AttackIntent("slash"), new System("t"));

        Assert.Equal(
            new[] { "turn-begin", "attack-prep", "attack:slash", "attack-cleanup", "turn-end" },
            rt.Latest.Log);
    }

    [Fact]
    public void Without_decorators_state_only_sees_raw_saga_output()
    {
        var sagas = new SagaRegistry();
        sagas.Register(new AttackSaga());
        var appliers = new EventApplierRegistry<S>();
        appliers.Register(new LogApplier());

        var rt = new Runtime<S>(new S(new List<string>()), sagas, appliers);
        rt.Dispatch(new AttackIntent("slash"), new System("t"));

        Assert.Equal(new[] { "attack:slash" }, rt.Latest.Log);
    }
}
