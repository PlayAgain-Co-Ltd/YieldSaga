namespace YieldSaga.Extensions.DependencyInjection.Tests;

public sealed class AddEffectHandlerTests
{
    [Fact]
    public async Task EffectHandler_fires_via_EffectDispatcher()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<IncrementSaga>()
            .AddEventApplier<IncrementApplier>()
            .AddEffectHandler<RecordingHandler>());
        using var sp = services.BuildServiceProvider();

        var runtime = sp.GetRequiredService<Runtime<CounterState>>();
        var dispatcher = sp.GetRequiredService<EffectDispatcher<CounterState>>();
        var trace = runtime.Dispatch(new IncrementIntent(7), new TestSender());

        await dispatcher.DispatchAsync(new EffectBatch<CounterState>(trace));

        var handler = sp.GetRequiredService<RecordingHandler>();
        Assert.Single(handler.Seen);
        Assert.Equal(7, handler.Seen[0]);
    }
}
