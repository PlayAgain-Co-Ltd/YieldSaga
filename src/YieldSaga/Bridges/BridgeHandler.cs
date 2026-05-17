namespace YieldSaga;

/// <summary>
/// Spec §10: <see cref="IBridge{TFromEffect, TToInput}"/> を出処側 Runtime の
/// EffectHandler として配線する adapter。
///
/// フロー:
/// 1. 出処側 Dispatcher が <typeparamref name="TFromEffect"/> を流す
/// 2. <see cref="OnEffectAsync"/> が呼ばれ、bridge.Bridge(effect) を実行
/// 3. 戻り値の各 input について宛先 Runtime.Update を **同期で** 呼ぶ
/// 4. 宛先 Dispatcher が指定されていれば、得られた trace を EffectBatch で流す
///    （これが next-hop の Bridge を起こし、カスケードが伸びる）
///
/// 宛先 Runtime が Take で中断していると Update が throw する。Take を扱うフェーズと
/// Bridge を扱うフェーズは分けるのが原則。
/// </summary>
public sealed class BridgeHandler<TFromEffect, TToInput, TFromState, TToState>
    : IEffectHandler<TFromEffect, TFromState>
    where TFromEffect : Effect
{
    private readonly IBridge<TFromEffect, TToInput> _bridge;
    private readonly Runtime<TToState> _toRuntime;
    private readonly EffectDispatcher<TToState>? _toDispatcher;
    private readonly Sender _sender;

    public BridgeHandler(
        IBridge<TFromEffect, TToInput> bridge,
        Runtime<TToState> toRuntime,
        Sender sender,
        EffectDispatcher<TToState>? toDispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(toRuntime);
        ArgumentNullException.ThrowIfNull(sender);
        _bridge = bridge;
        _toRuntime = toRuntime;
        _toDispatcher = toDispatcher;
        _sender = sender;
    }

    public async Task OnEffectAsync(TFromEffect effect, TFromState state, EffectBatch<TFromState> batch)
    {
        foreach (var input in _bridge.Bridge(effect))
        {
            var trace = _toRuntime.Update(input, _sender);
            if (_toDispatcher is not null && trace.Count > 0)
                await _toDispatcher.DispatchAsync(new EffectBatch<TToState>(trace));
        }
    }
}

/// <summary>
/// <see cref="EffectHandlerRegistry{TFromState}"/> に Bridge を一発で配線する拡張。
/// 4 つの generic 引数を全部 inference させる（registry → TFromState、bridge → TFromEffect/TToInput、
/// toRuntime → TToState）。
/// </summary>
public static class BridgeRegistryExtensions
{
    public static void RegisterBridge<TFromEffect, TToInput, TFromState, TToState>(
        this EffectHandlerRegistry<TFromState> registry,
        IBridge<TFromEffect, TToInput> bridge,
        Runtime<TToState> toRuntime,
        Sender sender,
        EffectDispatcher<TToState>? toDispatcher = null)
        where TFromEffect : Effect
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(new BridgeHandler<TFromEffect, TToInput, TFromState, TToState>(
            bridge, toRuntime, sender, toDispatcher));
    }
}
