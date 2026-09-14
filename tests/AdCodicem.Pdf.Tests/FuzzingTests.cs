using System.Diagnostics;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Feeds damaged versions of real documents to the reader and asserts the only two acceptable outcomes.
/// </summary>
/// <remarks>
/// The hostile-input tests cover the failures we thought of. This covers the ones we did not: real files
/// with their bytes mangled at random, which is what a corrupted download, a truncated upload or a
/// deliberate attack actually looks like.
///
/// Mutations are seeded, so a failure is reproducible from the seed printed in the message, and the
/// offending bytes are written out for triage. Iterations default low enough for every commit and can be
/// raised for a longer campaign through ADCODICEM_FUZZ_ITERATIONS.
/// </remarks>
public class FuzzingTests
{
    private static readonly TimeSpan PerInputBudget = TimeSpan.FromSeconds(5);

    /// <summary>
    /// No mutation of a hundred-kilobyte file justifies this much memory. A bound catches the case the
    /// reader is meant to prevent: a length or a count read from the file driving an allocation.
    /// </summary>
    private const long AllocationBudget = 64 * 1024 * 1024;

    private static int Iterations =>
        int.TryParse(Environment.GetEnvironmentVariable("ADCODICEM_FUZZ_ITERATIONS"), out var value) && value > 0
            ? value
            : 60;

    [Theory]
    [MemberData(nameof(SeedDocuments))]
    public void Opening_a_mutated_document_either_works_or_fails_with_a_typed_exception(string file)
    {
        var original = Corpus.Read(file);

        for (var seed = 0; seed < Iterations; seed++)
        {
            var mutated = Mutate(original, seed);

            Survives(
                () =>
                {
                    using var document = PdfDocument.Open(mutated, ReaderOptions);
                    ReadEverything(document);
                },
                mutated,
                $"{Corpus.Get(file).Name}, seed {seed}");
        }
    }

    [Theory]
    [MemberData(nameof(SeedDocuments))]
    public void Parsing_mutated_bytes_never_throws(string file)
    {
        var original = Corpus.Read(file);

        for (var seed = 0; seed < Iterations; seed++)
        {
            var mutated = Mutate(original, seed);

            // The parser is below the point where failure is expressed as an exception: whatever it is
            // given, it reports through diagnostics and returns an object.
            Survives(
                () =>
                {
                    var diagnostics = new PdfDiagnostics();
                    var parser = new PdfObjectParser(mutated, diagnostics: diagnostics);
                    parser.ParseObject();
                },
                mutated,
                $"parsing {Corpus.Get(file).Name}, seed {seed}",
                allowTypedExceptions: false);
        }
    }

    [Fact]
    public void A_mutation_is_reproducible_from_its_seed()
    {
        var original = Corpus.Read(Corpus.PathsWhere(document => document.UseCase == "invoice")[0]);

        Mutate(original, seed: 7).Should().Equal(Mutate(original, seed: 7));
        Mutate(original, seed: 7).Should().NotEqual(Mutate(original, seed: 8));
    }

    /// <summary>
    /// Small documents only: fuzzing is about the shape of the input, not its size, and a thousand-page
    /// document would spend the budget on work rather than on variety.
    /// </summary>
    public static TheoryData<string> SeedDocuments
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var document in Corpus.Documents)
            {
                if (document.Expect.Encrypted || document.Features.Contains("many-pages"))
                {
                    continue;
                }

                if (new FileInfo(Corpus.PathOf(document.File)).Length <= 120 * 1024)
                {
                    data.Add(document.File);
                }
            }

            return data;
        }
    }

    private static PdfReaderOptions ReaderOptions { get; } = new() { ThrowOnEncrypted = true };

    private static void Survives(Action action, byte[] input, string what, bool allowTypedExceptions = true)
    {
        var stopwatch = Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();

        try
        {
            action();
        }
        catch (PdfException) when (allowTypedExceptions)
        {
            // Refusing a document is a legitimate outcome; refusing it any other way is not.
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"{what} threw {exception.GetType().Name}: {exception.Message}. Input saved to {Save(input, what)}",
                exception);
        }
        finally
        {
            stopwatch.Stop();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        stopwatch.Elapsed.Should().BeLessThan(
            PerInputBudget,
            $"{what} must not take unbounded time. Input saved to {Save(input, what)}");

        allocated.Should().BeLessThan(
            AllocationBudget,
            $"{what} allocated {allocated / 1024 / 1024} MB, which a file of {input.Length / 1024} KB must not "
            + $"be able to ask for. Input saved to {Save(input, what)}");
    }

    private static string Save(byte[] input, string what)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"adcodicem-fuzz-{what.Replace(' ', '-').Replace(',', '-').Replace(".pdf", string.Empty)}.pdf");

        File.WriteAllBytes(path, input);
        return path;
    }

    private static long ReadEverything(PdfDocument document)
    {
        long decoded = 0;

        foreach (var number in document.ObjectNumbers.ToList())
        {
            if (document.GetObject(new PdfObjectId(number)) is PdfStream stream && !stream.HasImageFilter())
            {
                decoded += stream.Decode(document.Diagnostics).Length;
            }
        }

        return decoded;
    }

    /// <summary>
    /// Damages a document in one of the ways files are damaged in practice.
    /// </summary>
    /// <remarks>
    /// Random bit flips alone rarely reach the interesting code: PDF is a format of offsets, lengths and
    /// keywords, so the mutations that matter corrupt digits and structural words. Each seed picks one
    /// strategy, which keeps a failure easy to read.
    /// </remarks>
    private static byte[] Mutate(byte[] original, int seed)
    {
        var random = new Random(seed);
        var mutated = (byte[])original.Clone();

        switch (seed % 6)
        {
            case 0:
                for (var i = 0; i < 1 + random.Next(8); i++)
                {
                    var index = random.Next(mutated.Length);
                    mutated[index] ^= (byte)(1 << random.Next(8));
                }

                return mutated;

            case 1:
                // Corrupting digits rewrites offsets, lengths and object numbers — the values the reader
                // is required to distrust.
                for (var i = 0; i < 1 + random.Next(16); i++)
                {
                    var index = random.Next(mutated.Length);
                    if (mutated[index] is >= (byte)'0' and <= (byte)'9')
                    {
                        mutated[index] = (byte)('0' + random.Next(10));
                    }
                }

                return mutated;

            case 2:
                return mutated[..Math.Max(1, random.Next(mutated.Length))];

            case 3:
                var start = random.Next(mutated.Length);
                var length = Math.Min(mutated.Length - start, 1 + random.Next(64));
                random.NextBytes(mutated.AsSpan(start, length));
                return mutated;

            case 4:
                return BreakKeyword(mutated, random, "endstream"u8);

            default:
                return BreakKeyword(mutated, random, "obj"u8);
        }
    }

    private static byte[] BreakKeyword(byte[] mutated, Random random, ReadOnlySpan<byte> keyword)
    {
        var occurrences = new List<int>();
        var index = 0;

        while (index < mutated.Length)
        {
            var found = mutated.AsSpan(index).IndexOf(keyword);
            if (found < 0)
            {
                break;
            }

            occurrences.Add(index + found);
            index += found + keyword.Length;
        }

        if (occurrences.Count > 0)
        {
            mutated[occurrences[random.Next(occurrences.Count)]] = (byte)'X';
        }

        return mutated;
    }
}
