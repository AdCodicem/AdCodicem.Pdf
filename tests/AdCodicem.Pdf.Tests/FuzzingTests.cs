using System.Diagnostics;
using System.Globalization;
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

    [Fact]
    public void Without_a_rotation_every_seed_document_is_fuzzed()
    {
        // Every commit runs this way, and so does the replay of a nightly failure.
        FuzzingSeeds.ForRun(null).Should().Equal(FuzzingSeeds.All);
        FuzzingSeeds.ForRun("all").Should().Equal(FuzzingSeeds.All);
    }

    [Fact]
    public void Each_night_fuzzes_the_smallest_document_of_every_reader_structure()
    {
        var core = FuzzingSeeds.Core;
        var structures = FuzzingSeeds.All.Select(FuzzingSeeds.SignatureOf).Distinct(StringComparer.Ordinal).ToList();

        core.Select(FuzzingSeeds.SignatureOf).Should().OnlyHaveUniqueItems().And.HaveCount(structures.Count);

        foreach (var file in core)
        {
            var size = new FileInfo(Corpus.PathOf(file)).Length;
            var alike = FuzzingSeeds.All.Where(other => FuzzingSeeds.SignatureOf(other) == FuzzingSeeds.SignatureOf(file));

            alike.Should().OnlyContain(
                other => new FileInfo(Corpus.PathOf(other)).Length >= size,
                $"{file} stands for its structure because nothing like it is smaller");
        }

        FuzzingSeeds.ForRun("12").Should().Contain(core);
    }

    [Fact]
    public void Every_seed_document_is_fuzzed_within_one_rotation()
    {
        var reached = Enumerable.Range(0, FuzzingSeeds.Period)
            .SelectMany(night => FuzzingSeeds.ForRun(night.ToString(CultureInfo.InvariantCulture)))
            .ToHashSet(StringComparer.Ordinal);

        reached.Should().BeEquivalentTo(FuzzingSeeds.All);
    }

    [Fact]
    public void A_night_s_share_is_fixed_by_its_number_and_bounded()
    {
        FuzzingSeeds.ForRun("41").Should().Equal(FuzzingSeeds.ForRun("41"));
        FuzzingSeeds.ForRun("41").Should().HaveCountLessThanOrEqualTo(FuzzingSeeds.Core.Count + FuzzingSeeds.RotatingShare);

        var refused = () => FuzzingSeeds.ForRun("tonight");
        refused.Should().Throw<InvalidOperationException>().WithMessage($"*{FuzzingSeeds.RotationVariable}*");
    }

    /// <summary>
    /// Every seed document on a commit; the nightly campaign's selection when a rotation is set.
    /// </summary>
    public static TheoryData<string> SeedDocuments
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var file in FuzzingSeeds.ForRun(Environment.GetEnvironmentVariable(FuzzingSeeds.RotationVariable)))
            {
                data.Add(file);
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

        // The reason strings are built before the assertion is evaluated, so the input is saved only once a
        // budget is known to be exceeded. Saving it on every pass wrote each mutation to disk and filled the
        // nightly runner's disk.
        if (stopwatch.Elapsed < PerInputBudget && allocated < AllocationBudget)
        {
            return;
        }

        var saved = Save(input, what);

        stopwatch.Elapsed.Should().BeLessThan(
            PerInputBudget,
            $"{what} must not take unbounded time. Input saved to {saved}");

        allocated.Should().BeLessThan(
            AllocationBudget,
            $"{what} allocated {allocated / 1024 / 1024} MB, which a file of {input.Length / 1024} KB must not "
            + $"be able to ask for. Input saved to {saved}");
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
