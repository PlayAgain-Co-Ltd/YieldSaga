namespace YieldSaga;

/// <summary>
/// Spec §7: 外部入力を **単一 Intent** に変換する Producer。
/// Runtime.Update に渡された入力に対し、CanProduce を満たした最初の Producer が選ばれる。
///
/// 設計上の narrowing:
/// - Effect ではなく Intent (Saga が Intent→Effect 展開を持つので Producer はそこに集中)
/// - 列ではなく単一 (チェーンは Saga 側で表現、Producer は起点だけ)
/// </summary>
public interface IIntentProducer<in TInput, in TState>
{
    /// <summary>この入力 + 現 State でこの Producer が発火するか。</summary>
    bool CanProduce(TInput input, TState state);

    /// <summary>起点 Intent を 1 つ産み出す。</summary>
    Intent Produce(TInput input, TState state);
}
