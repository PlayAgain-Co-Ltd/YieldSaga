namespace YieldSaga.Tests.Signals;

public class TakeTests
{
    private sealed record IntPrompt(int Min) : Prompt
    {
        public override bool Accepts(object input) => input is int n && n >= Min;
    }

    private sealed record SingleChoicePrompt(int Only) : Prompt
    {
        public override bool Accepts(object input) => input is int n && n == Only;
        public override object? GetAutoResolution() => Only;
    }

    [Fact]
    public void Newly_created_Take_is_unresolved()
    {
        var take = new Take(new IntPrompt(0));
        Assert.False(take.IsResolved);
        Assert.Null(take.ResolvedInput);
    }

    [Fact]
    public void TryResolve_succeeds_when_prompt_accepts()
    {
        var take = new Take(new IntPrompt(5));
        Assert.True(take.TryResolve(10));
        Assert.True(take.IsResolved);
        Assert.Equal(10, take.ResolvedInput);
    }

    [Fact]
    public void TryResolve_fails_when_prompt_rejects()
    {
        var take = new Take(new IntPrompt(5));
        Assert.False(take.TryResolve(1));
        Assert.False(take.IsResolved);
    }

    [Fact]
    public void Second_TryResolve_is_rejected_even_if_accepted()
    {
        var take = new Take(new IntPrompt(0));
        Assert.True(take.TryResolve(1));
        Assert.False(take.TryResolve(2));
        Assert.Equal(1, take.ResolvedInput);
    }

    [Fact]
    public void Prompt_can_offer_auto_resolution()
    {
        var prompt = new SingleChoicePrompt(42);
        Assert.Equal(42, prompt.GetAutoResolution());
    }

    [Fact]
    public void Default_Prompt_has_no_auto_resolution()
    {
        var prompt = new IntPrompt(0);
        Assert.Null(prompt.GetAutoResolution());
    }
}
