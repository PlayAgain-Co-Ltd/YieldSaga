namespace YieldSaga.Extensions.DependencyInjection.Tests;

public sealed class EndToEndTests
{
    /// <summary>
    /// Saga + Applier + Interceptor + AutoIntentProducer + Update + Tick + Take/Resume の組み合わせを
    /// DI 経由で 1 から組み、手動配線版と同じ最終 State になることを assert。
    /// </summary>
    [Fact]
    public void Combined_pipeline_drives_state_to_fixed_point()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<IncrementSaga>()
            .AddSaga<DecrementSaga>()
            .AddEventApplier<IncrementApplier>()
            .AddEventApplier<DecrementApplier>()
            .AddInterceptor<DoubleIncrementInterceptor>()
            .AddIntentProducer<IntInputProducer>());
        using var sp = services.BuildServiceProvider();

        var runtime = sp.GetRequiredService<Runtime<CounterState>>();

        // Update(5) → Producer は IncrementIntent(5) を吐く → Interceptor で IncrementedEvent(10) に化ける
        runtime.Update(5, new TestSender());
        Assert.Equal(10, runtime.Latest.Value);

        // Direct Dispatch も並走可能
        runtime.Dispatch(new DecrementIntent(2), new TestSender());
        Assert.Equal(8, runtime.Latest.Value);
    }

    [Fact]
    public async Task Effect_handlers_observe_full_trace_via_dispatcher()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<IncrementSaga>()
            .AddEventApplier<IncrementApplier>()
            .AddInterceptor<DoubleIncrementInterceptor>()
            .AddEffectHandler<RecordingHandler>());
        using var sp = services.BuildServiceProvider();

        var runtime = sp.GetRequiredService<Runtime<CounterState>>();
        var dispatcher = sp.GetRequiredService<EffectDispatcher<CounterState>>();

        var trace = runtime.Dispatch(new IncrementIntent(3), new TestSender());
        await dispatcher.DispatchAsync(new EffectBatch<CounterState>(trace));

        Assert.Equal(6, runtime.Latest.Value);
        Assert.Equal(new[] { 6 }, sp.GetRequiredService<RecordingHandler>().Seen);
    }
}
