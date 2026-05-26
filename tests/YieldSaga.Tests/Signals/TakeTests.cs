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

    [Fact]
    public void Typed_Take_resolves_and_exposes_Value_without_cast()
    {
        var take = new Take<int>(new IntPrompt(0));
        Assert.True(take.TryResolve(42));
        Assert.Equal(42, take.Value);
    }

    [Fact]
    public void Typed_Take_is_assignable_to_base_Take()
    {
        Take t = new Take<int>(new IntPrompt(0));
        Assert.True(t.TryResolve(7));
        Assert.True(t.IsResolved);
        Assert.Equal(7, t.ResolvedInput);
    }

    [Fact]
    public void ReadInput_returns_typed_Take_and_assigns_out_holder()
    {
        var produced = Read.Input<int>(out var holder, new IntPrompt(0));
        Assert.Same(produced, holder);
        Assert.False(holder.IsResolved);
        Assert.True(holder.TryResolve(13));
        Assert.Equal(13, holder.Value);
    }

    // typed Prompt<TInput>: pattern match 不要、Read.Input は型引数推論

    private sealed record TypedIntPrompt(int Min) : Prompt<int>
    {
        public override bool Accepts(int input) => input >= Min;
    }

    private sealed record TypedAutoZeroPrompt : Prompt<int>
    {
        public override bool Accepts(int input) => true;
        public override bool TryGetAutoResolution(out int input) { input = 0; return true; }
    }

    [Fact]
    public void Typed_Prompt_routes_object_through_typed_Accepts()
    {
        Prompt p = new TypedIntPrompt(5);
        Assert.True(p.Accepts(10));
        Assert.False(p.Accepts(1));
        Assert.False(p.Accepts("not an int"));  // 型不一致は即 false
    }

    [Fact]
    public void Typed_Prompt_exposes_auto_resolution_via_base()
    {
        Prompt p = new TypedAutoZeroPrompt();
        Assert.Equal(0, p.GetAutoResolution());
    }

    [Fact]
    public void ReadInput_with_typed_Prompt_infers_type_argument()
    {
        // <int> 明示なしでコンパイル通ることが本質
        var t = Read.Input(out var holder, new TypedIntPrompt(0));
        Assert.IsType<Take<int>>(t);
        Assert.True(holder.TryResolve(42));
        Assert.Equal(42, holder.Value);
    }
}
