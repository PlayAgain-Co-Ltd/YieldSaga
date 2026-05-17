namespace YieldSaga.Tests.State;

/// <summary>
/// Spec §4: Applier 解決は継承チェーンを辿る。
/// 基底 Event 型に登録した Applier は派生 Event 型にも適用される。
/// </summary>
public class EventApplierRegistryTests
{
    private sealed record S(int Hp);

    private abstract record DamageEvent(int Amount) : Event;
    private sealed record FireDamageEvent(int Amount) : DamageEvent(Amount);
    private sealed record IceDamageEvent(int Amount) : DamageEvent(Amount);
    private sealed record UnregisteredEvent : Event;

    private sealed class DamageApplier : IEventApplier<S, DamageEvent>
    {
        public S Apply(S state, DamageEvent ev) => state with { Hp = state.Hp - ev.Amount };
    }

    private sealed class IceDamageApplier : IEventApplier<S, IceDamageEvent>
    {
        public S Apply(S state, IceDamageEvent ev) => state with { Hp = state.Hp - ev.Amount * 2 };
    }

    [Fact]
    public void Base_type_applier_handles_derived_event()
    {
        var reg = new EventApplierRegistry<S>();
        reg.Register(new DamageApplier());

        var result = reg.Apply(new S(100), new FireDamageEvent(10));

        Assert.Equal(90, result.Hp);
    }

    [Fact]
    public void Exact_type_applier_wins_over_base_type()
    {
        var reg = new EventApplierRegistry<S>();
        reg.Register(new DamageApplier());        // base: -Amount
        reg.Register(new IceDamageApplier());     // derived: -Amount*2

        // FireDamage は base 経由（×1）
        var fire = reg.Apply(new S(100), new FireDamageEvent(10));
        Assert.Equal(90, fire.Hp);

        // IceDamage は exact 経由（×2）
        var ice = reg.Apply(new S(100), new IceDamageEvent(10));
        Assert.Equal(80, ice.Hp);
    }

    [Fact]
    public void Throws_when_no_applier_in_inheritance_chain()
    {
        var reg = new EventApplierRegistry<S>();
        reg.Register(new IceDamageApplier());

        // UnregisteredEvent は IceDamageApplier も DamageApplier も無いので throw
        Assert.Throws<InvalidOperationException>(() =>
            reg.Apply(new S(100), new UnregisteredEvent()));
    }

    [Fact]
    public void Resolved_cache_is_invalidated_when_more_specific_applier_is_registered()
    {
        var reg = new EventApplierRegistry<S>();
        reg.Register(new DamageApplier());

        // 一度 IceDamage を base 経由で解決させてキャッシュさせる
        var viaBase = reg.Apply(new S(100), new IceDamageEvent(10));
        Assert.Equal(90, viaBase.Hp);

        // その後で派生型の Applier を後追い登録すると、キャッシュは無効化されて派生 Applier が引かれる
        reg.Register(new IceDamageApplier());
        var viaDerived = reg.Apply(new S(100), new IceDamageEvent(10));
        Assert.Equal(80, viaDerived.Hp);
    }

    [Fact]
    public void Duplicate_registration_for_same_event_type_throws()
    {
        var reg = new EventApplierRegistry<S>();
        reg.Register(new IceDamageApplier());

        Assert.Throws<InvalidOperationException>(() =>
            reg.Register(new IceDamageApplier()));
    }
}
