namespace YieldSaga.Extensions.DependencyInjection.Tests;

public sealed class ScanTests
{
    [Fact]
    public void ScanFromAssemblyOf_picks_up_sagas_and_appliers()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .ScanFromAssemblyOf<IncrementSaga>());
        using var sp = services.BuildServiceProvider();

        var runtime = sp.GetRequiredService<Runtime<CounterState>>();
        // MultiIntent1 → MultiSaga (multi-interface) → MultiEvent1 → MultiApplier (multi-interface).
        // The scan must have wired all four for this dispatch to land on state.Value = 5.
        runtime.Dispatch(new MultiIntent1(5), new TestSender());

        Assert.Equal(5, runtime.Latest.Value);
    }

    [Fact]
    public void ScanFromAssemblyOf_picks_up_auto_intent_producer()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .ScanFromAssemblyOf<IncrementSaga>());
        using var sp = services.BuildServiceProvider();

        var runtime = sp.GetRequiredService<Runtime<CounterState>>();
        runtime.Tick(new TestSender());

        // StopAtTenAutoProducer feeds IncrementIntent(1), which DoubleIncrementInterceptor doubles.
        // Fixed-point reached once value >= 10 — exact value depends on interception arithmetic.
        Assert.True(runtime.Latest.Value >= 10);
    }

    [Fact]
    public void Scan_skips_participants_whose_state_does_not_match()
    {
        // Build for OtherState. CounterState-bound participants must be silently skipped.
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<OtherState>(b => b
            .WithInitialState(OtherState.Initial)
            .ScanFromAssemblyOf<IncrementSaga>());
        using var sp = services.BuildServiceProvider();

        var runtime = sp.GetRequiredService<Runtime<OtherState>>();
        runtime.Dispatch(new TaggedIntent("hello"), new TestSender());

        // TaggedSaga + TaggedApplier picked up; CounterState-only types skipped without error.
        Assert.Equal("hello", runtime.Latest.Tag);
    }

    [Fact]
    public void Explicit_Add_and_Scan_together_do_not_double_register()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<MultiSaga>()                      // explicit
            .AddEventApplier<MultiApplier>()           // explicit
            .ScanFromAssemblyOf<IncrementSaga>());     // would otherwise re-register the same types
        using var sp = services.BuildServiceProvider();

        var runtime = sp.GetRequiredService<Runtime<CounterState>>();
        runtime.Dispatch(new MultiIntent1(4), new TestSender());
        runtime.Dispatch(new MultiIntent2(1), new TestSender());

        Assert.Equal(3, runtime.Latest.Value);
    }

    [Fact]
    public void Scan_on_empty_assembly_is_a_no_op()
    {
        // The Abstractions assembly contains zero YieldSaga participants.
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<IncrementSaga>()
            .AddEventApplier<IncrementApplier>()
            .ScanFromAssembly(typeof(IServiceCollection).Assembly));
        using var sp = services.BuildServiceProvider();

        var runtime = sp.GetRequiredService<Runtime<CounterState>>();
        runtime.Dispatch(new IncrementIntent(3), new TestSender());

        Assert.Equal(3, runtime.Latest.Value);
    }
}
