using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The acceptance conditions of M01, run against real documents from real producers.
/// </summary>
/// <remarks>
/// Hand-built files prove the reader handles what we imagined; these prove it handles what Chromium,
/// LibreOffice, ReportLab and qpdf actually emit — and what a damaged one of those looks like. See
/// docs/corpus.md.
/// </remarks>
public class CorpusReadingTests
{
    private static readonly TimeSpan OpenBudget = TimeSpan.FromSeconds(20);

    /// <summary>How the corpus model reads the manifest: the key <c>readerLimits</c> is its <c>ReaderLimits</c>.</summary>
    private static readonly JsonSerializerOptions ManifestOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Every folder under the one enumerated and every file in them, hidden ones included, matched whatever the case.
    /// </summary>
    private static readonly EnumerationOptions EveryFile = new()
    {
        RecurseSubdirectories = true,
        MatchCasing = MatchCasing.CaseInsensitive,
        AttributesToSkip = 0,
    };

    [Fact]
    public void The_corpus_manifest_describes_every_document_present()
    {
        // Every PDF under the corpus root, wherever it lies — documents/, vendor/, the private/ and remote/ folders
        // git ignores, or a folder nobody thought of — and whether its extension is .pdf or .PDF.
        var onDisk = Directory.EnumerateFiles(Corpus.Root, "*.pdf", EveryFile)
            .Select(path => Path.GetRelativePath(Corpus.Root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order()
            .ToList();

        var described = Corpus.Documents.Select(document => document.File).Order().ToList();

        // A document nobody asserts anything about is not part of the corpus, it is clutter.
        described.Should().Equal(onDisk);
    }

    [Fact]
    public void Remote_documents_are_pinned_and_kept_where_git_ignores_them()
    {
        // Read raw: the corpus leaves out the remote entries whose file was not fetched, and those are
        // exactly the ones this main job never sees otherwise.
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Corpus.Root, "manifest.json")));

