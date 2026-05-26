using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using YieldSaga.Extensions.DependencyInjection.Internal;

namespace YieldSaga.Extensions.DependencyInjection;

internal sealed class YieldSagaRuntimeBuilder<TState> : IYieldSagaRuntimeBuilder<TState>
{
    public IServiceCollection Services { get; }

    internal Func<IServiceProvider, TState>? InitialStateFactory;

    internal readonly List<Action<IServiceProvider, SagaRegistry>> SagaActions = new();
    internal readonly List<Action<IServiceProvider, EventApplierRegistry<TState>>> ApplierActions = new();
    internal readonly List<Action<IServiceProvider, InterceptorRegistry<TState>>> InterceptorActions = new();
    internal readonly List<Action<IServiceProvider, SagaDecoratorRegistry>> DecoratorActions = new();
    internal readonly List<Action<IServiceProvider, IntentProducerRegistry<TState>>> ProducerActions = new();
    internal readonly List<Action<IServiceProvider, AutoIntentProducerRegistry<TState>>> AutoProducerActions = new();
    internal readonly List<Action<IServiceProvider, EffectHandlerRegistry<TState>>> HandlerActions = new();

    // Dedup key: (implType, closed-generic-iface-or-marker). Bridges bypass this set.
    private readonly HashSet<(Type ImplType, Type Key)> _seen = new();

    public YieldSagaRuntimeBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    public IYieldSagaRuntimeBuilder<TState> WithInitialState(TState initial)
    {
        InitialStateFactory = _ => initial;
        return this;
    }

    public IYieldSagaRuntimeBuilder<TState> WithInitialState(Func<IServiceProvider, TState> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        InitialStateFactory = factory;
        return this;
    }

    // ─── Saga ─────────────────────────────────────────────────────

    public IYieldSagaRuntimeBuilder<TState> AddSaga<TSaga>() where TSaga : class
        => AddByOpenIface<TSaga>(typeof(ISaga<>), args => AddSagaCore(typeof(TSaga), args[0]),
            stateArgIndex: -1);

    public IYieldSagaRuntimeBuilder<TState> AddSaga<TSaga, TIntent>()
        where TSaga : class, ISaga<TIntent>
        where TIntent : Intent
    {
        AddSagaCore(typeof(TSaga), typeof(TIntent));
        return this;
    }

    private void AddSagaCore(Type implType, Type intentType)
    {
        var closed = typeof(ISaga<>).MakeGenericType(intentType);
        if (!_seen.Add((implType, closed))) return;
        Services.TryAddSingleton(implType);
        var register = typeof(SagaRegistry)
            .GetMethod(nameof(SagaRegistry.Register), BindingFlags.Public | BindingFlags.Instance)!
            .MakeGenericMethod(intentType);
        SagaActions.Add((sp, reg) =>
        {
            var instance = sp.GetRequiredService(implType);
            register.Invoke(reg, new[] { instance });
        });
    }

    // ─── EventApplier ─────────────────────────────────────────────

    public IYieldSagaRuntimeBuilder<TState> AddEventApplier<TApplier>() where TApplier : class
        => AddByOpenIface<TApplier>(typeof(IEventApplier<,>),
            args => AddEventApplierCore(typeof(TApplier), args[1]),
            stateArgIndex: 0);

    public IYieldSagaRuntimeBuilder<TState> AddEventApplier<TApplier, TEvent>()
        where TApplier : class, IEventApplier<TState, TEvent>
        where TEvent : Event
    {
        AddEventApplierCore(typeof(TApplier), typeof(TEvent));
        return this;
    }

    private void AddEventApplierCore(Type implType, Type eventType)
    {
        var closed = typeof(IEventApplier<,>).MakeGenericType(typeof(TState), eventType);
        if (!_seen.Add((implType, closed))) return;
        Services.TryAddSingleton(implType);
        var register = typeof(EventApplierRegistry<TState>)
            .GetMethod(nameof(EventApplierRegistry<TState>.Register), BindingFlags.Public | BindingFlags.Instance)!
            .MakeGenericMethod(eventType);
        ApplierActions.Add((sp, reg) =>
        {
            var instance = sp.GetRequiredService(implType);
            register.Invoke(reg, new[] { instance });
        });
    }

