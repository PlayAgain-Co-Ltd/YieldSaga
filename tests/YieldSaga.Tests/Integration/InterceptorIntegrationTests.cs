namespace YieldSaga.Tests.Integration;

/// <summary>
/// Runtime + Pipeline + Interceptor フルパス。
/// Saga が吐いた Event が Interceptor で書き換わって State 適用される。
/// </summary>
public class InterceptorIntegrationTests
{
    private sealed record S(int Hp, int Shield);
    private sealed record HitIntent(int Amount) : Intent;
    private sealed record DamageEvent(int Amount) : Event;
    private sealed record ShieldBlockedEvent(int Amount) : Event;
    private sealed record System(string Name) : Sender;

    private sealed class HitSaga : ISaga<HitIntent>
    {
        public IEnumerable<Effect> Run(HitIntent intent, Sender sender)
        {
            yield return new DamageEvent(intent.Amount);
        }
    }

    private sealed class DamageApplier : IEventApplier<S, DamageEvent>
    {
        public S Apply(S state, DamageEvent ev) => state with { Hp = state.Hp - ev.Amount };
    }

    private sealed class ShieldBlockApplier : IEventApplier<S, ShieldBlockedEvent>
    {
        public S Apply(S state, ShieldBlockedEvent ev) =>
            state with { Shield = state.Shield - ev.Amount };
    }

    /// <summary>
    /// Shield があるときは Damage を「Shield 分減少」+「残り Damage」に Decorate する。
    /// </summary>
    private sealed class ShieldInterceptor : IInterceptor<S, DamageEvent>
    {
        public InterceptResult Apply(DamageEvent ev, S preState)
        {
            if (preState.Shield <= 0) return InterceptResult.Skip;
            var blocked = Math.Min(preState.Shield, ev.Amount);
            var remaining = ev.Amount - blocked;
            // Transform: Damage(remaining) に置き換え、Shield 減算 Event を Replace で発火
            // ここでは「Damage を別物に置換しつつ Shield Event を発生させる」= Replace を使う
            return InterceptResult.Emit(
                new ShieldBlockedEvent(blocked),
                new DamageEvent(remaining));
        }
    }

    [Fact]
    public void Damage_without_shield_passes_through()
    {
        var rt = Build(initialHp: 100, initialShield: 0);
        rt.Dispatch(new HitIntent(20), new System("t"));
        Assert.Equal(80, rt.Latest.Hp);
        Assert.Equal(0, rt.Latest.Shield);
    }

    [Fact]
    public void Shield_intercepts_damage_into_two_events()
    {
        var rt = Build(initialHp: 100, initialShield: 5);
        rt.Dispatch(new HitIntent(20), new System("t"));
        // Shield(5) で 5 ブロック、残り 15 が Damage に
        Assert.Equal(85, rt.Latest.Hp);
        Assert.Equal(0, rt.Latest.Shield);
    }

    [Fact]
    public void Interceptor_sees_preState_not_post_state()
    {
        // 連続 Dispatch で preState が正しく取れているか
        var rt = Build(initialHp: 100, initialShield: 10);
        rt.Dispatch(new HitIntent(3), new System("t"));   // Shield 10→7, Hp 100
        rt.Dispatch(new HitIntent(20), new System("t"));  // Shield 7→0, Hp 100-13=87
        Assert.Equal(87, rt.Latest.Hp);
        Assert.Equal(0, rt.Latest.Shield);
    }

    private static Runtime<S> Build(int initialHp, int initialShield)
    {
        var sagas = new SagaRegistry();
        sagas.Register(new HitSaga());

        var appliers = new EventApplierRegistry<S>();
        appliers.Register(new DamageApplier());
        appliers.Register(new ShieldBlockApplier());

        var interceptors = new InterceptorRegistry<S>();
        interceptors.Register(new ShieldInterceptor());

        return new Runtime<S>(new S(initialHp, initialShield), sagas, appliers, interceptors);
    }
}
