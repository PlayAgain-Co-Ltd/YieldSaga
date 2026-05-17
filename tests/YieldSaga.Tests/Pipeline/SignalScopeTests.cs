namespace YieldSaga.Tests.Pipeline;

/// <summary>
/// Spec §9: SignalScope は Take / Query&lt;TState&gt; / Call に対する handler を保持する
/// immutable な束。OnXxx と Merge は新インスタンスを返す。
/// </summary>
public class SignalScopeTests
{
    private sealed record S(int N);
    private sealed record T(string Name);
    private sealed record FooIntent(int X) : Intent;
    private sealed record BarIntent : Intent;
    private sealed record FooEvent(int X) : Event;
    private sealed record PickPrompt(int Max) : Prompt
    {
        public override bool Accepts(object input) => input is int i && i >= 0 && i < Max;
    }
    private sealed record TestSender : Sender;

    // ─── OnTake ─────────────────────────────────────────────────────────

    [Fact]
    public void OnTake_resolves_unresolved_Take_via_resolver()
    {
        var scope = SignalScope.Empty.OnTake(_ => 3);
        var take = new Take(new PickPrompt(5));

        scope.TryResolveTake(take);

        Assert.True(take.IsResolved);
        Assert.Equal(3, take.ResolvedInput);
    }

    [Fact]
    public void OnTake_skips_already_resolved_Take()
    {
        var scope = SignalScope.Empty.OnTake(_ => 1);
        var take = new Take(new PickPrompt(5));
        take.TryResolve(2);   // 先に外部で解決

        scope.TryResolveTake(take);

        Assert.Equal(2, take.ResolvedInput);   // 1 で上書きされない
    }

    [Fact]
    public void OnTake_resolver_returning_null_falls_back_to_previous()
    {
        // 後勝ち + null フォールバック: 新 resolver が null なら旧へ
        var scope = SignalScope.Empty
            .OnTake(_ => 7)
            .OnTake(_ => null);
        var take = new Take(new PickPrompt(10));

        scope.TryResolveTake(take);

        Assert.Equal(7, take.ResolvedInput);
    }

    [Fact]
    public void OnTake_resolver_returning_non_null_overrides_previous()
    {
        var scope = SignalScope.Empty
            .OnTake(_ => 1)
            .OnTake(_ => 9);   // 新が non-null → 旧は無視
        var take = new Take(new PickPrompt(10));

        scope.TryResolveTake(take);

        Assert.Equal(9, take.ResolvedInput);
    }

    [Fact]
    public void OnTake_resolver_returning_invalid_input_does_not_resolve()
    {
        // Prompt.Accepts が false なら TryResolve は失敗、IsResolved=false のまま
        var scope = SignalScope.Empty.OnTake(_ => 100);   // Max=5 を超える
        var take = new Take(new PickPrompt(5));

        scope.TryResolveTake(take);

        Assert.False(take.IsResolved);
    }

    // ─── OnQuery ────────────────────────────────────────────────────────

    [Fact]
    public void OnQuery_fills_Value_and_returns_true()
    {
        var scope = SignalScope.Empty.OnQuery<S>(_ => new S(42));
        var q = new Query<S>();

        var resolved = scope.TryResolveQuery(q);

        Assert.True(resolved);
        Assert.Equal(new S(42), q.Value);
    }

    [Fact]
    public void OnQuery_for_different_TState_does_not_match()
    {
        var scope = SignalScope.Empty.OnQuery<S>(_ => new S(1));
        var qWrong = new Query<T>();

        var resolved = scope.TryResolveQuery(qWrong);

        Assert.False(resolved);
        Assert.Null(qWrong.Value);
    }

    [Fact]
    public void OnQuery_same_TState_last_registration_wins()
    {
        var scope = SignalScope.Empty
            .OnQuery<S>(_ => new S(1))
            .OnQuery<S>(_ => new S(2));
        var q = new Query<S>();

        scope.TryResolveQuery(q);

        Assert.Equal(new S(2), q.Value);
    }

    [Fact]
    public void OnQuery_provider_is_called_each_time_with_live_value()
    {
        // provider はクロージャで外部状態を覗ける = live
        var live = new S(0);
        var scope = SignalScope.Empty.OnQuery<S>(_ => live);
        var q1 = new Query<S>();
        var q2 = new Query<S>();

        scope.TryResolveQuery(q1);
        live = new S(99);   // 間で更新
        scope.TryResolveQuery(q2);

        Assert.Equal(new S(0), q1.Value);
        Assert.Equal(new S(99), q2.Value);
    }

    // ─── OnCall ─────────────────────────────────────────────────────────

    [Fact]
    public void OnCall_returns_override_effects_for_matching_intent()
    {
        var mockEffects = new Effect[] { new FooEvent(7) };
        var scope = SignalScope.Empty.OnCall<FooIntent>(_ => mockEffects);

        var result = scope.TryOverrideCall(new FooIntent(1));

        Assert.Same(mockEffects, result);
    }

    [Fact]
    public void OnCall_returns_null_for_unmatched_intent()
    {
        var scope = SignalScope.Empty.OnCall<FooIntent>(_ => Array.Empty<Effect>());

        var result = scope.TryOverrideCall(new BarIntent());

        Assert.Null(result);
    }

