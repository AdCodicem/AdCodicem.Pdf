using System.Diagnostics;
using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Documents;

namespace AdCodicem.Pdf.InliningCheck;

/// <summary>
/// Opens, again and again, a file whose index is rebuilt, until the rebuild's scan for objects has its final optimized
/// compile, and fails when one of its optimized compiles left the lexer out of the scan's loop (#374).
/// </summary>
/// <remarks>
/// <para>
/// The scan reads back each object header a comment parts with the lexer, through <c>HeaderNumbers.TakeLine</c>, which it
/// inlines; the compiler inlines less in a method the more locals it holds, and the scan has twice grown past what kept
/// the lexer in, the rebuild of a file of such lines then about a tenth slower (<c>ObjectScanBenchmarks</c>,
/// <c>percent-strings</c>). Nothing a caller observes tells it, and no benchmark CI runs would.
/// </para>
/// <para>
/// The file is that benchmark's: 4 MB of lines of a string holding a <c>%</c> before a number and a comment, each followed
/// by a line <c>0 obj</c>, so that the profile the compiler gathers sees the lexer called. The compiles are those of the
/// default tiers — on-stack replacement while the first rebuilds run, then the instrumented and final tiers — and each one
/// that optimizes must inline <c>PdfLexer.Read</c> into the scan. The runtime reports its inlining decisions as events,
/// which this process reads from itself. The check holds under the runtime's default settings: without tiered
/// compilation, or without the profile its tiers gather, no compile of the scan inlines the call that reaches the lexer.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>The most rebuilds made before the final compile must have come: some sixty bring it with the default tiers.</summary>
    private const int MaxRebuilds = 400;

    private const int LinesSize = 4 * 1024 * 1024;

    /// <summary>How long the runtime's events may take to reach the listener once the rebuilds stop.</summary>
    private static readonly TimeSpan Settling = TimeSpan.FromSeconds(10);

    private static int Main()
    {
        using var listener = new InliningListener();
        var file = PercentStrings();
        var rebuilds = 0;

        while (rebuilds < MaxRebuilds && !listener.SawFinalCompile)
        {
            using var document = PdfDocument.Open(file);
            _ = document.ObjectCount;
            rebuilds++;
        }

        var waited = Stopwatch.StartNew();

        while (!listener.SawFinalCompile && waited.Elapsed < Settling)
        {
            Thread.Sleep(50);
        }

        var compiles = listener.Compiles();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{rebuilds} rebuilds; the scan's compiles:"));

        foreach (var compile in compiles)
        {
            Console.WriteLine($"  {compile.Tier}: {compile.Verdict}");
        }

        if (!listener.SawFinalCompile)
        {
            Console.WriteLine("The scan never had its final optimized compile: nothing was checked of it.");
            return 1;
        }

        var failed = false;

        foreach (var compile in compiles)
        {
            if (compile.Optimized && !compile.LexerInlined)
            {
                Console.WriteLine($"The {compile.Tier} compile of the scan left PdfLexer.Read out: {compile.Verdict}.");
                failed = true;
            }
        }

        return failed ? 1 : 0;
    }

    /// <summary>Writes a catalog, an empty page tree and lines of a string holding a <c>%</c>, each followed by <c>0 obj</c>.</summary>
    private static byte[] PercentStrings()
    {
        var text = new StringBuilder("%PDF-1.7\n")
            .Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
            .Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

        while (text.Length < LinesSize)
        {
            text.Append("(50%) 4 % x\n0 obj\n");
        }

        return Encoding.Latin1.GetBytes(text.Append("trailer\n<< /Root 1 0 R >>\n%%EOF\n").ToString());
    }
}
