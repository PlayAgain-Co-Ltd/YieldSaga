namespace YieldSaga.Tests.Sagas;

/// <summary>
/// Spec §3: Decorator が Saga 出力を継承チェーンに沿って積層する挙動を Registry 単体で確認。
/// </summary>
public class SagaDecoratorRegistryTests
{
    private record BaseIntent : Intent;
    private sealed record SubIntent : BaseIntent;
    private sealed record UnrelatedIntent : Intent;

    private sealed record MarkerEvent(string Tag) : Event;

    private sealed class WrapWith : ISagaDecorator<Intent>
    {
        private readonly string _prefix;
        private readonly string _suffix;
        public WrapWith(string prefix, string suffix) { _prefix = prefix; _suffix = suffix; }
        public IEnumerable<Effect> Decorate(IEnumerable<Effect> effects)
        {
            yield return new MarkerEvent(_prefix);
            foreach (var e in effects) yield return e;
            yield return new MarkerEvent(_suffix);
        }
    }

    private sealed class WrapBase : ISagaDecorator<BaseIntent>
    {
        public IEnumerable<Effect> Decorate(IEnumerable<Effect> effects)
        {
            yield return new MarkerEvent("base-pre");
            foreach (var e in effects) yield return e;
            yield return new MarkerEvent("base-post");
        }
    }

    private sealed class WrapSub : ISagaDecorator<SubIntent>
    {
        public IEnumerable<Effect> Decorate(IEnumerable<Effect> effects)
        {
            yield return new MarkerEvent("sub-pre");
            foreach (var e in effects) yield return e;
            yield return new MarkerEvent("sub-post");
        }
    }

    private static IEnumerable<Effect> RawSagaOutput()
    {
        yield return new MarkerEvent("body");
    }

    [Fact]
    public void Empty_registry_passes_stream_through_unchanged()
    {
        var reg = new SagaDecoratorRegistry();
        var result = reg.Apply(typeof(SubIntent), RawSagaOutput()).ToList();
        Assert.Single(result);
        Assert.Equal(new MarkerEvent("body"), result[0]);
    }

    [Fact]
    public void Single_decorator_wraps_the_stream()
    {
        var reg = new SagaDecoratorRegistry();
        reg.Register(new WrapSub());

        var result = reg.Apply(typeof(SubIntent), RawSagaOutput()).ToList();

        Assert.Equal(3, result.Count);
        Assert.Equal(new MarkerEvent("sub-pre"), result[0]);
        Assert.Equal(new MarkerEvent("body"), result[1]);
        Assert.Equal(new MarkerEvent("sub-post"), result[2]);
    }

    [Fact]
    public void Base_decorator_runs_as_outermost_wrapper()
    {
        // Sub の decorator は内側、Base の decorator が外側。
        var reg = new SagaDecoratorRegistry();
        reg.Register(new WrapSub());
        reg.Register(new WrapBase());

        var result = reg.Apply(typeof(SubIntent), RawSagaOutput()).ToList();

        // 期待: base-pre / sub-pre / body / sub-post / base-post
        Assert.Equal(5, result.Count);
        Assert.Equal(new MarkerEvent("base-pre"), result[0]);
        Assert.Equal(new MarkerEvent("sub-pre"), result[1]);
        Assert.Equal(new MarkerEvent("body"), result[2]);
        Assert.Equal(new MarkerEvent("sub-post"), result[3]);
        Assert.Equal(new MarkerEvent("base-post"), result[4]);
    }

    [Fact]
    public void Base_decorator_does_not_apply_to_unrelated_intent_type()
    {
        var reg = new SagaDecoratorRegistry();
        reg.Register(new WrapBase());

        var result = reg.Apply(typeof(UnrelatedIntent), RawSagaOutput()).ToList();

        Assert.Single(result);
        Assert.Equal(new MarkerEvent("body"), result[0]);
    }

    [Fact]
    public void Multiple_decorators_on_same_type_register_order_inner_to_outer()
    {
        // 同じ Intent 型に 2 つ登録: 後に登録した方が外側に来る
        var reg = new SagaDecoratorRegistry();
        reg.Register(new WrapWith("inner-pre", "inner-post"));
        reg.Register(new WrapWith("outer-pre", "outer-post"));

        var result = reg.Apply(typeof(SubIntent), RawSagaOutput()).ToList();

        Assert.Equal(5, result.Count);
        Assert.Equal(new MarkerEvent("outer-pre"), result[0]);
        Assert.Equal(new MarkerEvent("inner-pre"), result[1]);
        Assert.Equal(new MarkerEvent("body"), result[2]);
        Assert.Equal(new MarkerEvent("inner-post"), result[3]);
        Assert.Equal(new MarkerEvent("outer-post"), result[4]);
    }

    [Fact]
    public void Decorator_on_Intent_root_applies_to_all_intents()
    {
        var reg = new SagaDecoratorRegistry();
        reg.Register(new WrapWith("any-pre", "any-post"));

        var result = reg.Apply(typeof(UnrelatedIntent), RawSagaOutput()).ToList();

        Assert.Equal(3, result.Count);
        Assert.Equal(new MarkerEvent("any-pre"), result[0]);
        Assert.Equal(new MarkerEvent("body"), result[1]);
        Assert.Equal(new MarkerEvent("any-post"), result[2]);
    }
}
