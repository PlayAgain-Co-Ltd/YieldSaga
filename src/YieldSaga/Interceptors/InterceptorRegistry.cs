using System.Collections.Immutable;

namespace YieldSaga;

/// <summary>
/// Spec §5: Event 型ごとに登録された Interceptor を順に走らせ、
/// Skip / Drop / Decorate / Transform / Replace の 4 モードを自動判別して
/// 最終的な Effect 列にする。
///
/// 解決は **継承チェーンを辿る**: leaf 型に登録された Interceptor だけでなく、
/// 基底 Event 型に登録されたものも合流させ、全体を一括 topological sort する。
/// 結果は <see cref="_resolvedCache"/> に memo 化する (新規 Register 時はクリア)。
///
/// 列挙は **完全遅延** (eager List 化しない)。 各 Hook の Emit 結果は単一 foreach で
/// 1 回だけ走査され、yield された各 step は recursive <see cref="ProcessCore"/>
/// 経由で他の Interceptor チェーンに通る。
///
/// 自動判別ルール (emit の中身を見て決まる; 観測可能な挙動として):
///   - 受け取った event を含む (同型 or 派生型 re-emit を含む)
///                                           → 装飾: 周辺 step を recursive Process、
///                                              同型 re-emit のみ起動元 Interceptor を除外して継続
///   - 単一の同型 event                       → 変換: 新値で recursive Process、起動元を除外して継続
///   - それ以外                               → 置換: 起動元を除外したまま emit を recursive Process
///
/// いずれの場合も「起動元 Interceptor が最初に発火 → emit を yield → yield break」
/// という単一構造で実現される (SF2 <c>BattleCommandPipeline.Process</c> と同じ recursive lazy pattern)。
///
/// 順序: <see cref="RunBeforeAttribute"/> / <see cref="RunAfterAttribute"/> によるトポロジカルソート。
/// </summary>
public sealed class InterceptorRegistry<TState>
{
    private readonly Dictionary<Type, List<Registered>> _byEventType = new();
    private readonly Dictionary<Type, List<Registered>> _resolvedCache = new();

    public void Register<TEvent>(IInterceptor<TState, TEvent> interceptor) where TEvent : Event
    {
        ArgumentNullException.ThrowIfNull(interceptor);
        var key = typeof(TEvent);
        if (!_byEventType.TryGetValue(key, out var list))
        {
            list = new List<Registered>();
            _byEventType[key] = list;
        }
        list.Add(new Registered(
            interceptor.GetType(),
            (state, ev) => interceptor.Apply((TEvent)ev, state)));
        _resolvedCache.Clear();
    }

    /// <summary>
    /// Event を Interceptor チェーンに通し、Effect 列を **遅延** 返却する。
    /// 各 step が yield されるまで後続の Interceptor は実行されないことが保証される。
    /// </summary>
    public IEnumerable<Effect> Process(Event ev, TState preState)
        => ProcessCore(ev, preState, ImmutableHashSet<Type>.Empty);

    private IEnumerable<Effect> ProcessCore(Event ev, TState preState, ImmutableHashSet<Type> excluded)
    {
        var eventType = ev.GetType();
        var hooks = Resolve(eventType);

        foreach (var ri in hooks)
        {
            if (excluded.Contains(ri.OwnerType)) continue;
            var result = ri.Apply(preState, ev);

            if (result is InterceptResult.SkipResult) continue;
            if (result is InterceptResult.DropResult) yield break;

            if (result is InterceptResult.EmitResult emit)
            {
                var nextExcluded = excluded.Add(ri.OwnerType);
                foreach (var step in emit.Effects)
                {
                    // 同型 or 派生 Event の re-emit: 起動元を除外して継続 (二重発火防止)。
                    // 異型 Event: 自分の Interceptor チェーンを fresh で通す。
                    // 非 Event: 素通し (Saga 出力以外の Signal/Call は Pipeline 側で再展開される)。
                    if (step is Event reEv)
                    {
                        var childExcluded = eventType.IsAssignableFrom(reEv.GetType())
                            ? nextExcluded
                            : ImmutableHashSet<Type>.Empty;
                        foreach (var inner in ProcessCore(reEv, preState, childExcluded))
                            yield return inner;
                    }
                    else
                    {
                        yield return step;
                    }
                }
                yield break;
            }
        }

        // どの Interceptor も発火しなかった: そのまま yield。
        yield return ev;
    }

    /// <summary>
    /// <paramref name="eventType"/> に対する applicable interceptor リストを返す。
    /// leaf 型から基底へ辿って登録 list を結合し、全体を一括 <see cref="TopologicalSort"/>。
    /// 結果は <see cref="_resolvedCache"/> に memo 化される。
    /// </summary>
    private List<Registered> Resolve(Type eventType)
    {
        if (_resolvedCache.TryGetValue(eventType, out var cached)) return cached;

        var combined = new List<Registered>();
        for (var t = eventType; t is not null && typeof(Event).IsAssignableFrom(t); t = t.BaseType)
        {
            if (_byEventType.TryGetValue(t, out var list))
                combined.AddRange(list);
        }

        if (combined.Count > 1)
            combined = TopologicalSort.Sort(combined, r => r.OwnerType);

        return _resolvedCache[eventType] = combined;
    }

    private readonly record struct Registered(Type OwnerType, Func<TState, Event, InterceptResult> Apply);
}
