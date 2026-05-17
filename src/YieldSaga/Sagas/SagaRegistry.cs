namespace YieldSaga;

/// <summary>
/// Intent ランタイム型 → ISaga.Run へのディスパッチテーブル。
/// 同じ Intent 型に複数の Saga は登録できない（仕様 §3 の純粋関数前提）。
/// </summary>
public sealed class SagaRegistry
{
    private readonly Dictionary<Type, Func<Intent, Sender, IEnumerable<Effect>>> _sagas = new();

    public void Register<TIntent>(ISaga<TIntent> saga) where TIntent : Intent
    {
        ArgumentNullException.ThrowIfNull(saga);
        var key = typeof(TIntent);
        if (_sagas.ContainsKey(key))
            throw new InvalidOperationException($"Saga for {key.Name} already registered.");
        _sagas[key] = (intent, sender) => saga.Run((TIntent)intent, sender);
    }

    public IEnumerable<Effect> Run(Intent intent, Sender sender)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!_sagas.TryGetValue(intent.GetType(), out var fn))
            throw new InvalidOperationException($"No saga registered for {intent.GetType().Name}.");
        return fn(intent, sender);
    }
}
