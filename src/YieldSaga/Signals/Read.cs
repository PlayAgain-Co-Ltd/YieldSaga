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

    /// <summary>
    /// 入力待ち Take を生成して out で同時に返す（untyped Prompt 版）。SF2 の <c>Read.Input&lt;TInput&gt;</c> と同じ書き味。
    /// <code>
    /// yield return Read.Input&lt;int&gt;(out var move, somePrompt);
    /// yield return new MarkPlacedEvent(move.Value, ...);
    /// </code>
    /// yield 直後 (= Resume で入力が到着したあと) に <c>move.Value</c> が typed で埋まる。
    /// 素の <see cref="Take"/> と違って cast / null-forgive 不要。
    /// </summary>
    public static Take<TInput> Input<TInput>(out Take<TInput> holder, Prompt prompt)
        where TInput : notnull
    {
        holder = new Take<TInput>(prompt);
        return holder;
    }

    /// <summary>
    /// 入力待ち Take を生成して out で同時に返す（typed <see cref="Prompt{TInput}"/> 版、型引数省略可）。
    /// Prompt が自身の入力型を知っているので、呼び出し側で <c>&lt;int&gt;</c> 明示が要らない:
    /// <code>
    /// yield return Read.Input(out var move, new MovePrompt(...));   // MovePrompt : Prompt&lt;int&gt;
    /// yield return new MarkPlacedEvent(move.Value, ...);
    /// </code>
    /// </summary>
    public static Take<TInput> Input<TInput>(out Take<TInput> holder, Prompt<TInput> prompt)
        where TInput : notnull
    {
        holder = new Take<TInput>(prompt);
        return holder;
    }
}
