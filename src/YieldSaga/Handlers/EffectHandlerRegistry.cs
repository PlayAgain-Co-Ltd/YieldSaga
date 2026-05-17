namespace YieldSaga;

/// <summary>
/// Spec §8: Effect 型 → IEffectHandler 群のディスパッチテーブル。
///
/// 解決は **継承チェーン上の全 Handler** を返す（<see cref="EventApplierRegistry{TState}"/> と違い、
/// 最も近い 1 つではなく親も子も全部発火させる）。<see cref="_resolveCache"/> でメモ化する。
/// </summary>
public sealed class EffectHandlerRegistry<TState>
{
    private readonly Dictionary<Type, List<HandlerEntry>> _handlers = new();
    private readonly Dictionary<Type, IReadOnlyList<HandlerEntry>> _resolveCache = new();

    /// <summary>handler 本体（identity 比較用）と invoke delegate のペア。</summary>
    internal sealed record HandlerEntry(object Handler, Func<Effect, TState, EffectBatch<TState>, Task> Invoke);

    public void Register<TEffect>(IEffectHandler<TEffect, TState> handler) where TEffect : Effect
    {
        ArgumentNullException.ThrowIfNull(handler);
        var key = typeof(TEffect);
        if (!_handlers.TryGetValue(key, out var list))
            _handlers[key] = list = new();
        list.Add(new HandlerEntry(
            handler,
            (effect, state, batch) => handler.OnEffectAsync((TEffect)effect, state, batch)));
        _resolveCache.Clear();
    }

    /// <summary>
    /// effectType の実体型から Effect まで親チェーンを辿り、各レイヤに登録された Handler を順に集める。
    /// </summary>
    internal IReadOnlyList<HandlerEntry> Resolve(Type effectType)
    {
        ArgumentNullException.ThrowIfNull(effectType);
        if (_resolveCache.TryGetValue(effectType, out var cached)) return cached;

        var result = new List<HandlerEntry>();
        for (var t = effectType; t is not null && typeof(Effect).IsAssignableFrom(t); t = t.BaseType)
        {
            if (_handlers.TryGetValue(t, out var list))
                result.AddRange(list);
        }
        return _resolveCache[effectType] = result;
    }
}
