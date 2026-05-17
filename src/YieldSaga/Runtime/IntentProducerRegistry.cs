namespace YieldSaga;

/// <summary>
/// 外部入力型 → IIntentProducer のディスパッチテーブル。
/// 入力型ごとに複数の Producer を登録でき、TryProduce 時は CanProduce を満たす最初の
/// Producer を選ぶ（登録順）。
///
/// 入力型のルックアップは <c>typeof(TInput)</c> で行う（コンパイル時型で完全一致）。
/// 多相が必要なら、共通インターフェースを TInput にして個別の Producer をその傘下にぶら下げる。
/// </summary>
public sealed class IntentProducerRegistry<TState>
{
    private readonly Dictionary<Type, object> _buckets = new();

    public void Register<TInput>(IIntentProducer<TInput, TState> producer)
    {
        ArgumentNullException.ThrowIfNull(producer);
        if (!_buckets.TryGetValue(typeof(TInput), out var bucket))
        {
            bucket = new Bucket<TInput>();
            _buckets[typeof(TInput)] = bucket;
        }
        ((Bucket<TInput>)bucket).Add(producer);
    }

    /// <summary>
    /// 入力にマッチする最初の Producer の Intent 列を返す。
    /// 入力型未登録、あるいは全 Producer が CanProduce=false なら false。
    /// </summary>
    public bool TryProduce<TInput>(TInput input, IStateProvider<TState> state, out IEnumerable<Intent> intents)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_buckets.TryGetValue(typeof(TInput), out var bucket))
            return ((Bucket<TInput>)bucket).TryProduce(input, state, out intents);
        intents = Array.Empty<Intent>();
        return false;
    }

    private sealed class Bucket<TInput>
    {
        private readonly List<IIntentProducer<TInput, TState>> _producers = new();

        public void Add(IIntentProducer<TInput, TState> p) => _producers.Add(p);

        public bool TryProduce(TInput input, IStateProvider<TState> state, out IEnumerable<Intent> intents)
        {
            foreach (var p in _producers)
            {
                if (p.CanProduce(input, state.Current))
                {
                    intents = p.Produce(input, state);
                    return true;
                }
            }
            intents = Array.Empty<Intent>();
            return false;
        }
    }
}
