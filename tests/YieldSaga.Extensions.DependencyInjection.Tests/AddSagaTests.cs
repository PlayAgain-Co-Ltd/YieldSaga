namespace YieldSaga.Extensions.DependencyInjection.Tests;

public sealed class AddSagaTests
{
    [Fact]
    public void Inferred_AddSaga_resolves_Intent_type_and_drives_Runtime()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<IncrementSaga>()
            .AddEventApplier<IncrementApplier>());
        using var sp = services.BuildServiceProvider();

        var runtime = sp.GetRequiredService<Runtime<CounterState>>();
        runtime.Dispatch(new IncrementIntent(5), new TestSender());

        Assert.Equal(5, runtime.Latest.Value);
    }

    [Fact]
    public void Multiple_sagas_register_distinct_intents()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<IncrementSaga>()
            .AddSaga<DecrementSaga>()
            .AddEventApplier<IncrementApplier>()
            .AddEventApplier<DecrementApplier>());
        using var sp = services.BuildServiceProvider();

        var runtime = sp.GetRequiredService<Runtime<CounterState>>();
        runtime.Dispatch(new IncrementIntent(7), new TestSender());
        runtime.Dispatch(new DecrementIntent(3), new TestSender());

        Assert.Equal(4, runtime.Latest.Value);
    }

    [Fact]
    public void Explicit_AddSaga_registers_specific_impl_only()
    {
        // Register only one of MultiSaga's two ISaga<> closures. The other intent is left
        // unhandled so dispatching it would throw, demonstrating the cherry-pick semantics.
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<MultiSaga, MultiIntent1>()
            .AddEventApplier<MultiApplier>());
        using var sp = services.BuildServiceProvider();

        var runtime = sp.GetRequiredService<Runtime<CounterState>>();
        runtime.Dispatch(new MultiIntent1(7), new TestSender());
        Assert.Equal(7, runtime.Latest.Value);

        // MultiIntent2 was intentionally not registered.
        Assert.Throws<InvalidOperationException>(() =>
            runtime.Dispatch(new MultiIntent2(1), new TestSender()));
    }
}
