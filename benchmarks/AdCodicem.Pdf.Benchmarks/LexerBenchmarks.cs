using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;
using BenchmarkDotNet.Attributes;

namespace AdCodicem.Pdf.Benchmarks;

/// <summary>
/// Measures what lexing costs on content streams dense in numbers, and what parsing those numbers costs alone (#186).
/// </summary>
/// <remarks>
/// Two streams: the first page of a Word print-driver invoice from the committed corpus, the densest in reals it
/// holds, written with up to six decimals; and a synthetic stream of a million numbers, <c>0.7071 -12.5 .25 1024</c>
/// over and over, three reals in four. <see cref="Lex"/> lexes the whole stream through <see cref="PdfLexer"/>;
/// <see cref="ParseNumbers"/> parses only its number tokens, found once in the setup, so the cost of
/// <see cref="PdfNumberParser"/> shows apart from the rest of the lexer. Nothing in M02 lexes a content stream —
/// M07 and M08 will — but a content stream is where reals are densest, so it is where a slower parser would show.
/// </remarks>
[MemoryDiagnoser]
public class LexerBenchmarks
{
    private byte[] _content = [];
    private (int Start, int Length)[] _numbers = [];

    [Params("word-print-driver-invoice", "synthetic")]
    public string Content { get; set; } = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _content = Content == "synthetic"
            ? Synthetic()
            : FirstPageContent("documents/invoice/word-print-driver-invoice-fr.pdf");

        var numbers = new List<(int, int)>();
        var lexer = new PdfLexer(_content);
        for (var token = lexer.Read(); token.Kind != PdfTokenKind.EndOfInput; token = lexer.Read())
        {
            if (token.Kind is PdfTokenKind.Integer or PdfTokenKind.Real)
            {
                numbers.Add((token.Start, token.End - token.Start));
            }
        }

        _numbers = [.. numbers];
    }

    [Benchmark(Description = "Lex the whole stream")]
    public double Lex()
    {
        var sum = 0d;
        var lexer = new PdfLexer(_content);
        for (var token = lexer.Read(); token.Kind != PdfTokenKind.EndOfInput; token = lexer.Read())
        {
            if (token.Kind == PdfTokenKind.Real)
            {
                sum += token.Real;
            }
        }

        return sum;
    }

    [Benchmark(Description = "Parse its numbers alone")]
    public double ParseNumbers()
    {
        var sum = 0d;
        var content = _content.AsSpan();
        foreach (var (start, length) in _numbers)
        {
            if (PdfNumberParser.TryParse(content.Slice(start, length), out _, out var real, out _))
            {
                sum += real;
            }
        }

        return sum;
    }

    private static byte[] Synthetic()
    {
        var builder = new StringBuilder(5_500_000);
        for (var i = 0; i < 250_000; i++)
        {
            builder.Append("0.7071 -12.5 .25 1024 ");
        }

        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static byte[] FirstPageContent(string path)
    {
        using var document = PdfDocument.Open(File.ReadAllBytes(Path.Combine(CorpusRoot(), path)));
        var node = document.Catalog.GetDictionary(PdfName.Pages);
        while (node?.GetArray(PdfName.Kids) is { Count: > 0 } kids)
        {
            node = kids.Resolved(0).AsDictionary();
        }

        var output = new MemoryStream();
        if (node?.GetStream(PdfName.Contents) is { } single)
        {
            output.Write(single.Decode().Span);
        }
        else if (node?.GetArray(PdfName.Contents) is { } parts)
        {
            for (var i = 0; i < parts.Count; i++)
            {
                if (parts.Resolved(i) is PdfStream part)
                {
                    output.Write(part.Decode().Span);
                    output.WriteByte((byte)'\n');
                }
            }
        }

        return output.ToArray();
    }

    private static string CorpusRoot()
    {
        // BenchmarkDotNet runs each benchmark from a generated project under bin/, so the repository is found
        // by walking up to the solution file rather than from a fixed relative path.
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AdCodicem.Pdf.slnx")))
            {
                return Path.Combine(directory.FullName, "tests", "corpus");
            }
        }

        throw new DirectoryNotFoundException("The repository root (AdCodicem.Pdf.slnx) was not found above " + AppContext.BaseDirectory);
    }
}
