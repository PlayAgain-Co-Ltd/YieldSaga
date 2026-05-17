namespace YieldSaga;

/// <summary>
/// Event ランタイム型 → IEventApplier.Apply へのディスパッチテーブル。
/// 1 Event = 1 Applier（仕様 §4）。
///
/// 解決は **継承チェーンを辿る**: 派生 Event 型に直接登録された Applier が無ければ
/// 最も近い基底 Event 型の Applier へ fallback する。チェーンを辿った結果は <see cref="_resolvedCache"/>
/// に alias として memoize される（同じ leaf 型を再ルックアップしても再走査しない）。
/// </summary>
public sealed class EventApplierRegistry<TState>
{
    private readonly Dictionary<Type, Func<TState, Event, TState>> _appliers = new();
    private readonly Dictionary<Type, Func<TState, Event, TState>?> _resolvedCache = new();

    public void Register<TEvent>(IEventApplier<TState, TEvent> applier) where TEvent : Event
    {
        ArgumentNullException.ThrowIfNull(applier);
        var key = typeof(TEvent);
        if (_appliers.ContainsKey(key))
            throw new InvalidOperationException($"Applier for {key.Name} already registered.");
        _appliers[key] = (state, ev) => applier.Apply(state, (TEvent)ev);
        _resolvedCache.Clear();
    }

    public TState Apply(TState state, Event ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        var fn = Resolve(ev.GetType());
        if (fn is null)
            throw new InvalidOperationException($"No applier registered for {ev.GetType().Name}.");
        return fn(state, ev);
    }

    private Func<TState, Event, TState>? Resolve(Type eventType)
    {
        if (_resolvedCache.TryGetValue(eventType, out var cached)) return cached;
        for (var t = eventType; t is not null && typeof(Event).IsAssignableFrom(t); t = t.BaseType)
        {
            if (_appliers.TryGetValue(t, out var fn))
                return _resolvedCache[eventType] = fn;
        }
        return _resolvedCache[eventType] = null;
    }
}
