namespace YieldSaga.Tests.Handlers;

/// <summary>
/// Spec §8: Effect 型階層に沿って **全ての** 該当 Handler が解決される（EventApplier と違い 1 つではない）。
/// </summary>
public class EffectHandlerRegistryTests
{
    private sealed record S(int X);

    private abstract record DamageEvent(int Amt) : Event;
    private sealed record FireDamageEvent(int Amt) : DamageEvent(Amt);
    private sealed record IceDamageEvent(int Amt) : DamageEvent(Amt);

    private sealed class TaggingHandler<T> : IEffectHandler<T, S> where T : Effect
    {
        public string Tag { get; }
        public List<Effect> Seen { get; } = new();
        public TaggingHandler(string tag) { Tag = tag; }
        public Task OnEffectAsync(T effect, S state, EffectBatch<S> batch)
        {
            Seen.Add(effect);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Resolve_includes_handlers_along_inheritance_chain()
    {
        var reg = new EffectHandlerRegistry<S>();
        var damageH = new TaggingHandler<DamageEvent>("damage");
        var fireH = new TaggingHandler<FireDamageEvent>("fire");
        var eventH = new TaggingHandler<Event>("event");
        reg.Register(damageH);
        reg.Register(fireH);
        reg.Register(eventH);

        var entries = reg.Resolve(typeof(FireDamageEvent));

        // FireDamage → DamageEvent → Event の親 3 つの handler 全部が引かれる
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.Handler == fireH);
        Assert.Contains(entries, e => e.Handler == damageH);
        Assert.Contains(entries, e => e.Handler == eventH);
    }

    [Fact]
    public void Resolve_does_not_include_sibling_or_unrelated_handlers()
    {
        var reg = new EffectHandlerRegistry<S>();
        var fireH = new TaggingHandler<FireDamageEvent>("fire");
        var iceH = new TaggingHandler<IceDamageEvent>("ice");
        reg.Register(fireH);
        reg.Register(iceH);

        var entries = reg.Resolve(typeof(FireDamageEvent));

        Assert.Single(entries);
        Assert.Equal(fireH, entries[0].Handler);
    }

    [Fact]
    public void Multiple_handlers_for_same_type_all_resolve()
    {
        var reg = new EffectHandlerRegistry<S>();
        var h1 = new TaggingHandler<FireDamageEvent>("h1");
        var h2 = new TaggingHandler<FireDamageEvent>("h2");
        reg.Register(h1);
        reg.Register(h2);

        var entries = reg.Resolve(typeof(FireDamageEvent));
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void Cache_is_invalidated_when_new_handler_registered()
    {
        var reg = new EffectHandlerRegistry<S>();
        reg.Register(new TaggingHandler<FireDamageEvent>("fire"));

        // 1 回目: fire のみ
        Assert.Single(reg.Resolve(typeof(FireDamageEvent)));

        // 親に後追い登録 → キャッシュが効くと取りこぼす
        reg.Register(new TaggingHandler<Event>("event"));

        // キャッシュ無効化されているので 2 つに増えること
        Assert.Equal(2, reg.Resolve(typeof(FireDamageEvent)).Count);
    }
}
