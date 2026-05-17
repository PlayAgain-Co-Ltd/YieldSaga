using System.Collections.Immutable;

namespace YieldSaga;

/// <summary>
/// Spec §9: Pipeline 処理中の <see cref="Take"/> / <see cref="Query{TState}"/> / <see cref="Call"/>
/// に対する handler 群を保持する immutable な束。
///
/// 2 階層で使われる:
/// - **Dispatch スコープ**: <see cref="Runtime{TState}"/> が組み立てる (必ず <c>OnQuery&lt;TState&gt;</c> 内蔵)。
///   <c>Dispatch / Update / Tick / Resume</c> に optional 引数で渡す user scope と Merge される
/// - **per-Call スコープ**: <see cref="Call.Scope"/> として yield 時に仕込む。Pipeline が Call を展開するとき、
///   Dispatch スコープと Merge して内側の再帰に持ち回る (= サブ Call / Interceptor 発の Signal にも効く)
///
/// 不変条件:
/// - <c>OnXxx</c> / <see cref="Merge"/> は新インスタンスを返す。元 scope は変更されない
/// - 同じ Signal 型に対して複数登録した場合、後勝ち (or null フォールバックでチェーン)
/// - <c>OnTake</c> の resolver が null を返すと、より外側の handler にフォールバックする
/// - <see cref="Merge"/> 引数の other が self を上書き優先 (per-Call が Dispatch を上書き)
/// </summary>
public sealed class SignalScope
{
    /// <summary>handler 1 つも持たない空 scope。</summary>
    public static SignalScope Empty { get; } = new(
        takeResolver: null,
        queryResolvers: ImmutableDictionary<Type, Action<Query>>.Empty,
        callOverrides: ImmutableDictionary<Type, Func<Intent, IEnumerable<Effect>>>.Empty);

    private readonly Func<Take, object?>? _takeResolver;
    private readonly ImmutableDictionary<Type, Action<Query>> _queryResolvers;
    private readonly ImmutableDictionary<Type, Func<Intent, IEnumerable<Effect>>> _callOverrides;

    private SignalScope(
        Func<Take, object?>? takeResolver,
        ImmutableDictionary<Type, Action<Query>> queryResolvers,
        ImmutableDictionary<Type, Func<Intent, IEnumerable<Effect>>> callOverrides)
    {
        _takeResolver = takeResolver;
        _queryResolvers = queryResolvers;
        _callOverrides = callOverrides;
    }

    /// <summary>
    /// 未解決 <see cref="Take"/> に対する resolver を追加する。resolver が non-null を返したら
    /// <see cref="Take.TryResolve"/> に渡される。null を返した場合は既存の resolver にフォールバック。
    /// </summary>
    public SignalScope OnTake(Func<Take, object?> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        var existing = _takeResolver;
        Func<Take, object?> combined = existing is null
            ? resolver
            : t => resolver(t) ?? existing(t);
        return new SignalScope(combined, _queryResolvers, _callOverrides);
    }

    /// <summary>
    /// <see cref="Query{TState}"/> の <see cref="Query{TState}.Value"/> を埋める provider を登録する。
    /// 同じ <typeparamref name="TState"/> に対する provider は後勝ち。
    /// 解決できた Query はストリームから drop される (Pipeline で yield されない)。
    /// </summary>
    public SignalScope OnQuery<TState>(Func<Query<TState>, TState> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Action<Query> erased = q =>
        {
            var typed = (Query<TState>)q;
            typed.Value = provider(typed);
        };
        return new SignalScope(
            _takeResolver,
            _queryResolvers.SetItem(typeof(Query<TState>), erased),
            _callOverrides);
    }

    /// <summary>
    /// <see cref="Call"/> の Intent 型 <typeparamref name="TIntent"/> に対する展開を上書きする。
    /// override されたとき、<see cref="SagaRegistry"/> も <see cref="SagaDecoratorRegistry"/> も skip される
    /// (完全 override セマンティクス)。同 <typeparamref name="TIntent"/> に対する override は後勝ち。
    /// </summary>
    public SignalScope OnCall<TIntent>(Func<TIntent, IEnumerable<Effect>> impl) where TIntent : Intent
    {
        ArgumentNullException.ThrowIfNull(impl);
        Func<Intent, IEnumerable<Effect>> erased = i => impl((TIntent)i);
        return new SignalScope(
            _takeResolver,
            _queryResolvers,
            _callOverrides.SetItem(typeof(TIntent), erased));
    }

    /// <summary>
    /// 2 つの scope を結合する。<paramref name="other"/> が self より優先される
    /// (per-Call が Dispatch を上書きするのを表現)。
    /// </summary>
    public SignalScope Merge(SignalScope? other)
    {
        if (other is null) return this;

        // Take: other を先に試し、null フォールバックで self へ
        Func<Take, object?>? mergedTake = (other._takeResolver, _takeResolver) switch
        {
            (null, null) => null,
            (var o, null) => o,
            (null, var s) => s,
            ({ } o, { } s) => t => o(t) ?? s(t),
        };

        // Query / Call: dict マージ、同 key は other が上書き
        var queries = _queryResolvers;
        foreach (var kv in other._queryResolvers)
            queries = queries.SetItem(kv.Key, kv.Value);

        var calls = _callOverrides;
        foreach (var kv in other._callOverrides)
            calls = calls.SetItem(kv.Key, kv.Value);

        return new SignalScope(mergedTake, queries, calls);
    }

    /// <summary>
    /// Pipeline 内部用。未解決 <see cref="Take"/> に対して resolver を呼び、
    /// 戻り値が non-null なら <see cref="Take.TryResolve"/>。
    /// </summary>
    internal void TryResolveTake(Take take)
    {
        ArgumentNullException.ThrowIfNull(take);
        if (_takeResolver is null || take.IsResolved) return;
        var input = _takeResolver(take);
        if (input is not null) take.TryResolve(input);
    }

    /// <summary>
    /// Pipeline 内部用。<paramref name="q"/> の concrete 型 (<c>Query&lt;TState&gt;</c>) に対する
    /// provider があれば呼んで <c>.Value</c> を埋め、true を返す (ストリームから drop すべき)。
    /// なければ false (Pipeline は yield して下流に流す。Runtime が型不一致を検出して throw)。
    /// </summary>
    internal bool TryResolveQuery(Query q)
    {
        ArgumentNullException.ThrowIfNull(q);
        if (_queryResolvers.TryGetValue(q.GetType(), out var resolver))
        {
            resolver(q);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Pipeline 内部用。<paramref name="intent"/> の concrete 型に対する override があれば
    /// その Effect 列を返す。なければ null (Pipeline は registry / decorator で展開)。
    /// </summary>
    internal IEnumerable<Effect>? TryOverrideCall(Intent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return _callOverrides.TryGetValue(intent.GetType(), out var impl)
            ? impl(intent)
            : null;
    }
}
