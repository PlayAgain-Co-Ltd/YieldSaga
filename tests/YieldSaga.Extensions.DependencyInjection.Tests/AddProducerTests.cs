namespace YieldSaga.Extensions.DependencyInjection.Tests;

public sealed class AddProducerTests
{
    [Fact]
    public void AddIntentProducer_inferred_drives_Update()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<IncrementSaga>()
            .AddEventApplier<IncrementApplier>()
            .AddIntentProducer<IntInputProducer>());
        using var sp = services.BuildServiceProvider();

        var runtime = sp.GetRequiredService<Runtime<CounterState>>();
        runtime.Update(3, new TestSender());

        Assert.Equal(3, runtime.Latest.Value);
    }

    [Fact]
    public void AddAutoIntentProducer_drives_Tick_fixed_point()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<IncrementSaga>()
            .AddEventApplier<IncrementApplier>()
            .AddAutoIntentProducer<StopAtTenAutoProducer>());
        using var sp = services.BuildServiceProvider();

        var runtime = sp.GetRequiredService<Runtime<CounterState>>();
        runtime.Tick(new TestSender());

        Assert.Equal(10, runtime.Latest.Value);
    }
}
