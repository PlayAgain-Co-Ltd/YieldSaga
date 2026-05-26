namespace YieldSaga.Extensions.DependencyInjection.Tests;

public sealed class WithInitialStateTests
{
    [Fact]
    public void Direct_overload_sets_initial_state()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(new CounterState(42))
            .AddSaga<IncrementSaga>()
            .AddEventApplier<IncrementApplier>());
        using var sp = services.BuildServiceProvider();

        Assert.Equal(42, sp.GetRequiredService<Runtime<CounterState>>().Latest.Value);
    }

    [Fact]
    public void Factory_overload_can_pull_dependencies_from_sp()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new InitialValueProvider { Seed = 100 });
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(sp => new CounterState(sp.GetRequiredService<InitialValueProvider>().Seed))
            .AddSaga<IncrementSaga>()
            .AddEventApplier<IncrementApplier>());
        using var sp = services.BuildServiceProvider();

        Assert.Equal(100, sp.GetRequiredService<Runtime<CounterState>>().Latest.Value);
    }

    [Fact]
    public void Missing_WithInitialState_throws_at_configure_time()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddYieldSagaRuntime<CounterState>(b => b
                .AddSaga<IncrementSaga>()
                .AddEventApplier<IncrementApplier>()));

        Assert.Contains("WithInitialState", ex.Message);
    }

    private sealed class InitialValueProvider { public int Seed { get; init; } }
}
