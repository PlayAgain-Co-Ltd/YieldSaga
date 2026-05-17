namespace YieldSaga.Tests.Pipeline;

/// <summary>
/// Spec §9: per-Call <see cref="SignalScope"/> (= <see cref="Call.Scope"/>) は
/// Pipeline で Call を展開するとき dispatch scope と Merge され、
/// その Call の展開と内側の再帰 (サブ Call / Interceptor 発の Signal も含む) で効く。
/// SF2 の <c>WithTargetEnemy(invocation, id)</c> 相当のパターンをここでカバーする。
/// </summary>
public class CallScopeTests
{
    private sealed record Sub1Intent(int Max) : Intent;
    private sealed record Sub2Intent : Intent;
    private sealed record DeepIntent : Intent;
    private sealed record ParentIntent : Intent;
    private sealed record MarkEvent(string Tag) : Event;
    private sealed record ResolvedEvent(int Value) : Event;
    private sealed record TestSender : Sender;

    private sealed record PickPrompt(int Max) : Prompt
    {
        public override bool Accepts(object input) => input is int i && i >= 0 && i < Max;
    }

    // ── Sagas ───────────────────────────────────────────────────────────

    /// <summary>Take を yield し、解決値を ResolvedEvent に乗せて再 yield。</summary>
    private sealed class Sub1Saga : ISaga<Sub1Intent>
    {
        public IEnumerable<Effect> Run(Sub1Intent intent, Sender sender)
        {
            var t = new Take(new PickPrompt(intent.Max));
            yield return t;
            yield return new ResolvedEvent(t.IsResolved ? (int)t.ResolvedInput! : -1);
        }
    }

    /// <summary>Sub1 を Call で呼ぶ中間段。</summary>
    private sealed class Sub2Saga : ISaga<Sub2Intent>
    {
        public IEnumerable<Effect> Run(Sub2Intent intent, Sender sender)
        {
            yield return new MarkEvent("sub2-start");
            yield return new Call(new Sub1Intent(10), sender);
            yield return new MarkEvent("sub2-end");
        }
    }

    /// <summary>Sub2 を Call で呼ぶ最外段 (= Deep → Sub2 → Sub1 で 3 段ネスト)。</summary>
    private sealed class DeepSaga : ISaga<DeepIntent>
    {
        public IEnumerable<Effect> Run(DeepIntent intent, Sender sender)
        {
            yield return new Call(new Sub2Intent(), sender);
        }
    }

    /// <summary>外側 Take を yield してから Sub1 を Call で呼ぶ (per-Call scope 隔離確認用)。</summary>
    private sealed class ParentSaga : ISaga<ParentIntent>
    {
        public IEnumerable<Effect> Run(ParentIntent intent, Sender sender)
        {
            var outer = new Take(new PickPrompt(99));
            yield return outer;
            yield return new ResolvedEvent(outer.IsResolved ? (int)outer.ResolvedInput! : -100);
            yield return new Call(new Sub1Intent(10), sender,
                Scope: SignalScope.Empty.OnTake(_ => 3));   // ← Sub1 の Take のみ resolve したい
        }
    }

    private static EffectPipeline BuildPipeline()
    {
        var sagas = new SagaRegistry();
        sagas.Register(new Sub1Saga());
        sagas.Register(new Sub2Saga());
        sagas.Register(new DeepSaga());
        sagas.Register(new ParentSaga());
        return new EffectPipeline(sagas);
    }

    // ── Tests ───────────────────────────────────────────────────────────

    [Fact]
    public void PerCall_OnTake_resolves_Take_in_called_Saga()
    {
        // SF2 WithTargetEnemy(invocation, id) 相当: Call の Scope で内側 Take を pre-resolve
        var pipeline = BuildPipeline();
        var call = new Call(
            new Sub1Intent(10),
            new TestSender(),
            Scope: SignalScope.Empty.OnTake(_ => 7));

        var result = pipeline.Process(call).ToArray();

        // Sub1Saga: yield Take (= 7 で resolve 済み), yield ResolvedEvent(7)
        Assert.Equal(2, result.Length);
        Assert.IsType<Take>(result[0]);
        Assert.True(((Take)result[0]).IsResolved);
        Assert.Equal(7, ((Take)result[0]).ResolvedInput);
        Assert.Equal(new ResolvedEvent(7), result[1]);
    }

