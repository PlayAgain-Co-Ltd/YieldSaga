namespace YieldSaga;

/// <summary>
/// Spec §2: 外側で値が埋まる照会の抽象基底。State 不変、Interceptor 対象外。
/// 非ジェネリックの基底があるのは、TState が Runtime と一致しなかった未解決 Query を
/// Runtime 側で検出して throw するため（型階層で `is Query` チェックできるように）。
/// </summary>
public abstract record Query : Signal;

/// <summary>
/// Spec §2 / §9: Saga が「現在の State を寄越せ」と yield する内部信号。
/// SF2 の <c>ContextRead&lt;TContext&gt;</c> と同じ役割。
///
/// 使い方: <c>yield return Read.State&lt;TState&gt;(out var s);</c> のあと <c>s.Value</c> でアクセス。
/// Runtime が <see cref="SignalScope"/> に <c>OnQuery&lt;TState&gt;(_ =&gt; Latest)</c> を内蔵して Pipeline に渡すので、
/// yield の直後に <see cref="Value"/> が「現時点の最新 State」で埋まる。マーカー自身は trace に乗らない。
/// </summary>
public sealed record Query<TState> : Query
{
    /// <summary>
    /// Pipeline が <see cref="SignalScope.TryResolveQuery"/> を通して最新 State を代入する。
    /// 未通過の状態で読むと <c>default(TState)</c>（class なら null）— その状況は Runtime が throw で検出。
    /// </summary>
    public TState Value { get; internal set; } = default!;
}
