namespace YieldSaga.Tests.Handlers;

/// <summary>
/// Spec §8: Current/Remaining/Mark/TakeWhileAndMark の挙動。
/// </summary>
public class EffectBatchTests
{
    private sealed record S(int V);
    private sealed record AEvent(int N) : Event;
    private sealed record BEvent(int N) : Event;

    private static IReadOnlyList<(Effect, S)> Trace(params (Effect, S)[] items) => items;

    [Fact]
    public void Empty_batch_has_no_current()
    {
        var b = new EffectBatch<S>(Array.Empty<(Effect, S)>());
        Assert.False(b.HasCurrent);
        Assert.Equal(0, b.Count);
    }

    [Fact]
    public void Current_and_state_reflect_position()
    {
        var t = Trace(
            (new AEvent(1), new S(1)),
            (new BEvent(2), new S(3)));
        var b = new EffectBatch<S>(t);

        Assert.True(b.HasCurrent);
        Assert.Equal(new AEvent(1), b.Current);
        Assert.Equal(1, b.CurrentState.V);
    }

    [Fact]
    public void Remaining_is_strictly_after_current()
    {
        var t = Trace(
            (new AEvent(1), new S(1)),
            (new BEvent(2), new S(3)),
            (new AEvent(3), new S(6)));
        var b = new EffectBatch<S>(t);

        var rem = b.Remaining.Select(p => p.Effect).ToList();
        Assert.Equal(2, rem.Count);
        Assert.Equal(new BEvent(2), rem[0]);
        Assert.Equal(new AEvent(3), rem[1]);
    }

    [Fact]
    public void Mark_and_IsProcessed_are_per_handler_per_effect()
    {
        var b = new EffectBatch<S>(Trace((new AEvent(1), new S(1))));
        var h1 = new object();
        var h2 = new object();
        var ev = new AEvent(1);

        Assert.False(b.IsProcessed(h1, ev));
        b.MarkAsProcessed(h1, ev);
        Assert.True(b.IsProcessed(h1, ev));
        Assert.False(b.IsProcessed(h2, ev));
    }

    [Fact]
    public void TakeWhileAndMark_grabs_consecutive_typed_effects_including_current()
    {
        var t = Trace(
            (new AEvent(1), new S(1)),
            (new AEvent(2), new S(3)),
            (new AEvent(3), new S(6)),
            (new BEvent(99), new S(7)),
            (new AEvent(4), new S(11)));
        var b = new EffectBatch<S>(t);
        var h = new object();

        var run = b.TakeWhileAndMark<AEvent>(h, _ => true);

        // BEvent で打ち切られるので 3 つ。Current を含むので 1,2,3
        Assert.Equal(3, run.Count);
        Assert.Equal(new[] { 1, 2, 3 }, run.Select(r => r.Effect.N).ToArray());
        // 3 つとも mark されている
        Assert.True(b.IsProcessed(h, new AEvent(1)));
        Assert.True(b.IsProcessed(h, new AEvent(2)));
        Assert.True(b.IsProcessed(h, new AEvent(3)));
        // BEvent と先の AEvent(4) は mark されない
        Assert.False(b.IsProcessed(h, new BEvent(99)));
        Assert.False(b.IsProcessed(h, new AEvent(4)));
    }

    [Fact]
    public void TakeWhileAndMark_respects_predicate()
    {
        var t = Trace(
            (new AEvent(2), new S(0)),
            (new AEvent(4), new S(0)),
            (new AEvent(3), new S(0)),
            (new AEvent(6), new S(0)));
        var b = new EffectBatch<S>(t);
        var h = new object();

        // 偶数のあいだだけ取る → 3 で打ち切る
        var run = b.TakeWhileAndMark<AEvent>(h, a => a.N % 2 == 0);

        Assert.Equal(new[] { 2, 4 }, run.Select(r => r.Effect.N).ToArray());
        Assert.False(b.IsProcessed(h, new AEvent(3)));
    }

    [Fact]
    public void TakeWhileAndMark_returns_empty_when_current_does_not_match()
    {
        var t = Trace((new BEvent(1), new S(0)), (new AEvent(2), new S(0)));
        var b = new EffectBatch<S>(t);
        var h = new object();

        var run = b.TakeWhileAndMark<AEvent>(h, _ => true);
        Assert.Empty(run);
        // 後ろに AEvent があっても Current 不一致で即終了
        Assert.False(b.IsProcessed(h, new AEvent(2)));
    }
}
