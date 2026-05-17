namespace YieldSaga;

/// <summary>
/// Spec §7: 外部入力なしで自走進行するための Producer。
/// State を見て発火条件が成立していたら Intent を吐く。
/// Runtime は外部 Update 完了後、CanProduce を満たす Producer が無くなるまで回す（自走ループ）。
///
/// 終了保証はユーザー責任: 同じ Producer が CanProduce=true のまま無限ループしないよう、
/// 各 Producer は発火後に State を変える Intent を吐くこと。
/// </summary>
public interface IAutoIntentProducer<in TState>
{
    /// <summary>現 State でこの Producer が自発的に発火するか。</summary>
    bool CanProduce(TState state);

    /// <summary>Intent 列を産み出す（遅延列挙）。</summary>
    IEnumerable<Intent> Produce(IStateProvider<TState> state);
}
