namespace YieldSaga.Tests.Ordering;

/// <summary>
/// Spec §5/§3: [RunBefore]/[RunAfter] による SagaDecorator の順序制御を検証。
///
/// 念のための意味整理:
///   - チェーン上での "後" = 適用関数 <c>d.Decorate(stream)</c> が後に呼ばれる
///   - 後に呼ばれた decorator は既に wrap 済みの stream を更に外側で wrap する
///   - つまり「チェーン上で後ろの decorator = 外側の wrapper」
/// </summary>
public class TopologicalSortDecoratorTests
{
    private sealed record I : Intent;
    private sealed record Mark(string Tag) : Event;

    private abstract class TaggingDecorator : ISagaDecorator<I>
    {
        protected abstract string Tag { get; }
        public IEnumerable<Effect> Decorate(IEnumerable<Effect> effects)
        {
            yield return new Mark(Tag + "-pre");
            foreach (var e in effects) yield return e;
            yield return new Mark(Tag + "-post");
        }
    }

    private sealed class A : TaggingDecorator { protected override string Tag => "A"; }

    // B はチェーン上で A の後ろ = 外側 wrap
    [RunAfter(typeof(A))]
    private sealed class B : TaggingDecorator { protected override string Tag => "B"; }

    private static IEnumerable<Effect> Body() { yield return new Mark("body"); }

    [Fact]
    public void RunAfter_makes_decorator_run_later_in_chain_outermost_wrapper()
    {
        // 登録順は B, A。RunAfter(A) で並びは [A, B] になり、適用は A.Decorate → B.Decorate。
        // B が後にかぶせるので B が外側、A が内側。
        // 結果: B-pre / A-pre / body / A-post / B-post
        var reg = new SagaDecoratorRegistry();
        reg.Register(new B());
        reg.Register(new A());

        var result = reg.Apply(typeof(I), Body()).OfType<Mark>().Select(m => m.Tag).ToList();

        Assert.Equal(
            new[] { "B-pre", "A-pre", "body", "A-post", "B-post" },
            result);
    }

    private sealed class P : TaggingDecorator { protected override string Tag => "P"; }

    // Q はチェーン上で P の前 = 内側 wrap
    [RunBefore(typeof(P))]
    private sealed class Q : TaggingDecorator { protected override string Tag => "Q"; }

    [Fact]
    public void RunBefore_makes_decorator_run_earlier_in_chain_innermost_wrapper()
    {
        // 登録順は P, Q。RunBefore(P) で並びは [Q, P] になり、適用は Q.Decorate → P.Decorate。
        // P が後にかぶせるので P が外側、Q が内側。
        // 結果: P-pre / Q-pre / body / Q-post / P-post
        var reg = new SagaDecoratorRegistry();
        reg.Register(new P());
        reg.Register(new Q());

        var result = reg.Apply(typeof(I), Body()).OfType<Mark>().Select(m => m.Tag).ToList();

        Assert.Equal(
            new[] { "P-pre", "Q-pre", "body", "Q-post", "P-post" },
            result);
    }

    // 循環テスト
    [RunBefore(typeof(Y))]
    private sealed class X : TaggingDecorator { protected override string Tag => "X"; }

    [RunBefore(typeof(X))]
    private sealed class Y : TaggingDecorator { protected override string Tag => "Y"; }

    [Fact]
    public void Cycle_in_decorator_chain_throws()
    {
        var reg = new SagaDecoratorRegistry();
        reg.Register(new X());
        reg.Register(new Y());

        var ex = Assert.Throws<InvalidOperationException>(() =>
            reg.Apply(typeof(I), Body()).ToList());
        Assert.Contains("X", ex.Message);
        Assert.Contains("Y", ex.Message);
    }

    // ───────────── Cross-level (継承チェーン全体での topological sort) ─────────────

    private record BaseI : Intent;
    private sealed record SubI : BaseI;

    private abstract class BaseTagging : ISagaDecorator<BaseI>
    {
        protected abstract string Tag { get; }
        public IEnumerable<Effect> Decorate(IEnumerable<Effect> effects)
        {
            yield return new Mark(Tag + "-pre");
            foreach (var e in effects) yield return e;
            yield return new Mark(Tag + "-post");
        }
    }

