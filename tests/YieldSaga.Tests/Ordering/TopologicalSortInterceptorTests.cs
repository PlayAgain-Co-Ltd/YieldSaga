namespace YieldSaga.Tests.Ordering;

/// <summary>
/// Spec §5: [RunBefore]/[RunAfter] による Interceptor の順序制御を検証。
/// 検証方法: prefix を蓄積する Decorate チェーンで実行順を観測する。
/// </summary>
public class TopologicalSortInterceptorTests
{
    private sealed record S(int X);
    private sealed record E(int Value) : Event;
    private sealed record Tag(string Name) : Event;

    /// <summary>
    /// Tag(name) を prefix に積みつつ event を通す Decorate-モード Interceptor。
    /// 実行順序がそのまま prefix の登場順になる。
    /// </summary>
    private abstract class TagInterceptor : IInterceptor<S, E>
    {
        protected abstract string Name { get; }
        public InterceptResult Apply(E ev, S preState) =>
            InterceptResult.Emit(new Tag(Name), ev);
    }

    private sealed class A : TagInterceptor { protected override string Name => "A"; }

    [RunAfter(typeof(A))]
    private sealed class B : TagInterceptor { protected override string Name => "B"; }

    [RunAfter(typeof(B))]
    private sealed class C : TagInterceptor { protected override string Name => "C"; }

    [RunBefore(typeof(A))]
    private sealed class Z : TagInterceptor { protected override string Name => "Z"; }

    private static List<string> TagsOf(IEnumerable<Effect> stream)
        => stream.OfType<Tag>().Select(t => t.Name).ToList();

    [Fact]
    public void RunAfter_pulls_interceptor_to_after_target_despite_registration_order()
    {
        // 登録順は B, A だが [RunAfter(A)] によって A→B になるべき
        var reg = new InterceptorRegistry<S>();
        reg.Register(new B());
        reg.Register(new A());

        var result = reg.Process(new E(1), new S(0));

        Assert.Equal(new[] { "A", "B" }, TagsOf(result));
    }

    [Fact]
    public void RunBefore_pulls_interceptor_to_before_target_despite_registration_order()
    {
        // 登録順は A, Z だが [RunBefore(A)] によって Z→A になるべき
        var reg = new InterceptorRegistry<S>();
        reg.Register(new A());
        reg.Register(new Z());

        var result = reg.Process(new E(1), new S(0));

        Assert.Equal(new[] { "Z", "A" }, TagsOf(result));
    }

    [Fact]
    public void Chained_constraints_compose()
    {
        // A → B → C を要請しているが、登録順は C, B, A
        var reg = new InterceptorRegistry<S>();
        reg.Register(new C());
        reg.Register(new B());
        reg.Register(new A());

        var result = reg.Process(new E(1), new S(0));

        Assert.Equal(new[] { "A", "B", "C" }, TagsOf(result));
    }

    [Fact]
    public void Items_without_constraints_keep_registration_order()
    {
        // X, Y は無制約。X, Y の順で登録 → そのまま X, Y
        var reg = new InterceptorRegistry<S>();
        reg.Register(new X1());
        reg.Register(new Y1());

        var result = reg.Process(new E(1), new S(0));

        Assert.Equal(new[] { "X", "Y" }, TagsOf(result));
    }

    private sealed class X1 : TagInterceptor { protected override string Name => "X"; }
    private sealed class Y1 : TagInterceptor { protected override string Name => "Y"; }

    // 循環検出
    [RunAfter(typeof(P2))]
    private sealed class P1 : TagInterceptor { protected override string Name => "P1"; }

    [RunAfter(typeof(P1))]
    private sealed class P2 : TagInterceptor { protected override string Name => "P2"; }

    [Fact]
    public void Cycle_throws_with_member_names_in_message()
    {
        var reg = new InterceptorRegistry<S>();
        reg.Register(new P1());
        reg.Register(new P2());

        // Process は遅延列挙なので、enumerate して初めて Resolve → トポロジカルソート → 循環検出が走る
        var ex = Assert.Throws<InvalidOperationException>(() => reg.Process(new E(1), new S(0)).ToList());
        Assert.Contains("P1", ex.Message);
        Assert.Contains("P2", ex.Message);
    }
}
