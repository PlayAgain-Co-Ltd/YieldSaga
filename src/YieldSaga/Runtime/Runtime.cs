namespace YieldSaga;

/// <summary>
/// Spec §7: EffectPipeline を駆動するエンジン。
///
/// 3 つの入口がある:
/// - <see cref="Dispatch"/>: 単一 Intent を直接展開する低レベル API
/// - <see cref="Update"/>: 外部入力を <see cref="IIntentProducer{TInput,TState}"/> で Intent 列に変換し、
///   完了後に <see cref="IAutoIntentProducer{TState}"/> の自走ループを fixed-point まで回す
/// - <see cref="Tick"/>: 外部入力なしで自走ループだけ回す
///
/// いずれも optional <see cref="SignalScope"/> を受け取り、TState 用 <c>OnQuery</c> を必ず内蔵した
/// 合成 scope を Pipeline に渡す。これにより:
/// - Saga が yield する <c>Read.State&lt;TState&gt;</c> が常に最新 State で埋まる
/// - ユーザは <c>scope</c> 引数で per-Dispatch な Take / Query / Call 上書きができる
///
/// 遅延列挙しつつ Event を State に適用し、未解決の <see cref="Take"/> に当たったら中断する。
/// <see cref="Resume"/> で入力を渡せば中断した saga の続きを実行し、Update / Tick 中に積まれた
/// 残り Intent と自走ループも引き継いで完走する。Resume にも optional scope を渡せる
/// (中断 iter 自体は元の scope を保持、続きの dispatch から効く)。
/// </summary>
public sealed class Runtime<TState>
{
    private readonly EffectPipeline _pipeline;
    private readonly EventApplierRegistry<TState> _appliers;
    private readonly IntentProducerRegistry<TState>? _producers;
    private readonly AutoIntentProducerRegistry<TState>? _autoProducers;

    // Saga 中断: 現在 Take 待ちの iterator
    private IEnumerator<Effect>? _suspended;
    private Take? _pendingTake;

    // Update 中の Take 中断は無くなった (Producer は単一 Intent narrow)
    // ただし autoLoop からの中断は次 Tick まで継続するため、autoAfter フラグだけ保持
    private Sender? _pendingSender;
    private bool _autoAfterPending;
    private SignalScope? _pendingScope;   // autoLoop 用の composite scope