    // ─── Interceptor ──────────────────────────────────────────────

    public IYieldSagaRuntimeBuilder<TState> AddInterceptor<TInterceptor>() where TInterceptor : class
        => AddByOpenIface<TInterceptor>(typeof(IInterceptor<,>),
            args => AddInterceptorCore(typeof(TInterceptor), args[1]),
            stateArgIndex: 0);

    public IYieldSagaRuntimeBuilder<TState> AddInterceptor<TInterceptor, TEvent>()
        where TInterceptor : class, IInterceptor<TState, TEvent>
        where TEvent : Event
    {
        AddInterceptorCore(typeof(TInterceptor), typeof(TEvent));
        return this;
    }

    private void AddInterceptorCore(Type implType, Type eventType)
    {
        var closed = typeof(IInterceptor<,>).MakeGenericType(typeof(TState), eventType);
        if (!_seen.Add((implType, closed))) return;
        Services.TryAddSingleton(implType);
        var register = typeof(InterceptorRegistry<TState>)
            .GetMethod(nameof(InterceptorRegistry<TState>.Register), BindingFlags.Public | BindingFlags.Instance)!
            .MakeGenericMethod(eventType);
        InterceptorActions.Add((sp, reg) =>
        {
            var instance = sp.GetRequiredService(implType);
            register.Invoke(reg, new[] { instance });
        });
    }

    // ─── SagaDecorator ────────────────────────────────────────────

    public IYieldSagaRuntimeBuilder<TState> AddSagaDecorator<TDecorator>() where TDecorator : class
        => AddByOpenIface<TDecorator>(typeof(ISagaDecorator<>),
            args => AddSagaDecoratorCore(typeof(TDecorator), args[0]),
            stateArgIndex: -1);

    public IYieldSagaRuntimeBuilder<TState> AddSagaDecorator<TDecorator, TIntent>()
        where TDecorator : class, ISagaDecorator<TIntent>
        where TIntent : Intent
    {
        AddSagaDecoratorCore(typeof(TDecorator), typeof(TIntent));
        return this;
    }

    private void AddSagaDecoratorCore(Type implType, Type intentType)
    {
        var closed = typeof(ISagaDecorator<>).MakeGenericType(intentType);
        if (!_seen.Add((implType, closed))) return;
        Services.TryAddSingleton(implType);
        var register = typeof(SagaDecoratorRegistry)
            .GetMethod(nameof(SagaDecoratorRegistry.Register), BindingFlags.Public | BindingFlags.Instance)!
            .MakeGenericMethod(intentType);
        DecoratorActions.Add((sp, reg) =>
        {
            var instance = sp.GetRequiredService(implType);
            register.Invoke(reg, new[] { instance });
        });
    }

    // ─── IntentProducer ───────────────────────────────────────────

    public IYieldSagaRuntimeBuilder<TState> AddIntentProducer<TProducer>() where TProducer : class
        => AddByOpenIface<TProducer>(typeof(IIntentProducer<,>),
            args => AddIntentProducerCore(typeof(TProducer), args[0]),
            stateArgIndex: 1);

    public IYieldSagaRuntimeBuilder<TState> AddIntentProducer<TProducer, TInput>()
        where TProducer : class, IIntentProducer<TInput, TState>
    {
        AddIntentProducerCore(typeof(TProducer), typeof(TInput));
        return this;
    }

    private void AddIntentProducerCore(Type implType, Type inputType)
    {
        var closed = typeof(IIntentProducer<,>).MakeGenericType(inputType, typeof(TState));
        if (!_seen.Add((implType, closed))) return;
        Services.TryAddSingleton(implType);
        var register = typeof(IntentProducerRegistry<TState>)
            .GetMethod(nameof(IntentProducerRegistry<TState>.Register), BindingFlags.Public | BindingFlags.Instance)!
            .MakeGenericMethod(inputType);
        ProducerActions.Add((sp, reg) =>
        {
            var instance = sp.GetRequiredService(implType);
            register.Invoke(reg, new[] { instance });
        });
    }

    // ─── AutoIntentProducer ───────────────────────────────────────

    public IYieldSagaRuntimeBuilder<TState> AddAutoIntentProducer<TProducer>()
        where TProducer : class, IAutoIntentProducer<TState>
    {
        AddAutoIntentProducerCore(typeof(TProducer));
        return this;
    }

