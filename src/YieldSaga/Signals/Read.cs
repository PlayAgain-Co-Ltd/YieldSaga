namespace YieldSaga;

/// <summary>
/// Saga 内で内部信号を組み立てる糖衣群。SF2 の <c>Read</c> ヘルパーと同じノリ。
/// </summary>
public static class Read
{
    /// <summary>
    /// 「現在 State 寄越せ」マーカーを生成して out で同時に返す。使い方:
    /// <code>
    /// yield return Read.State&lt;Battle&gt;(out var s);
    /// if (s.Value.Hp &lt; 10) yield return new LowHpEvent();
    /// </code>
    /// yield 直後 (= Pipeline が scope の <c>OnQuery&lt;TState&gt;</c> 経由で埋めたあと) に <c>s.Value</c> が最新 State で埋まる。
    /// </summary>
    public static Query<TState> State<TState>(out Query<TState> holder)
    {
        holder = new Query<TState>();
        return holder;
    }
}
