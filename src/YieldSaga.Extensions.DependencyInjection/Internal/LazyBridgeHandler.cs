namespace YieldSaga.Extensions.DependencyInjection.Internal;

internal sealed class LazyBridgeHandler<TFromEvent, TToInput, TFromState, TToState>
    : IEffectHandler<TFromEvent, TFromState>
    where TFromEvent : Event
{
    private readonly IBridge<TFromEvent, TToInput> _bridge;
    private readonly Lazy<Runtime<TToState>> _toRuntime;
    private readonly Lazy<Sender> _sender;
    private readonly Lazy<EffectDispatcher<TToState>?> _toDispatcher;

    public LazyBridgeHandler(
        IBridge<TFromEvent, TToInput> bridge,
        Func<Runtime<TToState>> toRuntimeFactory,
        Func<Sender> senderFactory,
        Func<EffectDispatcher<TToState>?>? toDispatcherFactory)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(toRuntimeFactory);
        ArgumentNullException.ThrowIfNull(senderFactory);
        _bridge = bridge;
        _toRuntime = new Lazy<Runtime<TToState>>(toRuntimeFactory);
        _sender = new Lazy<Sender>(senderFactory);
        _toDispatcher = toDispatcherFactory is null
            ? new Lazy<EffectDispatcher<TToState>?>(() => null)
            : new Lazy<EffectDispatcher<TToState>?>(toDispatcherFactory);
    }

    public async Task OnEffectAsync(TFromEvent effect, TFromState state, EffectBatch<TFromState> batch)
    {
        var to = _toRuntime.Value;
        var sender = _sender.Value;
        var dispatcher = _toDispatcher.Value;
        foreach (var input in _bridge.Bridge(effect))
        {
            var trace = to.Update(input, sender);
            if (dispatcher is not null && trace.Count > 0)
                await dispatcher.DispatchAsync(new EffectBatch<TToState>(trace));
        }
    }
}