    private void AddAutoIntentProducerCore(Type implType)
    {
        var key = typeof(IAutoIntentProducer<>).MakeGenericType(typeof(TState));
        if (!_seen.Add((implType, key))) return;
        Services.TryAddSingleton(implType);
        AutoProducerActions.Add((sp, reg) =>
            reg.Register((IAutoIntentProducer<TState>)sp.GetRequiredService(implType)));
    }

    // ─── EffectHandler ────────────────────────────────────────────

    public IYieldSagaRuntimeBuilder<TState> AddEffectHandler<THandler>() where THandler : class
        => AddByOpenIface<THandler>(typeof(IEffectHandler<,>),
            args => AddEffectHandlerCore(typeof(THandler), args[0]),
            stateArgIndex: 1);

    public IYieldSagaRuntimeBuilder<TState> AddEffectHandler<THandler, TEffect>()
        where THandler : class, IEffectHandler<TEffect, TState>
        where TEffect : Effect
    {
        AddEffectHandlerCore(typeof(THandler), typeof(TEffect));
        return this;
    }

    private void AddEffectHandlerCore(Type implType, Type effectType)
    {
        var closed = typeof(IEffectHandler<,>).MakeGenericType(effectType, typeof(TState));
        if (!_seen.Add((implType, closed))) return;
        Services.TryAddSingleton(implType);
        var register = typeof(EffectHandlerRegistry<TState>)
            .GetMethod(nameof(EffectHandlerRegistry<TState>.Register), BindingFlags.Public | BindingFlags.Instance)!
            .MakeGenericMethod(effectType);
        HandlerActions.Add((sp, reg) =>
        {
            var instance = sp.GetRequiredService(implType);
            register.Invoke(reg, new[] { instance });
        });
    }

    // ─── Bridge ───────────────────────────────────────────────────