    [Fact]
    public void PerCall_OnCall_overrides_intent_expansion()
    {
        // テスト用モック: 特定 Intent の expansion を差し替え
        var pipeline = BuildPipeline();
        var mockEvent = new MarkEvent("mocked");
        var call = new Call(
            new Sub1Intent(10),
            new TestSender(),
            Scope: SignalScope.Empty.OnCall<Sub1Intent>(_ => new Effect[] { mockEvent }));

        var result = pipeline.Process(call).ToArray();

        // Saga は呼ばれない、override の Effects だけが流れる
        Assert.Single(result);
        Assert.Same(mockEvent, result[0]);
    }

    [Fact]
    public void PerCall_scope_does_not_leak_to_outer_Saga()
    {
        // 親 Saga の Take には影響せず、内側 Call(Sub1Intent) の Take のみ resolve する
        var pipeline = BuildPipeline();
        var call = new Call(new ParentIntent(), new TestSender());

        var result = pipeline.Process(call).ToArray();

        // 期待: 外側 Take は未解決 (= -100 が記録)、内側 Sub1 の Take は 3 で resolve
        var takes = result.OfType<Take>().ToList();
        var events = result.OfType<ResolvedEvent>().ToList();

        Assert.Equal(2, takes.Count);
        Assert.False(takes[0].IsResolved);                 // 親の Take は未解決
        Assert.True(takes[1].IsResolved);                  // 内側 Sub1 の Take は解決
        Assert.Equal(3, takes[1].ResolvedInput);

        Assert.Equal(2, events.Count);
        Assert.Equal(new ResolvedEvent(-100), events[0]);  // 親側 (未解決 = sentinel)
        Assert.Equal(new ResolvedEvent(3), events[1]);     // 内側 (3 で解決)
    }

    [Fact]
    public void Dispatch_scope_and_per_Call_scope_both_apply_with_per_Call_winning()
    {
        // Dispatch: OnTake → 1, per-Call: OnTake → 9 ⇒ per-Call が勝つ
        var pipeline = BuildPipeline();
        var dispatch = SignalScope.Empty.OnTake(_ => 1);
        var call = new Call(
            new Sub1Intent(10),
            new TestSender(),
            Scope: SignalScope.Empty.OnTake(_ => 9));

        var result = pipeline.Process(call, dispatch).ToArray();

        var take = (Take)result[0];
        Assert.Equal(9, take.ResolvedInput);              // per-Call が上書き優先
        Assert.Equal(new ResolvedEvent(9), result[1]);
    }

    [Fact]
    public void Dispatch_scope_handles_when_per_Call_scope_returns_null()
    {
        // per-Call が null フォールバック ⇒ dispatch の handler に降りる
        var pipeline = BuildPipeline();
        var dispatch = SignalScope.Empty.OnTake(_ => 2);
        var call = new Call(
            new Sub1Intent(10),
            new TestSender(),
            Scope: SignalScope.Empty.OnTake(_ => null));   // null → fallback

        var result = pipeline.Process(call, dispatch).ToArray();

        var take = (Take)result[0];
        Assert.Equal(2, take.ResolvedInput);
        Assert.Equal(new ResolvedEvent(2), result[1]);
    }

    [Fact]
    public void PerCall_scope_propagates_through_3_level_nested_Calls()
    {
        // Deep → Sub2 → Sub1 → Take の 3 段ネスト
        // 最外 Call の per-Call scope が最深部の Take に届く
        var pipeline = BuildPipeline();
        var call = new Call(
            new DeepIntent(),
            new TestSender(),
            Scope: SignalScope.Empty.OnTake(_ => 5));

        var result = pipeline.Process(call).ToArray();

        // 期待ストリーム: Mark(sub2-start), Take(resolved=5), ResolvedEvent(5), Mark(sub2-end)
        Assert.Equal(4, result.Length);
        Assert.Equal(new MarkEvent("sub2-start"), result[0]);
        Assert.IsType<Take>(result[1]);
        Assert.Equal(5, ((Take)result[1]).ResolvedInput);
        Assert.Equal(new ResolvedEvent(5), result[2]);
        Assert.Equal(new MarkEvent("sub2-end"), result[3]);
    }

    [Fact]
    public void Process_without_scope_yields_unresolved_Take_through()
    {
        // scope なし & per-Call scope なしの場合、Take は未解決のまま yield
        var pipeline = BuildPipeline();
        var call = new Call(new Sub1Intent(10), new TestSender());

        var result = pipeline.Process(call).ToArray();

        Assert.IsType<Take>(result[0]);
        Assert.False(((Take)result[0]).IsResolved);
        Assert.Equal(new ResolvedEvent(-1), result[1]);   // sentinel
    }
}
