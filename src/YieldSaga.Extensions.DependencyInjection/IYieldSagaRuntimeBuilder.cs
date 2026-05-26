using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace YieldSaga.Extensions.DependencyInjection;

/// <summary>
/// Fluent builder for wiring a single <see cref="Runtime{TState}"/> through Microsoft.Extensions.DependencyInjection.
/// Created by <see cref="YieldSagaServiceCollectionExtensions.AddYieldSagaRuntime{TState}(IServiceCollection, Action{IYieldSagaRuntimeBuilder{TState}})"/>.
///
/// All registered participants (Saga / Applier / Interceptor / Decorator / Producer / Handler) are stored
/// as <see cref="ServiceLifetime.Singleton"/> in the DI container and resolved exactly once when the
/// <see cref="Runtime{TState}"/> / <see cref="EffectHandlerRegistry{TState}"/> singletons are first built.
///
/// All single-type-parameter add methods (e.g. <c>AddSaga&lt;TSaga&gt;()</c>) register **every** generic
/// interface instantiation the type implements that matches the participant role (and, for participants
/// that take <typeparamref name="TState"/>, every instantiation closed over <typeparamref name="TState"/>).
/// Duplicate registrations across <c>Add*</c> and <c>Scan*</c> calls are deduplicated automatically.
/// </summary>
public interface IYieldSagaRuntimeBuilder<TState>
{
    IServiceCollection Services { get; }

    IYieldSagaRuntimeBuilder<TState> WithInitialState(TState initial);
    IYieldSagaRuntimeBuilder<TState> WithInitialState(Func<IServiceProvider, TState> factory);

    /// <summary>Registers every <c>ISaga&lt;TIntent&gt;</c> implementation on <typeparamref name="TSaga"/>.</summary>
    IYieldSagaRuntimeBuilder<TState> AddSaga<TSaga>() where TSaga : class;
    /// <summary>Registers only the specific <c>ISaga&lt;TIntent&gt;</c> closure on <typeparamref name="TSaga"/>.</summary>
    IYieldSagaRuntimeBuilder<TState> AddSaga<TSaga, TIntent>()
        where TSaga : class, ISaga<TIntent>
        where TIntent : Intent;

    /// <summary>Registers every <c>IEventApplier&lt;TState, TEvent&gt;</c> implementation on <typeparamref name="TApplier"/>.</summary>
    IYieldSagaRuntimeBuilder<TState> AddEventApplier<TApplier>() where TApplier : class;
    IYieldSagaRuntimeBuilder<TState> AddEventApplier<TApplier, TEvent>()
        where TApplier : class, IEventApplier<TState, TEvent>
        where TEvent : Event;

    IYieldSagaRuntimeBuilder<TState> AddInterceptor<TInterceptor>() where TInterceptor : class;
    IYieldSagaRuntimeBuilder<TState> AddInterceptor<TInterceptor, TEvent>()
        where TInterceptor : class, IInterceptor<TState, TEvent>
        where TEvent : Event;

    IYieldSagaRuntimeBuilder<TState> AddSagaDecorator<TDecorator>() where TDecorator : class;
    IYieldSagaRuntimeBuilder<TState> AddSagaDecorator<TDecorator, TIntent>()
        where TDecorator : class, ISagaDecorator<TIntent>
        where TIntent : Intent;

    IYieldSagaRuntimeBuilder<TState> AddIntentProducer<TProducer>() where TProducer : class;
    IYieldSagaRuntimeBuilder<TState> AddIntentProducer<TProducer, TInput>()
        where TProducer : class, IIntentProducer<TInput, TState>;

    IYieldSagaRuntimeBuilder<TState> AddAutoIntentProducer<TProducer>()
        where TProducer : class, IAutoIntentProducer<TState>;

    IYieldSagaRuntimeBuilder<TState> AddEffectHandler<THandler>() where THandler : class;
    IYieldSagaRuntimeBuilder<TState> AddEffectHandler<THandler, TEffect>()
        where THandler : class, IEffectHandler<TEffect, TState>
        where TEffect : Effect;

    /// <summary>
    /// Wires a one-way bridge: when <typeparamref name="TFromEvent"/> fires through this runtime's
    /// <see cref="EffectDispatcher{TState}"/>, <paramref name="bridge"/> converts it to inputs and
    /// the destination <see cref="Runtime{TToState}"/> is driven via <c>Update</c>.
    /// The destination runtime is resolved lazily on first dispatch, so cyclic A↔B bridges are safe.
    /// </summary>
    IYieldSagaRuntimeBuilder<TState> AddBridge<TFromEvent, TToInput, TToState>(
        IBridge<TFromEvent, TToInput> bridge,
        Func<IServiceProvider, Sender> senderFactory,
        Func<IServiceProvider, EffectDispatcher<TToState>?>? toDispatcherFactory = null)
        where TFromEvent : Event;

    /// <summary>
    /// Scans the assembly containing <typeparamref name="TAnchor"/> for every concrete type that
    /// implements any YieldSaga participant interface and registers all matching generic closures.
    /// Types whose state arg does not match <typeparamref name="TState"/> are silently skipped.
    /// </summary>
    IYieldSagaRuntimeBuilder<TState> ScanFromAssemblyOf<TAnchor>();
    IYieldSagaRuntimeBuilder<TState> ScanFromAssembly(Assembly assembly);
}
