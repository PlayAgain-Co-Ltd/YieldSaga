namespace YieldSaga.Tests.Interceptors;

/// <summary>
/// Spec §5: Interceptor 4 モード自動判別を Registry 単体で確認。
/// </summary>
public class InterceptorRegistryTests
{
    private sealed record S(int Hp);
    private sealed record DamageEvent(int Amount) : Event;
    private sealed record HealEvent(int Amount) : Event;
    private sealed record LogEffect(string Text) : Event;

    private sealed class Func<TState, TEvent>(System.Func<TEvent, TState, InterceptResult> fn)
        : IInterceptor<TState, TEvent>
        where TEvent : Event
    {
        public InterceptResult Apply(TEvent ev, TState preState) => fn(ev, preState);
    }

    [Fact]
    public void Skip_passes_event_unchanged()
    {
        var reg = new InterceptorRegistry<S>();
        reg.Register(new Func<S, DamageEvent>((_, _) => InterceptResult.Skip));

        var result = reg.Process(new DamageEvent(5), new S(100)).ToList();

        Assert.Single(result);
        Assert.Equal(new DamageEvent(5), result[0]);
    }

    [Fact]
    public void Drop_removes_event_completely()
    {
        var reg = new InterceptorRegistry<S>();
        reg.Register(new Func<S, DamageEvent>((_, _) => InterceptResult.Drop));

        var result = reg.Process(new DamageEvent(5), new S(100)).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void Decorate_when_emit_contains_original_event()
    {
        // [Log("pre"), DamageEvent(5), Log("post")] → 装飾
        var reg = new InterceptorRegistry<S>();
        reg.Register(new Func<S, DamageEvent>((ev, _) =>
            InterceptResult.Emit(new LogEffect("pre"), ev, new LogEffect("post"))));

        var result = reg.Process(new DamageEvent(5), new S(100)).ToList();

        Assert.Equal(3, result.Count);
        Assert.Equal(new LogEffect("pre"), result[0]);
        Assert.Equal(new DamageEvent(5), result[1]);
        Assert.Equal(new LogEffect("post"), result[2]);
    }

    // Spec §5「起動元 Interceptor "型" は除外される」を踏まえ、二重発火防止が
    // 期待通りに働くケースを書くには distinct な class を使う必要がある
    // （同型インスタンスを 2 つ登録すると後続が全部除外される）。
    private sealed class DecorateLogAZ : IInterceptor<S, DamageEvent>
    {
        public InterceptResult Apply(DamageEvent ev, S preState) =>
            InterceptResult.Emit(new LogEffect("a"), ev, new LogEffect("z"));
    }

    private sealed class DecorateLogB : IInterceptor<S, DamageEvent>
    {
        public InterceptResult Apply(DamageEvent ev, S preState) =>
            InterceptResult.Emit(new LogEffect("b"), ev);
    }

    [Fact]
    public void Decorate_chains_through_multiple_interceptors()
    {
        // i1: [Log("a"), ev, Log("z")]   → ev を recurse、起動元 i1 を excluded で再投入
        // i2 (distinct 型): [Log("b"), ev] → さらに recurse、{i1, i2} excluded
        // 最終的に ev はもう発火する hook が無いので素通し
        // 周辺 LogEffect は異型 Event なので独自の fresh chain (登録 hook 無し → 素通し)
        // final: Log(a) Log(b) ev Log(z)
        var reg = new InterceptorRegistry<S>();
        reg.Register(new DecorateLogAZ());
        reg.Register(new DecorateLogB());

        var result = reg.Process(new DamageEvent(5), new S(100)).ToList();

        Assert.Equal(4, result.Count);
        Assert.Equal(new LogEffect("a"), result[0]);
        Assert.Equal(new LogEffect("b"), result[1]);
        Assert.Equal(new DamageEvent(5), result[2]);
        Assert.Equal(new LogEffect("z"), result[3]);
    }

    [Fact]
    public void Transform_when_emit_is_single_same_type_event_with_new_value()
    {
        // Damage を半減する装備
        var reg = new InterceptorRegistry<S>();
        reg.Register(new Func<S, DamageEvent>((ev, _) =>
            InterceptResult.Emit(new DamageEvent(ev.Amount / 2))));

        var result = reg.Process(new DamageEvent(10), new S(100)).ToList();

        Assert.Single(result);
        Assert.Equal(new DamageEvent(5), result[0]);
    }

    private sealed class HalveDamage : IInterceptor<S, DamageEvent>
    {
        public InterceptResult Apply(DamageEvent ev, S preState) =>
            InterceptResult.Emit(new DamageEvent(ev.Amount / 2));
    }

    private sealed class SubtractOneDamage : IInterceptor<S, DamageEvent>
    {
        public InterceptResult Apply(DamageEvent ev, S preState) =>
            InterceptResult.Emit(new DamageEvent(ev.Amount - 1));
    }

    [Fact]
    public void Transform_chains_through_multiple_interceptors()
    {
        // 10 → /2 (=5) → -1 (=4)
        var reg = new InterceptorRegistry<S>();
        reg.Register(new HalveDamage());
        reg.Register(new SubtractOneDamage());

        var result = reg.Process(new DamageEvent(10), new S(100)).ToList();

        Assert.Single(result);
        Assert.Equal(new DamageEvent(4), result[0]);
    }

    [Fact]
    public void Replace_when_emit_is_different_event_or_multiple_non_original()
    {
        // ダメージ無効 → Heal に置換
        var reg = new InterceptorRegistry<S>();
        reg.Register(new Func<S, DamageEvent>((_, _) =>
            InterceptResult.Emit(new HealEvent(3))));
        // 後続の DamageEvent 用 Interceptor は走らないはず
        reg.Register(new Func<S, DamageEvent>((ev, _) =>
            InterceptResult.Emit(new DamageEvent(ev.Amount * 100))));

        var result = reg.Process(new DamageEvent(7), new S(100)).ToList();

        Assert.Single(result);
        Assert.Equal(new HealEvent(3), result[0]);
    }

    // 二重発火防止テストのために distinct な Interceptor 型を 2 つ用意する
    // （仕様 §5「起動元の Interceptor "型" は除外される」がポイント）。
    private sealed class ReplaceWithDoubleAndHeal : IInterceptor<S, DamageEvent>
    {
        public InterceptResult Apply(DamageEvent ev, S preState) =>
            InterceptResult.Emit(new DamageEvent(ev.Amount * 2), new HealEvent(1));
    }

    private sealed class AddHundredToDamage : IInterceptor<S, DamageEvent>
    {
        public InterceptResult Apply(DamageEvent ev, S preState) =>
            InterceptResult.Emit(new DamageEvent(ev.Amount + 100));
    }

    [Fact]
    public void Replace_excludes_originating_interceptor_for_same_type_reemit()
    {
        // i1 (Replace): Damage(n) → [Damage(n*2), Heal(1)]
        // i2 (Transform): Damage(n) → Damage(n+100)
        // i1 の Replace 後、再処理時に i1 型は除外される（無限ループ回避）
        var reg = new InterceptorRegistry<S>();
        reg.Register(new ReplaceWithDoubleAndHeal());
        reg.Register(new AddHundredToDamage());

        var result = reg.Process(new DamageEvent(5), new S(100)).ToList();

        // i1 が走って Replace: [Damage(10), Heal(1)]
        //   Damage(10) を再処理: i1 除外、i2 だけ走る → Transform: Damage(110)
        //   Heal(1): 登録 Interceptor なし → そのまま
        Assert.Equal(2, result.Count);
        Assert.Equal(new DamageEvent(110), result[0]);
        Assert.Equal(new HealEvent(1), result[1]);
    }

    [Fact]
    public void No_interceptor_registered_for_event_type_passes_through()
    {
        var reg = new InterceptorRegistry<S>();

        var result = reg.Process(new DamageEvent(5), new S(100)).ToList();

        Assert.Single(result);
        Assert.Equal(new DamageEvent(5), result[0]);
    }

    [Fact]
    public void Drop_stops_chain_immediately()
    {
        var afterDropCalled = false;
        var reg = new InterceptorRegistry<S>();
        reg.Register(new Func<S, DamageEvent>((_, _) => InterceptResult.Drop));
        reg.Register(new Func<S, DamageEvent>((_, _) =>
        {
            afterDropCalled = true;
            return InterceptResult.Skip;
        }));

        reg.Process(new DamageEvent(5), new S(100)).ToList();

        Assert.False(afterDropCalled);
    }

    [Fact]
    public void Interceptor_can_inspect_preState()
    {
        S? captured = null;
        var reg = new InterceptorRegistry<S>();
        reg.Register(new Func<S, DamageEvent>((_, state) =>
        {
            captured = state;
            return InterceptResult.Skip;
        }));

        reg.Process(new DamageEvent(5), new S(77)).ToList();

        Assert.Equal(new S(77), captured);
    }

    // ───────────── 継承型解決 (Spec §4/§5: 基底 Event 型に登録された Interceptor は派生 Event 型でも発火) ─────────────

    private abstract record AnyDamageEvent(int Amount) : Event;
    private sealed record FireDamageEvent(int Amount) : AnyDamageEvent(Amount);
    private sealed record IceDamageEvent(int Amount) : AnyDamageEvent(Amount);

    private sealed class AnyDamageHalver : IInterceptor<S, AnyDamageEvent>
    {
        public InterceptResult Apply(AnyDamageEvent ev, S preState) =>
            ev switch
            {
                FireDamageEvent f => InterceptResult.Emit(new FireDamageEvent(f.Amount / 2)),
                IceDamageEvent i => InterceptResult.Emit(new IceDamageEvent(i.Amount / 2)),
                _ => InterceptResult.Skip,
            };
    }

    [Fact]
    public void Base_type_interceptor_fires_for_derived_event()
    {
        var reg = new InterceptorRegistry<S>();
        reg.Register(new AnyDamageHalver());

        var fire = reg.Process(new FireDamageEvent(10), new S(100)).ToList();
        Assert.Single(fire);
        Assert.Equal(new FireDamageEvent(5), fire[0]);

        var ice = reg.Process(new IceDamageEvent(20), new S(100)).ToList();
        Assert.Single(ice);
        Assert.Equal(new IceDamageEvent(10), ice[0]);
    }

    private sealed class FireSpecificTransform : IInterceptor<S, FireDamageEvent>
    {
        public InterceptResult Apply(FireDamageEvent ev, S preState) =>
            InterceptResult.Emit(new FireDamageEvent(ev.Amount + 1));
    }

    [Fact]
    public void Derived_and_base_interceptors_chain_in_leaf_first_order()
    {
        // 継承チェーン解決は leaf-first → root-last で合流する。
        // 派生 (FireSpecificTransform) が先、基底 (AnyDamageHalver) が後。
        var reg = new InterceptorRegistry<S>();
        reg.Register(new AnyDamageHalver());
        reg.Register(new FireSpecificTransform());

        var fire = reg.Process(new FireDamageEvent(10), new S(100)).ToList();
        // FireSpecificTransform (10→11) → AnyDamageHalver (11→5)
        Assert.Single(fire);
        Assert.Equal(new FireDamageEvent(5), fire[0]);

        // IceDamage は FireSpecificTransform にマッチしないので AnyDamageHalver のみ
        var ice = reg.Process(new IceDamageEvent(20), new S(100)).ToList();
        Assert.Single(ice);
        Assert.Equal(new IceDamageEvent(10), ice[0]);
    }

    // ───────────── 遅延列挙 (Emit 中身が yield-iterator の場合も 1 回限り走査される) ─────────────

    [Fact]
    public void Emit_iterates_input_enumerable_lazily()
    {
        // generator が走った回数を数える。Process を ToList する前は 0 回、Emit 列を消費して初めて enumerate される。
        var enumerationCount = 0;
        IEnumerable<Effect> Generator(DamageEvent ev)
        {
            yield return new LogEffect("pre");
            enumerationCount++;
            yield return ev;
            yield return new LogEffect("post");
            enumerationCount++;
        }

        var reg = new InterceptorRegistry<S>();
        reg.Register(new Func<S, DamageEvent>((ev, _) => InterceptResult.Emit(Generator(ev))));

        var stream = reg.Process(new DamageEvent(5), new S(100));
        Assert.Equal(0, enumerationCount);  // まだ走らない

        var list = stream.ToList();
        Assert.Equal(2, enumerationCount);  // 全消費で 2 ステップ進んだ
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void Process_does_not_materialize_unless_enumerated()
    {
        // Drop モードの Interceptor が「副作用」(invoke カウンタ) を持つことで、enumerate 前に呼ばれていないことを確認。
        var invokeCount = 0;
        var reg = new InterceptorRegistry<S>();
        reg.Register(new Func<S, DamageEvent>((_, _) =>
        {
            invokeCount++;
            return InterceptResult.Drop;
        }));

        var stream = reg.Process(new DamageEvent(5), new S(100));
        Assert.Equal(0, invokeCount);

        stream.ToList();
        Assert.Equal(1, invokeCount);
    }

    // ───────────── 装飾 emit 中の異型 Event が独自の Interceptor チェーンを通る ─────────────

    private sealed class EmitLogPlusEvent : IInterceptor<S, DamageEvent>
    {
        public InterceptResult Apply(DamageEvent ev, S preState) =>
            // 装飾: [HealEvent(99), ev, HealEvent(1)] — HealEvent は異型 Event。
            // HealEvent 用に登録された Interceptor が独自に発火するべき。
            InterceptResult.Emit(new HealEvent(99), ev, new HealEvent(1));
    }

    private sealed class HealHalver : IInterceptor<S, HealEvent>
    {
        public InterceptResult Apply(HealEvent ev, S preState) =>
            InterceptResult.Emit(new HealEvent(ev.Amount / 2));
    }

    [Fact]
    public void Decorate_surrounding_events_go_through_their_own_interceptor_chain()
    {
        // Damage 用 hook が周辺に HealEvent を Emit する。HealHalver が HealEvent に登録されているので、
        // 周辺の HealEvent 達もそれぞれ独自 chain に通って半減される。
        var reg = new InterceptorRegistry<S>();
        reg.Register(new EmitLogPlusEvent());
        reg.Register(new HealHalver());

        var result = reg.Process(new DamageEvent(5), new S(100)).ToList();

        Assert.Equal(3, result.Count);
        Assert.Equal(new HealEvent(49), result[0]); // 99/2
        Assert.Equal(new DamageEvent(5), result[1]);
        Assert.Equal(new HealEvent(0), result[2]);  // 1/2
    }
}