    [Fact]
    public void OnCall_same_intent_last_registration_wins()
    {
        var scope = SignalScope.Empty
            .OnCall<FooIntent>(_ => new Effect[] { new FooEvent(1) })
            .OnCall<FooIntent>(_ => new Effect[] { new FooEvent(2) });

        var result = scope.TryOverrideCall(new FooIntent(0))!.ToList();

        Assert.Single(result);
        Assert.Equal(new FooEvent(2), result[0]);
    }

    [Fact]
    public void OnCall_impl_receives_strongly_typed_intent()
    {
        FooIntent? captured = null;
        var scope = SignalScope.Empty.OnCall<FooIntent>(i =>
        {
            captured = i;
            return Array.Empty<Effect>();
        });

        _ = scope.TryOverrideCall(new FooIntent(42))!.ToList();

        Assert.NotNull(captured);
        Assert.Equal(42, captured!.X);
    }

    // ─── Merge ──────────────────────────────────────────────────────────

    [Fact]
    public void Merge_with_null_returns_unchanged_self()
    {
        var scope = SignalScope.Empty.OnTake(_ => 1);

        var merged = scope.Merge(null);

        var take = new Take(new PickPrompt(5));
        merged.TryResolveTake(take);
        Assert.Equal(1, take.ResolvedInput);
    }

    [Fact]
    public void Merge_other_overrides_self_for_same_TState_Query()
    {
        var dispatchScope = SignalScope.Empty.OnQuery<S>(_ => new S(1));
        var perCallScope = SignalScope.Empty.OnQuery<S>(_ => new S(2));

        var merged = dispatchScope.Merge(perCallScope);
        var q = new Query<S>();
        merged.TryResolveQuery(q);

        Assert.Equal(new S(2), q.Value);   // per-Call が優先
    }

    [Fact]
    public void Merge_combines_disjoint_TState_handlers()
    {
        var scopeS = SignalScope.Empty.OnQuery<S>(_ => new S(1));
        var scopeT = SignalScope.Empty.OnQuery<T>(_ => new T("hi"));

        var merged = scopeS.Merge(scopeT);
        var qS = new Query<S>();
        var qT = new Query<T>();
        merged.TryResolveQuery(qS);
        merged.TryResolveQuery(qT);

        Assert.Equal(new S(1), qS.Value);
        Assert.Equal(new T("hi"), qT.Value);
    }

    [Fact]
    public void Merge_take_resolvers_try_other_first_fallback_to_self()
    {
        var self = SignalScope.Empty.OnTake(_ => 1);
        var other = SignalScope.Empty.OnTake(_ => null);   // other は null

        var merged = self.Merge(other);
        var take = new Take(new PickPrompt(5));
        merged.TryResolveTake(take);

        Assert.Equal(1, take.ResolvedInput);   // other が null なので self へ
    }

    [Fact]
    public void Merge_take_other_non_null_wins_over_self()
    {
        var self = SignalScope.Empty.OnTake(_ => 1);
        var other = SignalScope.Empty.OnTake(_ => 3);

        var merged = self.Merge(other);
        var take = new Take(new PickPrompt(5));
        merged.TryResolveTake(take);

        Assert.Equal(3, take.ResolvedInput);
    }

    [Fact]
    public void Merge_with_self_only_take_passes_through()
    {
        var self = SignalScope.Empty.OnTake(_ => 4);
        var other = SignalScope.Empty;

        var merged = self.Merge(other);
        var take = new Take(new PickPrompt(5));
        merged.TryResolveTake(take);

        Assert.Equal(4, take.ResolvedInput);
    }

    [Fact]
    public void Merge_with_other_only_take_passes_through()
    {
        var self = SignalScope.Empty;
        var other = SignalScope.Empty.OnTake(_ => 4);

        var merged = self.Merge(other);
        var take = new Take(new PickPrompt(5));
        merged.TryResolveTake(take);

        Assert.Equal(4, take.ResolvedInput);
    }

    // ─── Immutability ──────────────────────────────────────────────────

    [Fact]
    public void OnTake_returns_new_instance_leaving_original_empty()
    {
        var original = SignalScope.Empty;
        _ = original.OnTake(_ => 1);

        var take = new Take(new PickPrompt(5));
        original.TryResolveTake(take);

        Assert.False(take.IsResolved);   // 元 scope には影響なし
    }

    [Fact]
    public void OnQuery_returns_new_instance_leaving_original_empty()
    {
        var original = SignalScope.Empty;
        _ = original.OnQuery<S>(_ => new S(7));

        var q = new Query<S>();
        var resolved = original.TryResolveQuery(q);

        Assert.False(resolved);
        Assert.Null(q.Value);
    }

    [Fact]
    public void Empty_scope_resolves_nothing()
    {
        var scope = SignalScope.Empty;
        var take = new Take(new PickPrompt(5));
        var q = new Query<S>();

        scope.TryResolveTake(take);
        var qResolved = scope.TryResolveQuery(q);
        var callOverride = scope.TryOverrideCall(new FooIntent(1));

        Assert.False(take.IsResolved);
        Assert.False(qResolved);
        Assert.Null(callOverride);
    }
}
