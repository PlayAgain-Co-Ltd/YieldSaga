namespace YieldSaga;

/// <summary>
/// Spec §3: Intent 継承チェーンに沿って Decorator を積層する。
///
/// 解決は **継承チェーン全体を 1 つの list に集約してから一括 topological sort** する
/// (SF2 <c>BattleCommandPipeline.BuildModifiers</c> と同じ pattern)。これにより
/// <see cref="RunBeforeAttribute"/> / <see cref="RunAfterAttribute"/> がクロス階層でも効く
/// (例: 派生 Intent の Decorator に <c>[RunAfter(typeof(BaseDecorator))]</c> を付けて、
/// デフォルトでは外側になる基底 Decorator より外側へ押し出す等)。
///
/// 集約は **leaf-first → root-last** で行うため、順序属性が無い場合のデフォルトは
/// 「具象 Intent の Decorator が内側、基底 Intent の Decorator が外側」となる。
/// 解決結果は <see cref="_resolvedCache"/> に memo 化 (新規 Register 時はクリア)。
///
/// 適用は <c>foreach (var d in resolved) stream = d.Decorate(stream);</c>。
/// 後に呼ばれた Decorate が外側で wrap するため、list の末尾要素が最外層 wrapper になる。
/// </summary>
public sealed class SagaDecoratorRegistry
{
    private readonly Dictionary<Type, List<Registered>> _byIntentType = new();
    private readonly Dictionary<Type, List<Registered>> _resolvedCache = new();

    public void Register<TIntent>(ISagaDecorator<TIntent> decorator) where TIntent : Intent
    {
        ArgumentNullException.ThrowIfNull(decorator);
        var key = typeof(TIntent);
        if (!_byIntentType.TryGetValue(key, out var list))
        {
            list = new List<Registered>();
            _byIntentType[key] = list;
        }
        list.Add(new Registered(decorator.GetType(), decorator.Decorate));
        _resolvedCache.Clear();
    }

    /// <summary>
    /// 与えられた Intent 型のチェーン全体を sort 済み Decorator list に解決し、順に適用する。
    /// stream は遅延列挙のまま wrap される。
    /// </summary>
    public IEnumerable<Effect> Apply(Type intentType, IEnumerable<Effect> stream)
    {
        ArgumentNullException.ThrowIfNull(intentType);
        ArgumentNullException.ThrowIfNull(stream);

        var decorators = Resolve(intentType);
        foreach (var d in decorators)
            stream = d.Decorate(stream);
        return stream;
    }

    /// <summary>
    /// <paramref name="intentType"/> の継承チェーン上に登録された全 Decorator を leaf-first で集約し、
    /// 全体に 1 回 topological sort をかけた結果を返す。結果は memo 化される。
    /// </summary>
    private List<Registered> Resolve(Type intentType)
    {
        if (_resolvedCache.TryGetValue(intentType, out var cached)) return cached;

        var combined = new List<Registered>();
        for (var t = intentType; t is not null && typeof(Intent).IsAssignableFrom(t); t = t.BaseType)
        {
            if (_byIntentType.TryGetValue(t, out var list))
                combined.AddRange(list);
        }

        if (combined.Count > 1)
            combined = TopologicalSort.Sort(combined, r => r.OwnerType);

        return _resolvedCache[intentType] = combined;
    }

    private readonly record struct Registered(
        Type OwnerType,
        Func<IEnumerable<Effect>, IEnumerable<Effect>> Decorate);
}
