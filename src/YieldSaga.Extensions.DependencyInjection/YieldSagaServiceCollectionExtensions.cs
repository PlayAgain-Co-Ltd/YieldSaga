using Microsoft.Extensions.DependencyInjection;

namespace YieldSaga.Extensions.DependencyInjection;

public static class YieldSagaServiceCollectionExtensions
{
    /// <summary>
    /// Wires a <see cref="Runtime{TState}"/> (and its companion <see cref="EffectHandlerRegistry{TState}"/> /
    /// <see cref="EffectDispatcher{TState}"/>) into the service collection. The configure delegate runs
    /// immediately to collect registrations; actual instances are constructed on first resolution.
    ///
    /// Calling this multiple times with different <typeparamref name="TState"/> registers independent
    /// per-state singletons in the same container (multi-runtime composition with <see cref="IBridge{TFromEvent, TToInput}"/>).
    /// </summary>
    public static IServiceCollection AddYieldSagaRuntime<TState>(
        this IServiceCollection services,
        Action<IYieldSagaRuntimeBuilder<TState>> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new YieldSagaRuntimeBuilder<TState>(services);
        configure(builder);
        builder.Build();
        return services;
    }
}