    private abstract class SubTagging : ISagaDecorator<SubI>
    {
        protected abstract string Tag { get; }
        public IEnumerable<Effect> Decorate(IEnumerable<Effect> effects)
        {
            yield return new Mark(Tag + "-pre");
            foreach (var e in effects) yield return e;
            yield return new Mark(Tag + "-post");
        }
    }

    private sealed class PlainBase : BaseTagging { protected override string Tag => "BASE"; }
    private sealed class PlainSub : SubTagging { protected override string Tag => "SUB"; }

    [Fact]
    public void Default_order_keeps_base_decorator_outermost_for_derived_intent()
    {
        // 順序属性なし: 集約は leaf-first → root-last なので combined = [SUB, BASE]。
        // Apply: stream = SUB(stream), stream = BASE(stream) → BASE が外側。
        // 既存の `Base_decorator_runs_as_outermost_wrapper` と同じ意味の回帰チェック。
        var reg = new SagaDecoratorRegistry();
        reg.Register(new PlainBase());
        reg.Register(new PlainSub());

        var result = reg.Apply(typeof(SubI), Body()).OfType<Mark>().Select(m => m.Tag).ToList();

        Assert.Equal(
            new[] { "BASE-pre", "SUB-pre", "body", "SUB-post", "BASE-post" },
            result);
    }

    // Sub をチェーン後ろ (= 外側) に押し出す。デフォルトと逆。
    [RunAfter(typeof(PlainBase))]
    private sealed class SubAfterBase : SubTagging { protected override string Tag => "SUB"; }

    [Fact]
    public void RunAfter_on_sub_pushes_it_outside_base_decorator()
    {
        var reg = new SagaDecoratorRegistry();
        reg.Register(new PlainBase());
        reg.Register(new SubAfterBase());

        var result = reg.Apply(typeof(SubI), Body()).OfType<Mark>().Select(m => m.Tag).ToList();

        // combined sort 後: [BASE, SUB]。Apply: stream = BASE(s), stream = SUB(s) → SUB が外側。
        Assert.Equal(
            new[] { "SUB-pre", "BASE-pre", "body", "BASE-post", "SUB-post" },
            result);
    }

    // Base をチェーン前 (= 内側) に引き寄せる。デフォルトと逆。
    private sealed class PlainSub2 : SubTagging { protected override string Tag => "SUB"; }

    [RunBefore(typeof(PlainSub2))]
    private sealed class BaseBeforeSub : BaseTagging { protected override string Tag => "BASE"; }

    [Fact]
    public void RunBefore_on_base_pulls_it_inside_sub_decorator()
    {
        var reg = new SagaDecoratorRegistry();
        reg.Register(new BaseBeforeSub());
        reg.Register(new PlainSub2());

        var result = reg.Apply(typeof(SubI), Body()).OfType<Mark>().Select(m => m.Tag).ToList();

        // combined sort 後: [BASE, SUB]。SUB が外側。
        Assert.Equal(
            new[] { "SUB-pre", "BASE-pre", "body", "BASE-post", "SUB-post" },
            result);
    }

    // Cross-level 循環: Sub に [RunBefore(Base)]、Base に [RunBefore(Sub)] = 両者がお互いを「自分より後」と要求 → cycle
    [RunBefore(typeof(CycleBase))]
    private sealed class CycleSub : SubTagging { protected override string Tag => "SUB"; }

    [RunBefore(typeof(CycleSub))]
    private sealed class CycleBase : BaseTagging { protected override string Tag => "BASE"; }

    [Fact]
    public void Cross_level_cycle_is_detected()
    {
        var reg = new SagaDecoratorRegistry();
        reg.Register(new CycleBase());
        reg.Register(new CycleSub());

        var ex = Assert.Throws<InvalidOperationException>(() =>
            reg.Apply(typeof(SubI), Body()).ToList());
        Assert.Contains("CycleSub", ex.Message);
        Assert.Contains("CycleBase", ex.Message);
    }
}
