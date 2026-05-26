namespace YieldSaga.Extensions.DependencyInjection.Tests;

public sealed class AddEventApplierTests
{
    [Fact]
    public void Inferred_AddEventApplier_applies_event_to_state()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<IncrementSaga>()
            .AddEventApplier<IncrementApplier>());
        using var sp = services.BuildServiceProvider();

        var runtime = sp.GetRequiredService<Runtime<CounterState>>();
        runtime.Dispatch(new IncrementIntent(8), new TestSender());

        Assert.Equal(8, runtime.Latest.Value);
    }

    [Fact]
    public void Explicit_AddEventApplier_registers_specific_event_only()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<MultiSaga>()
            .AddEventApplier<MultiApplier, MultiEvent1>()
            .AddEventApplier<MultiApplier, MultiEvent2>());
        using var sp = services.BuildServiceProvider();

        var runtime = sp.GetRequiredService<Runtime<CounterState>>();
        runtime.Dispatch(new MultiIntent1(4), new TestSender());
        runtime.Dispatch(new MultiIntent2(1), new TestSender());

        Assert.Equal(3, runtime.Latest.Value);
    }
}
