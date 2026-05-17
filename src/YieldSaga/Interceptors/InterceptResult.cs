namespace YieldSaga;

/// <summary>
/// Spec §5: Interceptor の戻り値。Emit はストリーム中身で 4 モード自動判別される。
///
/// <see cref="EmitResult.Effects"/> は **遅延列挙される** ことに注意 (一度しか走査しない設計)。
/// 呼び出し側 (<see cref="InterceptorRegistry{TState}"/>) は単一 foreach で 1 回だけ enumerate する。
/// Interceptor 実装側で <c>yield return</c> を使って良いし、<see cref="Emit(IEnumerable{Effect})"/>
/// に generator を渡しても良い。 ただし複数回 enumerate されることは想定しない。
/// </summary>
public abstract record InterceptResult
{
    private InterceptResult() { }

    public static InterceptResult Skip { get; } = new SkipResult();
    public static InterceptResult Drop { get; } = new DropResult();
    /// <summary>遅延列挙される (materialize しない)。1 回のみ enumerate される。</summary>
    public static InterceptResult Emit(IEnumerable<Effect> effects) => new EmitResult(effects);
    public static InterceptResult Emit(params Effect[] effects) => new EmitResult(effects);

    public sealed record SkipResult : InterceptResult;
    public sealed record DropResult : InterceptResult;
    public sealed record EmitResult(IEnumerable<Effect> Effects) : InterceptResult;
}