    public Runtime(
        TState initial,
        EffectPipeline pipeline,
        EventApplierRegistry<TState> appliers,
        IntentProducerRegistry<TState>? producers = null,
        AutoIntentProducerRegistry<TState>? autoProducers = null)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(appliers);
        Latest = initial;
        _pipeline = pipeline;
        _appliers = appliers;
        _producers = producers;
        _autoProducers = autoProducers;
    }

    /// <summary>
    /// Saga / Applier / Decorator / Interceptor / Producer を渡して Pipeline を内蔵構築する便宜コンストラクタ。
    /// Interceptor は適用時の preState として <see cref="Latest"/> を読む。
    /// </summary>
    public Runtime(
        TState initial,
        SagaRegistry sagas,
        EventApplierRegistry<TState> appliers,
        InterceptorRegistry<TState>? interceptors = null,
        SagaDecoratorRegistry? decorators = null,
        IntentProducerRegistry<TState>? producers = null,
        AutoIntentProducerRegistry<TState>? autoProducers = null)
    {
        ArgumentNullException.ThrowIfNull(sagas);
        ArgumentNullException.ThrowIfNull(appliers);
        Latest = initial;
        _appliers = appliers;
        _producers = producers;
        _autoProducers = autoProducers;
        Func<Event, IEnumerable<Effect>>? transformer = interceptors is null
            ? null
            : ev => interceptors.Process(ev, Latest);
        _pipeline = new EffectPipeline(sagas, decorators, transformer);
    }

    public TState Latest { get; private set; }

    public IStateProvider<TState> StateProvider => new SnapshotProvider(this);

    public bool IsPaused => _suspended is not null;

    /// <summary>現在 Resume 待ちの Take（未中断時は null）。</summary>
    public Take? PendingTake => _pendingTake;

    /// <summary>
    /// Intent を 1 つ展開する。生成された Effect ごとに State を更新し、(Effect, State) を返す。
    /// 未解決の Take に当たったら手前までを返して中断する。
    /// Update と違って自走ループは回さない（低レベル API）。
    /// </summary>
    public IReadOnlyList<(Effect Effect, TState State)> Dispatch(
        Intent intent, Sender sender, SignalScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(sender);
        EnsureNotPaused();

        var effective = BuildScope(scope);
        var trace = new List<(Effect, TState)>();
        DispatchOne(intent, sender, effective, trace);
        return trace;
    }

    /// <summary>
    /// 外部入力 1 つを処理する。<see cref="IIntentProducer{TInput,TState}"/> で Intent 列を作り、
    /// 順次 Dispatch し、最後に自走ループ（<see cref="IAutoIntentProducer{TState}"/>）を fixed-point まで回す。
    /// 途中で Take に当たれば中断し、残り Intent と「終わったら自走」フラグが保持される。
    /// </summary>
    public IReadOnlyList<(Effect Effect, TState State)> Update<TInput>(
        TInput input, Sender sender, SignalScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(sender);
        EnsureNotPaused();
        if (_producers is null)
            throw new InvalidOperationException("Update requires an IntentProducerRegistry. None was configured.");

        if (!_producers.TryProduce(input, Latest, out var intent))
            throw new InvalidOperationException($"No IIntentProducer matched input of type {typeof(TInput).Name}.");

        var effective = BuildScope(scope);
        var trace = new List<(Effect, TState)>();
        DispatchOne(intent, sender, effective, trace);
        if (IsPaused)
        {
            // Update の Intent が Take で中断 → 続きの autoLoop は Resume 後に
            _pendingSender = sender;
            _pendingScope = effective;
            _autoAfterPending = true;
            return trace;
        }
        RunAutoLoop(sender, effective, trace);
        return trace;
    }

    /// <summary>
    /// 外部入力なしで自走ループだけを回す。CanProduce を満たす Producer が無くなるまで Intent を吐かせる。
    /// 途中で Take に当たれば中断し、Resume で続行可能。
    /// </summary>
    public IReadOnlyList<(Effect Effect, TState State)> Tick(
        Sender sender, SignalScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(sender);
        EnsureNotPaused();

        var effective = BuildScope(scope);
        var trace = new List<(Effect, TState)>();
        RunAutoLoop(sender, effective, trace);
        return trace;
    }

    /// <summary>
    /// 中断中の Take に入力を渡して続行する。Prompt.Accepts が false なら拒否し
    /// 空列を返して中断状態を維持する。Resume 後、saga が完走したら Update/Tick で積まれていた
    /// 残り Intent と自走ループも引き継いで完走する。
    /// <paramref name="scope"/> を渡すと、中断元の保留 scope と Merge されて続きの dispatch に効く
    /// (中断 iter 自体は作成時の scope を保持しているので影響しない)。
    /// </summary>
    public IReadOnlyList<(Effect Effect, TState State)> Resume(
        object input, SignalScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!IsPaused)
            throw new InvalidOperationException("Runtime is not paused. Call Dispatch / Update / Tick instead.");

        if (!_pendingTake!.TryResolve(input))
            return Array.Empty<(Effect, TState)>();

        var iter = _suspended!;
        _suspended = null;
        _pendingTake = null;

        var trace = new List<(Effect, TState)>();
        DrainSaga(iter, trace);
        if (IsPaused)
            return trace; // 同 saga 内の別 Take で再び停まった

        // Update/Tick 中に保留された自走ループを引き継ぐ
        if (_autoAfterPending)
        {
            var pendingSender = _pendingSender!;
            var followOnScope = scope is null
                ? _pendingScope!
                : _pendingScope!.Merge(scope);
            _pendingSender = null;
            _pendingScope = null;
            _autoAfterPending = false;
            RunAutoLoop(pendingSender, followOnScope, trace);
        }
        return trace;
    }

    /// <summary>
    /// CanProduce を満たす Auto Producer が無くなるまで、単一 Intent を順次 Dispatch する。
    /// 中断したら次 Resume に再開を引き継ぐため pendingSender/Scope/autoAfter を保持して即帰る。
    /// </summary>
    private void RunAutoLoop(Sender sender, SignalScope scope, List<(Effect, TState)> trace)
    {
        if (_autoProducers is null) return;
        while (true)
        {
            if (!_autoProducers.TryProduce(Latest, out var intent))
                return;
            DispatchOne(intent, sender, scope, trace);
            if (IsPaused)
            {
                _pendingSender = sender;
                _pendingScope = scope;
                _autoAfterPending = true;
                return;
            }
        }
    }

    private void DispatchOne(
        Intent intent, Sender sender, SignalScope scope, List<(Effect, TState)> trace)
    {
        // Pipeline に scope を渡す。scope には必ず OnQuery<TState>(_ => Latest) が仕込まれているので、
        // saga が yield した Query<TState> は最新 State で埋まり、ストリームから除外される。
        var stream = _pipeline.Process(new Call(intent, sender), scope);
        var iter = stream.GetEnumerator();
        DrainSaga(iter, trace);
    }

    private void DrainSaga(IEnumerator<Effect> iter, List<(Effect, TState)> trace)
    {
        while (iter.MoveNext())
        {
            var effect = iter.Current;
            // Guardrail: scope が一致しなかった Query が漏れてきた = TState 不一致
            // （Runtime<TState> の saga が Read.State<別型>(...) を yield したパターン）
            if (effect is Query unresolved)
                throw new InvalidOperationException(
                    $"Unresolved {unresolved.GetType().Name} reached Runtime<{typeof(TState).Name}>. " +
                    "Query's TState does not match this Runtime — check your Read.State<T>(...) call.");
            if (effect is Event ev)
                Latest = _appliers.Apply(Latest, ev);
            trace.Add((effect, Latest));

            if (effect is Take take && !take.IsResolved)
            {
                // Pipeline で Prompt.GetAutoResolution と scope.OnTake は既に試行済み。
                // ここに来た = 両方とも解決できなかった → 中断して外部 Resume を待つ。
                _suspended = iter;
                _pendingTake = take;
                return;
            }
        }
        iter.Dispose();
    }

    private SignalScope BuildScope(SignalScope? userScope)
    {
        return SignalScope.Empty
            .OnQuery<TState>(_ => Latest)
            .Merge(userScope);
    }

    private void EnsureNotPaused()
    {
        if (IsPaused)
            throw new InvalidOperationException("Runtime is paused on a Take. Call Resume first.");
    }

    private sealed class SnapshotProvider(Runtime<TState> owner) : IStateProvider<TState>
    {
        public TState Current => owner.Latest;
    }
}
