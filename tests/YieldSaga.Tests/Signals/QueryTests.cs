namespace YieldSaga.Tests.Signals;

/// <summary>
/// Spec §2: Query は State 不変、Interceptor 対象外のマーカー。
/// 値の埋め込みは <see cref="SignalScope"/> の <c>OnQuery&lt;TState&gt;</c> 経由 (Pipeline が処理) なので、
/// マーカー単体の挙動は「Value が代入できる」だけ。
/// </summary>
public class QueryTests
{
    private sealed record S(int Hp);

    [Fact]
    public void Newly_created_Query_has_default_value()
    {
        var q = new Query<S>();
        // class default は null
        Assert.Null(q.Value);
    }

    [Fact]
    public void Value_is_assignable_via_internal_setter_within_assembly()
    {
        var q = new Query<S>();
        q.Value = new S(100);
        Assert.Equal(new S(100), q.Value);
    }

    [Fact]
    public void Query_is_a_Signal_not_an_Event()
    {
        Effect q = new Query<S>();
        Assert.IsAssignableFrom<Signal>(q);
        Assert.False(q is Event);
    }

    [Fact]
    public void Query_typed_inherits_from_abstract_Query_base()
    {
        // Runtime の guardrail が `is Query` で型不一致を検出するため、
        // 派生 Query<TState> は非ジェネリック Query を継承している必要がある
        Effect q = new Query<S>();
        Assert.IsAssignableFrom<Query>(q);
    }

    [Fact]
    public void Read_State_returns_same_instance_via_out_and_return()
    {
        var holder = Read.State<S>(out var byOut);
        Assert.Same(byOut, holder);
    }
}
