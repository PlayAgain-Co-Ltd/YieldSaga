namespace YieldSaga;

/// <summary>
/// 自走進行用 Producer のディスパッチテーブル。
/// TryProduce は CanProduce を満たす最初の Producer を選び、その単一 Intent を返す（登録順）。
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

    public bool TryProduce(TState state, out Intent intent)
    {
        foreach (var p in _producers)
        {
            if (p.CanProduce(state))
            {
                intent = p.Produce(state);
                return true;
            }
        }
        intent = null!;
        return false;
    }
}
