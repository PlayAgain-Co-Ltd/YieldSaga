namespace YieldSaga;

/// <summary>
/// Spec §7/§9: 現在の State を覗くための読み取り専用ハンドル。
/// 遅延列挙される EffectPipeline の途中で常に「現時点の State」を返すよう、
/// クロージャ経由で実装する。
/// </summary>
public interface IStateProvider<out TState>
{
    TState Current { get; }
}
