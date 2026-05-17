namespace YieldSaga;

/// <summary>
/// Spec §8: <see cref="EffectHandlerRegistry{TState}"/> で集めた Handler を型階層で引いて
/// <see cref="EffectBatch{TState}"/> に順次配信する。各 effect の Handler 群は Task.WhenAll で並列実行。
///
/// Redux の <c>store.dispatch</c> とは別物（あちらは reducer に action を投げる）。こちらは
/// 副作用 handler に effect をブロードキャストする観測層 broadcaster。
/// </summary>
public sealed class EffectDispatcher<TState>
{
    private readonly EffectHandlerRegistry<TState> _registry;
    private readonly EffectStream _stream = new();

    public EffectDispatcher(EffectHandlerRegistry<TState> registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>各 Effect の配信完了を通知するストリーム。</summary>
    public IObservable<Effect> OnEffect => _stream;

    /// <summary>
    /// Batch 内の Effect を順に取り出し、各々の Handler 群を並列で発火させる。
    /// Handler が例外を投げた場合は <see cref="Task.WhenAll(System.Threading.Tasks.Task[])"/> の挙動に従う。
    /// </summary>
    public async Task DispatchAsync(EffectBatch<TState> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        while (batch.HasCurrent)
        {
            var effect = batch.Current;
            var state = batch.CurrentState;
            var handlers = _registry.Resolve(effect.GetType());
            if (handlers.Count > 0)
                await Task.WhenAll(handlers.Select(h => h.Invoke(effect, state, batch)));
            _stream.OnNext(effect);
            batch.MoveNext();
        }
    }

    /// <summary>IObservable&lt;Effect&gt; の最小実装。OnError/OnCompleted は流さない。</summary>
    private sealed class EffectStream : IObservable<Effect>
    {
        private readonly List<IObserver<Effect>> _observers = new();
        private readonly object _lock = new();

        public IDisposable Subscribe(IObserver<Effect> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            lock (_lock) _observers.Add(observer);
            return new Unsub(this, observer);
        }

        internal void OnNext(Effect effect)
        {
            IObserver<Effect>[] snapshot;
            lock (_lock) snapshot = _observers.ToArray();
            foreach (var o in snapshot) o.OnNext(effect);
        }

        private sealed class Unsub(EffectStream s, IObserver<Effect> o) : IDisposable
        {
            public void Dispose() { lock (s._lock) s._observers.Remove(o); }
        }
    }
}
