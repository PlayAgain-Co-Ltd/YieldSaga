namespace YieldSaga;

/// <summary>
/// 自走進行用 Producer のディスパッチテーブル。
/// TryProduce は CanProduce を満たす最初の Producer を選ぶ（登録順）。
///
/// Runtime の自走ループは、これが false を返すまで（fixed-point）繰り返し呼ぶ。
/// </summary>
public sealed class AutoIntentProducerRegistry<TState>
{
    private readonly List<IAutoIntentProducer<TState>> _producers = new();

    public void Register(IAutoIntentProducer<TState> producer)
    {
        ArgumentNullException.ThrowIfNull(producer);
        _producers.Add(producer);
    }

    public bool TryProduce(IStateProvider<TState> state, out IEnumerable<Intent> intents)
    {
        ArgumentNullException.ThrowIfNull(state);
        foreach (var p in _producers)
        {
            if (p.CanProduce(state.Current))
            {
                intents = p.Produce(state);
                return true;
            }
        }
        intents = Array.Empty<Intent>();
        return false;
    }
}
