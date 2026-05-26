namespace YieldSaga;

/// <summary>
/// 外部入力型 → IIntentProducer のディスパッチテーブル。
/// 入力型ごとに複数の Producer を登録でき、TryProduce 時は CanProduce を満たす最初の
/// Producer の単一 Intent を返す（登録順）。
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
    /// 入力にマッチする最初の Producer の単一 Intent を返す。
    /// 入力型未登録、あるいは全 Producer が CanProduce=false なら false。
    /// </summary>
    public bool TryProduce<TInput>(TInput input, TState state, out Intent intent)
    {
        if (_buckets.TryGetValue(typeof(TInput), out var bucket))
            return ((Bucket<TInput>)bucket).TryProduce(input, state, out intent);
        intent = null!;
        return false;
    }

    private sealed class Bucket<TInput>
    {
        private readonly List<IIntentProducer<TInput, TState>> _producers = new();

        public void Add(IIntentProducer<TInput, TState> p) => _producers.Add(p);

        public bool TryProduce(TInput input, TState state, out Intent intent)
        {
            foreach (var p in _producers)
            {
                if (p.CanProduce(input, state))
                {
                    intent = p.Produce(input, state);
                    return true;
                }
            }
            intent = null!;
            return false;
        }
    }
}
