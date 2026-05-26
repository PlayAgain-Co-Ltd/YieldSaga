namespace YieldSaga;

/// <summary>
/// Spec §7: 外部入力なしで自走進行するための Producer。
/// State を見て発火条件が成立していたら **単一 Intent** を吐く。
/// Runtime は外部 Update 完了後、CanProduce を満たす Producer が無くなるまで回す（自走ループ）。
///
/// 単一 Intent narrow の理由: チェーン展開 (1 Intent dispatch → state 見て次 Intent) は Saga が
/// Query + 多 Effect yield で表現できる。Producer に同機能を持たせると Saga と重複し、責務もぼやける。
/// Producer は「いつ何の起点 Intent を投げるか」だけに集中する。
///
/// 終了保証はユーザー責任: 同じ Producer が CanProduce=true のまま無限ループしないよう、
/// 各 Producer は発火後に State を変える Intent を吐くこと。
/// </summary>
public interface IAutoIntentProducer<in TState>
{
    /// <summary>現 State でこの Producer が自発的に発火するか。</summary>
    bool CanProduce(TState state);

    /// <summary>起点 Intent を 1 つ産み出す。</summary>
    Intent Produce(TState state);
}
