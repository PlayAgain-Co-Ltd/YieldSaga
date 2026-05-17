namespace YieldSaga;

/// <summary>
/// Spec §7: 外部入力を Intent 列に変換する Producer。
/// Runtime.Update に渡された入力に対し、CanProduce を満たした最初の Producer が選ばれる。
///
/// 設計上の narrowing: Producer は Effect ではなく Intent を吐く。Saga が既に Intent→Effect の
/// 展開ロジックを持っているので、Producer は「いつ何の Intent を投げるか」だけに集中する
/// （Interceptor / SagaDecorator / Call の再帰展開も全部 Saga 経由で素通り）。
/// </summary>
public interface IIntentProducer<in TInput, in TState>
{
    /// <summary>この入力 + 現 State でこの Producer が発火するか。</summary>
    bool CanProduce(TInput input, TState state);

    /// <summary>
    /// Intent 列を産み出す。遅延列挙される（各 Intent を Dispatch した直後の State を
    /// <paramref name="state"/> 経由で読みつつ次の Intent を決められる）。
    /// </summary>
    IEnumerable<Intent> Produce(TInput input, IStateProvider<TState> state);
}
