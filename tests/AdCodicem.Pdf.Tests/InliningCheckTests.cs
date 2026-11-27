using AdCodicem.Pdf.InliningCheck;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The inlining check, <c>tests/AdCodicem.Pdf.InliningCheck</c>: which of the scan's compiles it holds to inlining the
/// lexer, and when it takes a compile to have inlined it (#374).
/// </summary>
public class InliningCheckTests
{
    /// <summary>Where the runtime's method flags hold a compile's tier.</summary>
    private const int TierShift = 7;

    /// <summary>The flag the runtime sets on a method it compiled, beside the tier.</summary>
    private const uint Jitted = 0x8;

    [Theory]
    [InlineData(0, "unknown", true, false)]
    [InlineData(1, "minimally optimized", true, true)]
    [InlineData(2, "optimized", true, true)]
    [InlineData(3, "quick", false, false)]
    [InlineData(4, "final tier", true, true)]
    [InlineData(5, "on-stack replacement", true, false)]
    [InlineData(6, "quick and instrumented", false, false)]
    [InlineData(7, "optimized and instrumented", false, false)]
    public void Holds_every_compile_but_the_tiers_the_runtime_replaces_and_ends_at_the_last(int tier, string name, bool held, bool last)
    {
        // The quick tiers are replaced once the method runs long, the instrumented ones once their profile is gathered; a
        // tier the runtime does not name is held, and never taken for the last.
        var compile = new Compile();

        compile.Load(((uint)tier << TierShift) | Jitted);

        compile.Tier.Should().Be(name);
        compile.Checked.Should().Be(held);
        compile.Final.Should().Be(last);
    }

    [Theory]
    [InlineData("inlined", true)]
    [InlineData("refused", false)]
    [InlineData("refused inlined", false)]
    [InlineData("inlined refused", false)]
    [InlineData("", false)]
    public void Takes_the_lexer_for_inlined_only_where_the_compile_refused_it_at_no_call(string decisions, bool inlined)
    {
        // A compile that reaches the lexer through two calls may inline it at one and refuse it at the other, in either
        // order: the scan's loop runs the call it refused.
        var compile = new Compile();

        foreach (var decision in decisions.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            compile.Decide(decision == "inlined", "unprofitable inline");
        }

        compile.Load(4u << TierShift);

        compile.LexerInlined.Should().Be(inlined);
        compile.Verdict.Should().Be(decisions switch
        {
            "inlined" => "PdfLexer.Read inlined",
            "" => "PdfLexer.Read not considered: the call that reaches it was not inlined",
            _ => "PdfLexer.Read refused (unprofitable inline)",
        });
    }
}
