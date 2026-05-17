namespace YieldSaga;

/// <summary>
/// Spec §9: Effect を 1 つ受け取り、Saga / Decorator / Interceptor 経由で Effect 列に展開する。
///
/// 展開ルール (<see cref="Process"/>):
/// - <see cref="Call"/>: dispatch scope と <see cref="Call.Scope"/> を Merge した <c>inner</c> scope を作り、
///   <c>inner.OnCall&lt;TIntent&gt;</c> 上書きがあればそれを採用 (Decorator chain も skip = 完全 override)。
///   なければ <see cref="SagaRegistry"/> + <see cref="SagaDecoratorRegistry"/> で展開。
///   結果を <see cref="ExpandRecursively"/> で再帰展開し、各 Effect に <c>inner</c> を持ち回る。
/// - <see cref="Event"/>: Interceptor チェーン (<see cref="_eventTransformer"/>) を通す。
///   Interceptor が emit した非 Event は <c>Process(t, scope)</c> で再投入 (= scope が深さに関わらず効く)。
/// - <see cref="Take"/>: 解決順序は <c>Prompt.GetAutoResolution → scope.OnTake → 未解決のまま yield</c>。
///   Runtime 側で <c>!IsResolved</c> なら中断する既存挙動と整合する。
/// - <see cref="Query{TState}"/>: <c>scope.OnQuery&lt;TState&gt;</c> で解決できたら <c>.Value</c> を埋めて drop。
///   できなければ yield (Runtime が型不一致を throw 検出)。
///
/// 遅延列挙する。呼び出し側は 1 Effect 取り出すごとに State へ適用してから次を引くこと。
/// </summary>
public sealed class EffectPipeline
{
    private readonly SagaRegistry _sagas;
    private readonly SagaDecoratorRegistry? _decorators;
    private readonly Func<Event, IEnumerable<Effect>>? _eventTransformer;

    public EffectPipeline(
        SagaRegistry sagas,
        SagaDecoratorRegistry? decorators = null,
        Func<Event, IEnumerable<Effect>>? eventTransformer = null)
    {
        ArgumentNullException.ThrowIfNull(sagas);
        _sagas = sagas;
        _decorators = decorators;
        _eventTransformer = eventTransformer;
    }

    /// <summary>
    /// <paramref name="effect"/> を展開する。<paramref name="scope"/> は省略可
    /// (null 渡しは <see cref="SignalScope.Empty"/> と同等)。Runtime 経由の通常ルートでは
    /// TState 用 <c>OnQuery</c> を内蔵した scope が必ず渡される。
    /// </summary>
    public IEnumerable<Effect> Process(Effect effect, SignalScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(effect);
        scope ??= SignalScope.Empty;

        if (effect is Call call)
        {
            // per-Call Scope を merge して以降の再帰に持ち回る
            var inner = scope.Merge(call.Scope);

            // scope.OnCall<TIntent> による完全 override (Decorator chain も skip)
            var overridden = inner.TryOverrideCall(call.Intent);
            IEnumerable<Effect> expanded;
            if (overridden is not null)
            {
                expanded = overridden;
            }
            else
            {
                expanded = _sagas.Run(call.Intent, call.Sender);
                if (_decorators is not null)
                    expanded = _decorators.Apply(call.Intent.GetType(), expanded);
            }

            foreach (var e in ExpandRecursively(expanded, inner))
                yield return e;
            yield break;
        }

        if (effect is Event ev)
        {
            foreach (var t in TransformEvent(ev, scope))
                yield return t;
            yield break;
        }

        if (effect is Take take)
        {
            // 順序: Prompt.GetAutoResolution → scope.OnTake → 未解決のまま yield
            if (!take.IsResolved)
            {
                var auto = take.Prompt.GetAutoResolution();
                if (auto is not null)
                    take.TryResolve(auto);
                else
                    scope.TryResolveTake(take);
            }
            yield return take;
            yield break;
        }

        if (effect is Query q)
        {
            if (scope.TryResolveQuery(q)) yield break;   // 解決できたら drop
            yield return q;                              // 未解決は yield (Runtime が throw 検出)
            yield break;
        }

        yield return effect;
    }

    private IEnumerable<Effect> ExpandRecursively(IEnumerable<Effect> effects, SignalScope scope)
    {
        foreach (var e in effects)
        {
            foreach (var inner in Process(e, scope))
                yield return inner;
        }
    }

    private IEnumerable<Effect> TransformEvent(Event ev, SignalScope scope)
    {
        if (_eventTransformer is null)
        {
            yield return ev;
            yield break;
        }

        // Transformer の出力 Event は最終形 (チェーン適用済み)。
        // 非 Event (Call / Signal) は Pipeline で改めて展開し、scope を持ち回る。
        foreach (var t in _eventTransformer(ev))
        {
            if (t is Event)
                yield return t;
            else
                foreach (var inner in Process(t, scope))
                    yield return inner;
        }
    }
}
