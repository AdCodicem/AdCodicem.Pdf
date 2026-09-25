using System.Globalization;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Chooses the corpus documents a mutation campaign starts from.
/// </summary>
/// <remarks>
/// Every commit fuzzes every seed document, a few mutations each. The nightly campaign, hundreds of times
/// deeper, need not: most documents share their structure with others, and its cost would grow with the
/// corpus. Each night it fuzzes a <see cref="Core"/> of one document per reader structure — the smallest — and
/// a rotating share of the rest, chosen by the run's number, so that every seed document is still reached
/// within a few nights at a cost that does not grow with the corpus. A failure names its document and its
/// seed; run without <c>ADCODICEM_FUZZ_ROTATION</c>, the campaign includes every seed document, so it replays
/// from those two alone.
/// </remarks>
internal static class FuzzingSeeds
{
    /// <summary>The environment variable that selects a night's share; absent or <c>all</c>, every seed.</summary>
    internal const string RotationVariable = "ADCODICEM_FUZZ_ROTATION";

    /// <summary>How many documents beyond the core one night fuzzes.</summary>
    internal const int RotatingShare = 16;

    /// <summary>Only small documents: fuzzing is about the shape of the input, not its size.</summary>
    private const long MaximumSize = 120 * 1024;

    /// <summary>The filters the reader decodes itself, whose presence changes the paths a mutation can reach.</summary>
    private static readonly string[] Filters =
        ["FlateDecode", "LZWDecode", "ASCII85Decode", "ASCIIHexDecode", "RunLengthDecode", "Crypt"];

    private static readonly Lazy<IReadOnlyList<Seed>> Seeds = new(Load);

    /// <summary>Gets every seed document: small, not encrypted, and not one of the many-page references.</summary>
    internal static IReadOnlyList<string> All => [.. Seeds.Value.Select(seed => seed.File)];

    /// <summary>Gets the smallest seed document of each reader structure, which every night fuzzes.</summary>
    internal static IReadOnlyList<string> Core =>
    [
        .. Seeds.Value
            .GroupBy(seed => seed.Signature, StringComparer.Ordinal)
            .Select(group => group.OrderBy(seed => seed.Size).ThenBy(seed => seed.File, StringComparer.Ordinal).First().File)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>Gets the number of nights after which the rotating share has reached every other seed.</summary>
    internal static int Period => Math.Max(1, (Others.Count + RotatingShare - 1) / RotatingShare);

    private static IReadOnlyList<string> Others =>
        [.. All.Except(Core, StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>Gets the reader structure a seed document stands for.</summary>
    internal static string SignatureOf(string file) =>
        Seeds.Value.First(seed => seed.File == file).Signature;

    /// <summary>
    /// Gets the documents one run fuzzes: every seed without a rotation, else the core and that night's share.
    /// </summary>
    internal static IReadOnlyList<string> ForRun(string? rotation)
    {
        if (string.IsNullOrWhiteSpace(rotation) || rotation.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return All;
        }

        if (!int.TryParse(rotation, NumberStyles.None, CultureInfo.InvariantCulture, out var night))
        {
            throw new InvalidOperationException(
                $"{RotationVariable} must be a run number or 'all', not '{rotation}'.");
        }

        var others = Others;
        var share = new List<string>(Core);

        if (others.Count > 0)
        {
            var start = (int)((long)night * RotatingShare % others.Count);

            for (var offset = 0; offset < Math.Min(RotatingShare, others.Count); offset++)
            {
                share.Add(others[(start + offset) % others.Count]);
            }
        }

        return share;
    }

    private static IReadOnlyList<Seed> Load()
    {
        var seeds = new List<Seed>();

        foreach (var document in Corpus.Documents)
        {
            if (document.Expect.Encrypted || document.Features.Contains("many-pages"))
            {
                continue;
            }

            var path = Corpus.PathOf(document.File);
            var size = new FileInfo(path).Length;

            if (size <= MaximumSize)
            {
                seeds.Add(new Seed(document.File, size, Signature(File.ReadAllBytes(path), damaged: !document.Expect.Clean)));
            }
        }

        return seeds;
    }

    /// <summary>
    /// What the reader meets before any content: the form of the index, object streams, linearization, the
    /// number of revisions, bytes before the header, line endings, the filters it decodes, predictors, and
    /// whether the file is damaged. Two documents alike in all of these exercise the same paths.
    /// </summary>
    private static string Signature(ReadOnlySpan<byte> bytes, bool damaged)
    {
        var table = bytes.IndexOf("\nxref"u8) >= 0 || bytes.IndexOf("\rxref"u8) >= 0;
        var stream = bytes.IndexOf("/XRef"u8) >= 0;
        var index = (table, stream) switch
        {
            (true, true) when bytes.IndexOf("/XRefStm"u8) >= 0 => "hybrid",
            (true, true) => "table-then-stream",
            (true, false) => "table",
            (false, true) => "stream",
            _ => "none",
        };

        var linearized = bytes[..Math.Min(bytes.Length, 2048)].IndexOf("/Linearized"u8) >= 0;
        var revisions = Math.Min(bytes.Count("%%EOF"u8), 3);
        var crlf = bytes.Count("\r\n"u8);
        var lf = bytes.Count((byte)'\n');
        var cr = bytes.Count((byte)'\r');
        var endings = crlf > lf / 2 ? "crlf" : cr > lf ? "cr" : "lf";

        var filters = new List<string>();
        foreach (var filter in Filters)
        {
            if (bytes.IndexOf(System.Text.Encoding.ASCII.GetBytes(filter)) >= 0)
            {
                filters.Add(filter);
            }
        }

        return string.Join(
            '|',
            index,
            bytes.IndexOf("/ObjStm"u8) >= 0 ? "object-streams" : "-",
            linearized ? "linearized" : "-",
            $"revisions-{revisions}",
            bytes.StartsWith("%PDF"u8) ? "-" : "prefix",
            endings,
            string.Join('+', filters),
            bytes.IndexOf("/Predictor"u8) >= 0 ? "predictor" : "-",
            damaged ? "damaged" : "clean");
    }

    private sealed record Seed(string File, long Size, string Signature);
}