        foreach (var entry in manifest.RootElement.GetProperty("documents").EnumerateArray())
        {
            var file = entry.GetProperty("file").GetString();
            var remote = entry.TryGetProperty("origin", out var origin) && origin.GetString() == "remote";

            // ADR 32: a remote document is fetched, never committed, and only the bytes that were reviewed
            // are accepted.
            (file?.StartsWith("remote/", StringComparison.Ordinal) ?? false).Should().Be(
                remote, $"{file}: origin \"remote\" and the remote/ folder go together");

            if (remote)
            {
                var source = entry.TryGetProperty("source", out var value) ? value : default;
                Text(source, "url").Should().MatchRegex(@"\Ahttps?://\S+\z", $"{file} is fetched from its source");
                Text(source, "sha256").Should().MatchRegex(@"\A[0-9a-f]{64}\z", $"{file} is pinned to its SHA-256");
                Size(source, "bytes").Should().BePositive($"{file} is pinned to its exact size");
            }
        }
    }

    [Fact]
    public void A_document_taken_from_an_archive_pins_the_archive_and_itself()
    {
        // ADR 33: the archive is pinned by every entry that names it, the same way, and each member is taken
        // once, by a path that stays inside the archive — the fetcher never writes where a member's name says.
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Corpus.Root, "manifest.json")));
        var pins = new Dictionary<string, string>(StringComparer.Ordinal);
        var members = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in manifest.RootElement.GetProperty("documents").EnumerateArray())
        {
            var file = entry.GetProperty("file").GetString();
            var source = entry.TryGetProperty("source", out var value) ? value : default;
            var url = Text(source, "url");

            if (url is null || entry.GetProperty("origin").GetString() != "remote")
            {
                continue;
            }

            var pin = "document";

            if (source.TryGetProperty("archive", out var archive))
            {
                Text(archive, "sha256").Should().MatchRegex(@"\A[0-9a-f]{64}\z", $"{file}: the archive is pinned to its SHA-256");
                Size(archive, "bytes").Should().BePositive($"{file}: the archive is pinned to its exact size");

                var member = Text(archive, "member");
                IsInsideItsArchive(member).Should().BeTrue($"{file}: the member '{member}' stays inside its archive");
                members.Add($"{url} {member}").Should().BeTrue($"{file}: {member} is taken once");

                pin = $"{Text(archive, "sha256")} {Size(archive, "bytes")}";
            }

            // One URL serves one thing: a document, or one archive pinned the same way by every entry.
            pins.TryAdd(url, pin);
            pins[url].Should().Be(pin, $"{file}: {url} is pinned the same way by every entry naming it");
        }
    }

    /// <summary>
    /// The rule fetch_remote.py applies to a member's path, so that both validators accept exactly the same
    /// names: relative, in POSIX form, with no control character and no empty, "." or ".." segment.
    /// </summary>
    private static bool IsInsideItsArchive(string? member) =>
        !string.IsNullOrEmpty(member)
        && member[0] != '/'
        && !member.Contains('\\', StringComparison.Ordinal)
        && !member.Any(character => character < ' ' || character == '\u007f')
        && member.Split('/').All(segment => segment is not ("" or "." or ".."));

    private static string? Text(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static long Size(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var size) ? size : 0;

    [Fact]
    public void A_committed_document_weighs_at_most_2_MB()
    {
        // Everyone who clones the repository downloads the committed corpus. Size never turns a document
        // down: over 2 MB it is fetched on demand from its public URL instead (ADR 32). Remote and private
        // documents are never committed, so their weight is nobody's download.
        const long Threshold = 2_000_000;

        var oversized = Corpus.Documents
            .Where(document => document.Origin != "remote" && !document.File.StartsWith("private/", StringComparison.Ordinal))
            .Where(document => new FileInfo(Corpus.PathOf(document.File)).Length > Threshold)
            .Select(document => document.File);

        oversized.Should().BeEmpty("a document over 2 MB belongs in the remote corpus, not in the repository");
    }

    [Fact]
    public void The_corpus_covers_several_producers_and_every_use_case()
    {
        Corpus.Documents.Select(document => document.Producer.Split(' ')[0]).Distinct().Count()
            .Should().BeGreaterThanOrEqualTo(3, "the shape of a cross-reference section is a producer's signature");

        var useCases = Corpus.Documents.Select(document => document.UseCase).Distinct().ToList();
        useCases.Should().Contain("invoice");
        useCases.Should().Contain("report");
        useCases.Should().Contain("contract");
        useCases.Should().Contain("form");
        useCases.Should().Contain("scan");
        useCases.Should().Contain("archival");
        useCases.Should().Contain("damaged");
    }

    [Theory]
    [MemberData(nameof(AllDocuments))]
    public void Every_corpus_document_opens_as_its_manifest_describes(string file)
    {
        var entry = Corpus.Get(file);
        Assert.SkipWhen(
            entry.Expect.IsUnsupportedIn(nameof(Every_corpus_document_opens_as_its_manifest_describes)),
            $"{entry.Name}: {entry.Expect.Unsupported}");

        if (entry.Expect.Encrypted)
        {
            // Decryption arrives in M16; until then the refusal must be typed and immediate.
            FluentThrow<PdfEncryptedException>(() => PdfDocument.Open(Corpus.Read(file), OptionsFor(entry)));
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        using var document = PdfDocument.Open(Corpus.Read(file), OptionsFor(entry));
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(OpenBudget, $"opening {entry.Name} must not take unbounded time");

        ExpectCatalog(document, entry, $"{entry.Name} has no document catalog");
        document.WasRepaired.Should().Be(entry.Expect.IndexRebuilt, $"{entry.Name}: unexpected rebuild state");

        // Judge the diagnostics on a full read, not on opening: a lying /Length is only discovered when
        // the stream it describes is actually decoded, which is the lazy reader behaving as designed. The
        // page tree is walked too, each page's contents and resources resolved: a reference to an object the
        // file lacks is only followed there.
        ReadEverything(document);
        var pages = entry.Expect.CatalogRecoverable ? CountPages(document) : 0;

        foreach (var code in entry.Expect.RequiredDiagnostics)
        {
            document.Diagnostics.Contains(code).Should().BeTrue(
                $"{entry.Name} should report '{code}', reported: {Describe(document.Diagnostics)}");
        }

        if (entry.Expect.Clean)
        {
            // A well-formed document must not need any repair, and must not worry the reader.
            var noise = document.Diagnostics
                .Where(entry => entry.Severity is PdfDiagnosticSeverity.Repair or PdfDiagnosticSeverity.Warning)
                .ToList();

            noise.Should().BeEmpty($"{entry.Name} is well formed but produced {Describe(document.Diagnostics)}");
        }

        if (entry.Expect.Pages is { } expectedPages && entry.Expect.CatalogRecoverable)
        {
            pages.Should().Be(expectedPages, $"{entry.Name}: page count");
        }
    }

    [Theory]
    [MemberData(nameof(AllDocuments))]
    public void Every_message_a_document_earns_is_printable_ascii_on_one_line_whatever_the_culture(string file)
    {
        // Persian writes a minus sign, a decimal and a group separator of its own, outside ASCII (#159): a message a
        // host prints or a log keeps reads the same under every culture, and quotes what the file wrote without
        // carrying a byte a terminal takes for a command.
        var entry = Corpus.Get(file);
        Assert.SkipWhen(
            entry.Expect.IsUnsupportedIn(nameof(Every_message_a_document_earns_is_printable_ascii_on_one_line_whatever_the_culture)),
            $"{entry.Name}: {entry.Expect.Unsupported}");
        var culture = CultureInfo.CurrentCulture;
        var texts = new List<string>();

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fa-IR");
            using var document = PdfDocument.Open(Corpus.PathOf(entry.File), OptionsFor(entry) with { ThrowOnEncrypted = false });
            ReadEverything(document);
            var report = new PdfValidator().Validate(document);

            foreach (var diagnostic in document.Diagnostics)
            {
                texts.Add(diagnostic.Message);
                texts.Add(diagnostic.ToString());
            }

            foreach (var finding in report.Findings)
            {
                texts.Add(finding.Message);
                texts.Add(finding.Remedy ?? string.Empty);
                texts.Add(finding.ToString());
            }

            texts.Add(report.ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        texts.Where(text => text.Any(character => character is < ' ' or > '~')).Should().BeEmpty(
            $"{entry.Name}'s messages are printable ASCII on one line");
    }

    [Theory]
    [MemberData(nameof(LargeDocuments))]
    public void Opening_does_not_read_the_content_of(string file)
    {
        var entry = Corpus.Get(file);
        Assert.SkipWhen(
            entry.Expect.IsUnsupportedIn(nameof(Opening_does_not_read_the_content_of)),
            $"{entry.Name}: {entry.Expect.Unsupported}");
        Assert.SkipWhen(entry.Expect.Encrypted, $"{entry.Name}: an encrypted document is refused at opening until M16");

        var source = new CountingSource(Corpus.Read(file));
        var size = source.Length;

        using var document = PdfDocument.Open(source, OptionsFor(entry), ownsSource: false);
        document.Catalog.Required();

        var readAtOpen = source.BytesRead;

        // Indexing touches the header, the tail and the cross-reference sections — never the page content.
        readAtOpen.Should().BeLessThan(
            size / 4,
            $"{entry.Name}: opening read {readAtOpen} of {size} bytes, so content was read eagerly");
    }

    [Fact]
    public void Reading_every_page_of_the_largest_document_stays_within_its_memory_budget()
    {
        var entry = Corpus.Documents.First(document => document.Features.Contains("many-pages"));
        var bytes = Corpus.Read(entry.File);

        // Per-thread, not process-wide: the suite runs in parallel and a process-wide counter would
        // measure whatever else happens to be running.
        var before = GC.GetAllocatedBytesForCurrentThread();
        using (var document = PdfDocument.Open(bytes, OptionsFor(entry)))
        {
            CountPages(document).Should().Be(entry.Expect.Pages!.Value);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Indexing a thousand-page document and walking its whole page tree measured 2.4 MB when M01 closed, and
        // 3.2 MB on 2026-10-01, roughly 3.3 KB per page: proportional to the number of objects, not to the weight
        // of the content. The budget leaves headroom for producer variation and fails loudly on a regression.
        allocated.Should().BeLessThan(
            4 * 1024 * 1024,
            $"indexing {entry.Name} allocated {allocated / 1024} KB");
    }

    [Theory]
    [MemberData(nameof(DamagedDocuments))]
    public void Damaged_documents_are_recovered_as_far_as_an_independent_tool_recovers_them(string file)
    {
        var entry = Corpus.Get(file);
        Assert.SkipWhen(
            entry.Expect.IsUnsupportedIn(nameof(Damaged_documents_are_recovered_as_far_as_an_independent_tool_recovers_them)),
            $"{entry.Name}: {entry.Expect.Unsupported}");

        using var document = PdfDocument.Open(Corpus.Read(file), OptionsFor(entry));

        ExpectCatalog(document, entry, $"{entry.Name}: qpdf recovers a catalog here, so must we");
        ReadEverything(document);

        // Damage is never silent: the reader says what it worked around, and what it reads without a word — a
        // generation its entry gets wrong, a trailer without /Size — the validator reports (M02).
        var findings = new PdfValidator().Validate(document).Findings.Count;
        (document.Diagnostics.Count + findings).Should().BeGreaterThan(0, $"{entry.Name}: damage must never be silent");

        if (entry.Expect.Pages is { } expectedPages && entry.Expect.CatalogRecoverable)
        {
            CountPages(document).Should().Be(expectedPages, $"{entry.Name}: recovered page count");
        }
    }

    [Theory]
    [InlineData("vendor/opf-format-corpus/distiller7-pscript5-census-housing-units-2005.pdf", 14, 484, 3879, 1499)]
    [InlineData("vendor/opf-format-corpus/groff-distiller405-mac-usgs-gps-noise-spectra.pdf", 211, 1118, 1310, 3804)]
    [InlineData("vendor/opf-format-corpus/word9-distiller405-usgs-nwql-volatile-organics-methods.pdf", 237, 791, 3067, 2004)]
    public void A_flate_stream_that_turns_corrupt_keeps_what_libz_keeps_and_is_reported_at_the_byte_libz_meets_the_fault_in(
        string file,
        int number,
        int faultAt,
        int encoded,
        int kept)
    {
        // The figures are libz's, not the reader's: the input its inflater had taken and the bytes it had written when
        // it met the fault, "invalid distance too far back" in each — within a block, where the unit tests' streams
        // fault at the start of one. qpdf keeps none of these three streams, so its referee cannot say how much the
        // reader keeps of them: this does, on the corpus CI always has.
        using var document = PdfDocument.Open(Corpus.Read(file));
        var diagnostics = new PdfDiagnostics();

        var decoded = document.GetObject(new PdfObjectId(number)).AsStream().Required().Decode(diagnostics);

        decoded.Length.Should().Be(kept);
        var report = diagnostics.Should().ContainSingle().Which;
        report.Code.Should().Be(PdfDiagnosticCodes.FilterFailed);
        report.Severity.Should().Be(PdfDiagnosticSeverity.Warning);
        report.Message.Should().Be(
            $"A Flate stream is corrupt at byte {faultAt} of its {encoded}; the {kept} bytes decoded before the fault was found were kept.");
    }

    [Fact]
    public void A_raised_reader_limit_raises_a_default_and_replaces_a_skip()
    {
        // Read raw, as the pins are: the only document with raised limits so far is remote, and the main job
        // never sees it otherwise.
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Corpus.Root, "manifest.json")));

        foreach (var entry in manifest.RootElement.GetProperty("documents").EnumerateArray())
        {
            if (!entry.TryGetProperty("readerLimits", out var value))
            {
                continue;
            }

            var file = entry.GetProperty("file").GetString();
            var raised = value.Deserialize<CorpusReaderLimits>(ManifestOptions)!;
            var limits = LimitsFor(raised);

            // ADR 34: a valid document the defaults cannot read whole is read under a raised limit, not
            // skipped, and a limit set to its default, or below, raises nothing.
            entry.GetProperty("expect").TryGetProperty("unsupported", out _).Should().BeFalse(
                $"{file}: a document read under raised limits is read, not skipped");
            Raises(raised.MaxDecodedStreamLength, limits.MaxDecodedStreamLength, PdfReaderLimits.Default.MaxDecodedStreamLength, file);
            Raises(raised.MaxObjectLength, limits.MaxObjectLength, PdfReaderLimits.Default.MaxObjectLength, file);
            Raises(raised.MaxXRefSectionLength, limits.MaxXRefSectionLength, PdfReaderLimits.Default.MaxXRefSectionLength, file);
            Raises(raised.MaxXRefSectionCount, limits.MaxXRefSectionCount, PdfReaderLimits.Default.MaxXRefSectionCount, file);
            Raises(raised.MaxTrailerLength, limits.MaxTrailerLength, PdfReaderLimits.Default.MaxTrailerLength, file);
            limits.Should().NotBe(PdfReaderLimits.Default, $"{file}: readerLimits raises at least one limit");
        }

        static void Raises(int? raised, int applied, int byDefault, string? file)
        {
            if (raised is not null)
            {
                applied.Should().BeGreaterThan(byDefault, $"{file}: a limit in readerLimits raises its default");
            }
        }
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(DocumentsReadUnderRaisedLimits))]
    public void A_document_read_under_raised_limits_reaches_a_default_one(string file)
    {
        // The negative control of readerLimits: under the defaults the document is cut, and says which
        // property to raise, so an override that is no longer needed does not outlive its reason.
        var entry = Corpus.Get(file);
        var raised = LimitsFor(entry.ReaderLimits!);

        using (var document = PdfDocument.Open(Corpus.Read(file)))
        {
            ReadEverything(document);
            var reached = document.Diagnostics.Where(diagnostic => diagnostic.Code.StartsWith("limit.", StringComparison.Ordinal)).ToList();

            reached.Should().NotBeEmpty($"{entry.Name} is read under raised limits, so the defaults must cut it");
            reached.Should().OnlyContain(
                diagnostic => NamesARaisedLimit(diagnostic.Message, raised),
                $"{entry.Name}: each limit reached is one its entry raises");
        }

        // Opened to throw, the same document throws from whichever operation reaches the guard — here the
        // decoding, long after opening succeeded.
        using var strict = PdfDocument.Open(Corpus.Read(file), PdfReaderOptions.Default with { ThrowOnLimit = true });
        var thrown = FluentActions.Invoking(() => ReadEverything(strict)).Should().Throw<PdfLimitExceededException>().Which;
        NamesARaisedLimit($"PdfReaderLimits.{thrown.LimitName}", raised).Should().BeTrue(
            $"{entry.Name}: {thrown.LimitName} is not among the limits its entry raises");
    }

    public static TheoryData<string> AllDocuments => Theory(Corpus.Paths);

    public static TheoryData<string> DocumentsReadUnderRaisedLimits =>
        Theory(Corpus.PathsWhere(document => document.ReaderLimits is not null));

    /// <summary>
    /// The options a corpus document is opened with: the defaults, with the limits its entry raises.
    /// Encrypted documents still throw, as <see cref="PdfReaderOptions.Default"/> has them.
    /// </summary>
    internal static PdfReaderOptions OptionsFor(CorpusDocument entry) =>
        entry.ReaderLimits is { } raised
            ? PdfReaderOptions.Default with { Limits = LimitsFor(raised) }
            : PdfReaderOptions.Default;

    private static PdfReaderLimits LimitsFor(CorpusReaderLimits raised)
    {
        var limits = PdfReaderLimits.Default;

        return limits with
        {
            MaxDecodedStreamLength = raised.MaxDecodedStreamLength ?? limits.MaxDecodedStreamLength,
            MaxObjectLength = raised.MaxObjectLength ?? limits.MaxObjectLength,
            MaxXRefSectionLength = raised.MaxXRefSectionLength ?? limits.MaxXRefSectionLength,
            MaxXRefSectionCount = raised.MaxXRefSectionCount ?? limits.MaxXRefSectionCount,
            MaxTrailerLength = raised.MaxTrailerLength ?? limits.MaxTrailerLength,
        };
    }

    private static bool NamesARaisedLimit(string message, PdfReaderLimits raised)
    {
        var defaults = PdfReaderLimits.Default;

        return (raised.MaxDecodedStreamLength != defaults.MaxDecodedStreamLength && message.Contains("PdfReaderLimits.MaxDecodedStreamLength", StringComparison.Ordinal))
            || (raised.MaxObjectLength != defaults.MaxObjectLength && message.Contains("PdfReaderLimits.MaxObjectLength", StringComparison.Ordinal))
            || (raised.MaxXRefSectionLength != defaults.MaxXRefSectionLength && message.Contains("PdfReaderLimits.MaxXRefSectionLength", StringComparison.Ordinal))
            || (raised.MaxXRefSectionCount != defaults.MaxXRefSectionCount && message.Contains("PdfReaderLimits.MaxXRefSectionCount", StringComparison.Ordinal))
            || (raised.MaxTrailerLength != defaults.MaxTrailerLength && message.Contains("PdfReaderLimits.MaxTrailerLength", StringComparison.Ordinal));
    }

    /// <summary>
    /// A catalog where one can be recovered, and none where the file holds none: the reader recovers what
    /// is there and never invents what is not.
    /// </summary>
    private static void ExpectCatalog(PdfDocument document, CorpusDocument entry, string because)
    {
        if (entry.Expect.CatalogRecoverable)
        {
            document.Catalog.Required(because);
        }
        else
        {
            document.Catalog.Should().BeNull($"{entry.Name}: no object in the file is a catalog");
            entry.Expect.Pages.Should().BeNull($"{entry.Name}: a file without a catalog has no page count to expect");
        }
    }

    /// <summary>
    /// Lazy opening is promised for documents whose index can be read as written: rebuilding one means
    /// scanning the whole file, by definition.
    /// </summary>
    public static TheoryData<string> LargeDocuments =>
        Theory(Corpus.PathsWhere(document =>
            (document.Features.Contains("many-pages") || document.Features.Contains("dct-image"))
            && !document.Expect.IndexRebuilt));

    public static TheoryData<string> DamagedDocuments =>
        Theory(Corpus.PathsWhere(document => !document.Expect.Clean));

    private static TheoryData<string> Theory(IReadOnlyList<string> paths)
    {
        var data = new TheoryData<string>();

        foreach (var path in paths)
        {
            data.Add(path);
        }

        return data;
    }

    /// <summary>
    /// Resolves every object and decodes every stream, so that anything wrong with the file has had the
    /// chance to be noticed. Returns the number of decoded bytes, to keep the work from being elided.
    /// </summary>
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
    /// Counts the pages as the library's walk of the page tree counts them — each null or missing kid a page with
    /// nothing on it, as qpdf does (M02) —, and resolves each page's contents and resources, which exercises reference
    /// resolution and inherited structure across every producer in the corpus. The page API is M06's.
    /// </summary>
    private static int CountPages(PdfDocument document)
    {
        var walk = PageTreeWalk.Run(document);

        foreach (var (number, _) in walk.PageIndexes)
        {
            if (document.GetObject(new PdfObjectId(number)).AsDictionary() is { } page)
            {
                ResolvePageEntries(page);
            }
        }

        return walk.PageCount;
    }

    private static void ResolvePageEntries(PdfDictionary page)
    {
        _ = page.GetDictionary(PdfName.Resources);

        if (page.GetArray(PdfName.Contents) is { } contents)
        {
            for (var index = 0; index < contents.Count; index++)
            {
                _ = contents.Resolved(index);
            }
        }
        else
        {
            _ = page.Get(PdfName.Contents);
        }
    }

    private static string Describe(PdfDiagnostics diagnostics) =>
        diagnostics.Count == 0 ? "no diagnostics" : string.Join("; ", diagnostics.Select(entry => entry.ToString()));

    private sealed class CountingSource : PdfFileSource
    {
        private readonly PdfFileSource _inner;

        public CountingSource(byte[] data) => _inner = FromMemory(data);

        public long BytesRead { get; private set; }

        public override long Length => _inner.Length;

        public override int Read(long offset, Span<byte> buffer)
        {
            var read = _inner.Read(offset, buffer);
            BytesRead += read;
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
