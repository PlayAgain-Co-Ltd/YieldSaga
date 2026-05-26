namespace YieldSaga.Extensions.DependencyInjection.Tests;

public sealed class MultiInterfaceTests
{
    [Fact]
    public void AddSaga_inferred_registers_all_ISaga_implementations()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<MultiSaga>()
            .AddEventApplier<MultiApplier>());
        using var sp = services.BuildServiceProvider();

        var runtime = sp.GetRequiredService<Runtime<CounterState>>();
        runtime.Dispatch(new MultiIntent1(5), new TestSender());
        runtime.Dispatch(new MultiIntent2(2), new TestSender());

        Assert.Equal(3, runtime.Latest.Value);
    }

    [Fact]
    public void AddEventApplier_inferred_registers_all_event_closures_for_state()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<MultiSaga>()
            .AddEventApplier<MultiApplier>());
        using var sp = services.BuildServiceProvider();

        var runtime = sp.GetRequiredService<Runtime<CounterState>>();
        runtime.Dispatch(new MultiIntent1(4), new TestSender());
        runtime.Dispatch(new MultiIntent2(1), new TestSender());

        Assert.Equal(3, runtime.Latest.Value);
    }

    [Fact]
    public void Same_impl_added_twice_is_deduplicated()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<IncrementSaga>()
            .AddSaga<IncrementSaga>()                  // dup
            .AddEventApplier<IncrementApplier>()
            .AddEventApplier<IncrementApplier>());     // dup
        using var sp = services.BuildServiceProvider();

        // Without dedup, SagaRegistry.Register would throw "already registered".
        var runtime = sp.GetRequiredService<Runtime<CounterState>>();
        runtime.Dispatch(new IncrementIntent(6), new TestSender());

        Assert.Equal(6, runtime.Latest.Value);
    }

    [Fact]
    public void AddSaga_inferred_throws_when_type_does_not_implement_ISaga()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddYieldSagaRuntime<CounterState>(b => b
                .WithInitialState(CounterState.Zero)
                .AddSaga<NotASaga>()));

        Assert.Contains("NotASaga", ex.Message);
        Assert.Contains("ISaga<>", ex.Message);
    }

    [Fact]
    public void AddEventApplier_inferred_throws_when_TState_mismatch()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddYieldSagaRuntime<CounterState>(b => b
                .WithInitialState(CounterState.Zero)
                .AddEventApplier<WrongStateApplier>()));

        Assert.Contains("WrongStateApplier", ex.Message);
        Assert.Contains("CounterState", ex.Message);
        Assert.Contains("OtherState", ex.Message);
    }
}
