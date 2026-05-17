namespace YieldSaga;

/// <summary>
/// Spec §8: Runtime が出した Effect を購読して副作用を起こす（描画・音・ログ・永続化）。
///
/// 性質:
/// - 同じ Effect に複数の Handler が並列に呼ばれる（Task.WhenAll）
/// - State は変えられない（変えたければ Interceptor で）
/// - 型階層で自動ディスパッチ: TEffect の継承親に登録された Handler も実体の派生型に対して全て呼ばれる
///
/// state は <see cref="EffectBatch{TState}.CurrentState"/> と同じ「effect 適用後」の State。
/// </summary>
public interface IEffectHandler<in TEffect, TState> where TEffect : Effect
{
    Task OnEffectAsync(TEffect effect, TState state, EffectBatch<TState> batch);
}
