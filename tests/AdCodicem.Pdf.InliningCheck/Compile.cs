namespace AdCodicem.Pdf.InliningCheck;

/// <summary>One compile of the rebuild's scan for objects: its tier, and what it decided of the lexer.</summary>
internal sealed class Compile
{
    /// <summary>Where the runtime's method flags hold the tier a method was compiled at.</summary>
    private const int TierShift = 7;

    /// <summary>The runtime's tier of a method compiled with minimal optimization, which inlines nothing.</summary>
    private const uint MinimalTier = 1;

    /// <summary>The tier the runtime gives a compile it does not name, or the tier of a compile that has not loaded.</summary>
    private const uint UnknownTier = 0;

    private string? _refusal;
    private uint _tier;

    /// <summary>Gets the tier, as the runtime's method flags name it.</summary>
    public string Tier { get; private set; } = "unknown";

    /// <summary>
    /// Gets a value indicating whether the compile must inline the lexer: any tier but the quick ones, which the runtime
    /// replaces once the method runs long; a tier it does not name included.
    /// </summary>
    public bool Checked { get; private set; }

    /// <summary>Gets a value indicating whether the compile is the method's last: its final tier, or the only one there is.</summary>
    public bool Final { get; private set; }

    /// <summary>Gets a value indicating whether the compile inlined the lexer into the scan, and refused it nowhere.</summary>
    public bool LexerInlined { get; private set; }

    /// <summary>Gets what the compile decided of the lexer, as a line says it.</summary>
    public string Verdict =>
        _refusal is not null ? $"PdfLexer.Read refused ({_refusal})"
        : LexerInlined ? "PdfLexer.Read inlined"
        : _tier == MinimalTier ? "minimally optimized, which inlines nothing"
        : _tier == UnknownTier ? "PdfLexer.Read not inlined, at a tier the runtime does not name"
        : Checked ? "PdfLexer.Read not considered: the call that reaches it was not inlined"
        : "not optimized";

    /// <summary>Records a decision of the compile on the lexer.</summary>
    public void Decide(bool inlined, string reason)
    {
        if (inlined)
        {
            LexerInlined = _refusal is null;
        }
        else
        {
            _refusal = reason;
            LexerInlined = false;
        }
    }

    /// <summary>Records the tier the compile loaded at, from the runtime's method flags.</summary>
    public void Load(uint methodFlags)
    {
        // The runtime's optimization tiers: 1 minimally optimized — a method the compiler gave up optimizing, or one marked
        // so —, 2 optimized outside the tiers — without tiered compilation, or a method the runtime optimizes at once —,
        // 3 quick, 4 the final tier, 5 on-stack replacement, 6 quick and instrumented, 7 optimized and instrumented.
        _tier = (methodFlags >> TierShift) & 0x7;
        (Tier, Checked, Final) = _tier switch
        {
            1 => ("minimally optimized", true, true),
            2 => ("optimized", true, true),
            3 => ("quick", false, false),
            4 => ("final tier", true, true),
            5 => ("on-stack replacement", true, false),
            6 => ("quick and instrumented", false, false),
            7 => ("optimized and instrumented", true, false),
            _ => ("unknown", true, false),
        };
    }
}