    public IYieldSagaRuntimeBuilder<TState> AddBridge<TFromEvent, TToInput, TToState>(
        IBridge<TFromEvent, TToInput> bridge,
        Func<IServiceProvider, Sender> senderFactory,
        Func<IServiceProvider, EffectDispatcher<TToState>?>? toDispatcherFactory = null)
        where TFromEvent : Event
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(senderFactory);
        var register = typeof(EffectHandlerRegistry<TState>)
            .GetMethod(nameof(EffectHandlerRegistry<TState>.Register), BindingFlags.Public | BindingFlags.Instance)!
            .MakeGenericMethod(typeof(TFromEvent));
        HandlerActions.Add((sp, reg) =>
        {
            var handler = new LazyBridgeHandler<TFromEvent, TToInput, TState, TToState>(
                bridge,
                () => sp.GetRequiredService<Runtime<TToState>>(),
                () => senderFactory(sp),
                toDispatcherFactory is null ? null : () => toDispatcherFactory(sp));
            register.Invoke(reg, new object[] { handler });
        });
        return this;
    }

    // ─── Scan ─────────────────────────────────────────────────────

    public IYieldSagaRuntimeBuilder<TState> ScanFromAssemblyOf<TAnchor>()
        => ScanFromAssembly(typeof(TAnchor).Assembly);

    public IYieldSagaRuntimeBuilder<TState> ScanFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }
        foreach (var type in types)
        {
            if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition) continue;
            ScanType(type);
        }
        return this;
    }

    private void ScanType(Type type)
    {
        foreach (var args in GenericInterfaceResolver.ResolveAll(type, typeof(ISaga<>)))
            AddSagaCore(type, args[0]);
        foreach (var args in GenericInterfaceResolver.ResolveAll(type, typeof(IEventApplier<,>)))
            if (args[0] == typeof(TState)) AddEventApplierCore(type, args[1]);
        foreach (var args in GenericInterfaceResolver.ResolveAll(type, typeof(IInterceptor<,>)))
            if (args[0] == typeof(TState)) AddInterceptorCore(type, args[1]);
        foreach (var args in GenericInterfaceResolver.ResolveAll(type, typeof(ISagaDecorator<>)))
            AddSagaDecoratorCore(type, args[0]);
        foreach (var args in GenericInterfaceResolver.ResolveAll(type, typeof(IIntentProducer<,>)))
            if (args[1] == typeof(TState)) AddIntentProducerCore(type, args[0]);
        foreach (var args in GenericInterfaceResolver.ResolveAll(type, typeof(IEffectHandler<,>)))
            if (args[1] == typeof(TState)) AddEffectHandlerCore(type, args[0]);
        if (typeof(IAutoIntentProducer<TState>).IsAssignableFrom(type))
            AddAutoIntentProducerCore(type);
    }

    // ─── helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Drives the inferred <c>Add*&lt;T&gt;()</c> overloads: resolves every generic-arg set <typeparamref name="T"/>
    /// closes <paramref name="openGeneric"/> over, optionally filters by <typeparamref name="TState"/> position,
    /// and invokes <paramref name="register"/> once per match. Throws if zero matches survive (either no
    /// implementation at all, or none for the builder's state).
    /// </summary>
    private IYieldSagaRuntimeBuilder<TState> AddByOpenIface<T>(
        Type openGeneric,
        Action<Type[]> register,
        int stateArgIndex)
    {
        var all = GenericInterfaceResolver.ResolveAll(typeof(T), openGeneric);
        if (all.Count == 0)
            throw new InvalidOperationException(
                $"Type {typeof(T).Name} does not implement {FormatOpen(openGeneric)}.");

        IReadOnlyList<Type[]> filtered = stateArgIndex < 0
            ? all
            : all.Where(a => a[stateArgIndex] == typeof(TState)).ToArray();

        if (filtered.Count == 0)
        {
            var foundStates = string.Join(", ", all.Select(a => a[stateArgIndex].Name));
            throw new InvalidOperationException(
                $"Type {typeof(T).Name} implements {FormatOpen(openGeneric)} but not for state " +
                $"{typeof(TState).Name}. Implementations found for state(s): {foundStates}.");
        }

        foreach (var args in filtered)
            register(args);
        return this;
    }

    private static string FormatOpen(Type openGeneric)
    {
        var name = openGeneric.Name;
        var tick = name.IndexOf('`');
        if (tick >= 0) name = name[..tick];
        var arity = openGeneric.GetGenericArguments().Length;
        return $"{name}<{new string(',', arity - 1)}>";
    }

    internal void Build()
    {
        if (InitialStateFactory is null)
            throw new InvalidOperationException(
                $"AddYieldSagaRuntime<{typeof(TState).Name}>(...) must call WithInitialState(...) inside the configure delegate.");

        Services.TryAddSingleton<EffectHandlerRegistry<TState>>(sp =>
        {
            var reg = new EffectHandlerRegistry<TState>();
            foreach (var a in HandlerActions) a(sp, reg);
            return reg;
        });

        Services.TryAddSingleton<EffectDispatcher<TState>>(sp =>
            new EffectDispatcher<TState>(sp.GetRequiredService<EffectHandlerRegistry<TState>>()));

        Services.TryAddSingleton<Runtime<TState>>(sp =>
        {
            var sagas = new SagaRegistry();
            foreach (var a in SagaActions) a(sp, sagas);

            var appliers = new EventApplierRegistry<TState>();
            foreach (var a in ApplierActions) a(sp, appliers);

            InterceptorRegistry<TState>? interceptors = null;
            if (InterceptorActions.Count > 0)
            {
                interceptors = new InterceptorRegistry<TState>();
                foreach (var a in InterceptorActions) a(sp, interceptors);
            }

            SagaDecoratorRegistry? decorators = null;
            if (DecoratorActions.Count > 0)
            {
                decorators = new SagaDecoratorRegistry();
                foreach (var a in DecoratorActions) a(sp, decorators);
            }

            IntentProducerRegistry<TState>? producers = null;
            if (ProducerActions.Count > 0)
            {
                producers = new IntentProducerRegistry<TState>();
                foreach (var a in ProducerActions) a(sp, producers);
            }

            AutoIntentProducerRegistry<TState>? autoProducers = null;
            if (AutoProducerActions.Count > 0)
            {
                autoProducers = new AutoIntentProducerRegistry<TState>();
                foreach (var a in AutoProducerActions) a(sp, autoProducers);
            }

            var initial = InitialStateFactory(sp);
            return new Runtime<TState>(initial, sagas, appliers, interceptors, decorators, producers, autoProducers);
        });
    }
}
