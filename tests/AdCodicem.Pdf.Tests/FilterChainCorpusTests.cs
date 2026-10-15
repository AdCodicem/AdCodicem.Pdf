using System.Security.Cryptography;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// The corpus's documents whose streams name a chain of filters, or read their filters or their parameters through a
/// reference, held to what decoding those streams gives, byte for byte, and to what it reports (#165, #166, #199, #209):
/// nothing, but the one stream whose ASCII85 data lacks its end marker (#141).
/// </summary>
/// <remarks>
/// The digests were taken before the filter chains were resolved first and bounded together, and again after: they
/// agree. The corpus holds no chain of three filters or more, and no parameter a filter reads through a reference
/// (<c>docs/corpus-contributions.md</c>).
/// </remarks>
public class FilterChainCorpusTests
{
    /// <summary>
    /// What decoding a document's streams reports, by document; nothing for the others. Acrobat Distiller 1.0.2's page
    /// thumbnail, object 1363, is ASCII85 data without its <c>~&gt;</c> that ends on a single digit, which qpdf, pikepdf
    /// and MuPDF decode to the same 3,652 bytes, in silence; the JPEG behind it is cut short, which no filter decodes.
    /// </summary>
    private static readonly Dictionary<string, string[]> Reports = new(StringComparer.Ordinal)
    {
        ["remote/opf-format-corpus/distiller102-mac-bts-transportation-statistics-1994.pdf"] =
        [
            "Warning filter.failed at 4336336: An ASCII85 stream ends without its ~> end marker, after a group of a single digit, which encodes no byte; it was dropped, and data may have been lost.",
        ],
    };

    /// <summary>Each document, how many of its streams chain filters or read them through a reference, and the digest.</summary>
    public static TheoryData<string, int, string> Documents => new()
    {
        { "documents/invoice/reportlab-invoice.pdf", 1, "2F9AA66FBFCA8545EB01BDB393520B83108E9532E54CC165B08907414AC9D2BA" },
        { "documents/form/reportlab-subscription-form.pdf", 1, "130C2ECD5FCD0B8951C081184DEE2CD26613CAB5726B4EF8D4011F9E8CAC7B0D" },
        { "documents/scan/reportlab-scanned-receipt.pdf", 2, "55BF775B9379434C9272F2A7F2471BCB2AC49B9B6D724E8BB52352CDE94422D2" },
        { "documents/stress/reportlab-journal-1000-pages.pdf", 1000, "2C1446E844665CE32B1D80B7EE1D007EFCE3AAF99E34CD990C9B52F6BD8225BF" },
        { "vendor/us-federal/xerox-workcentre-5755-ocr-hud-fonsi-mrc.pdf", 2, "E9A3B14F79277AFCA7FCFC19C8F4CAD595A99FEEA0DD8BBFA1E92669B8B47584" },
        { "vendor/us-federal/pdfwriter4-usda-dry-whey-standard-2000.pdf", 7, "A67E2C3CF9C7B3EB2F3C62A413788754FE237E9A43D64AC1C7F2A73C0A29C48C" },
        { "vendor/fr-licence-ouverte/pdfmaker-acrobat-cerfa-12156-form.pdf", 1, "14FE05300D600E0411C141F58E8AD6111A10676DD12F73396EF13BAA30EEF1DB" },
        { "remote/usgs/us-topo-washington-west-2023.pdf", 175, "2C91DF65D19C37E37E56F136D368CC4AC6A66BE1907D6701EE8EF9EBD51DE8FC" },
        { "remote/pdfjs/canon-scan-junk-after-eof-scan-bad.pdf", 1, "961ABB2E947DD7489711FF2DD11DFA26A38D2131C1DA790F7550BBFDB4DD986F" },
        { "remote/pdfplumber/pypdf2-inverted-mediabox-issue932.pdf", 1, "EC76DDB8866640E78038DCB6E793C2E558E6AAB9ADCA91955E1787DB07C88B91" },
        { "remote/opf-format-corpus/xyvision-distiller5-census-statistical-abstract-domestic-trade.pdf", 22, "D23C045CFDCF36FD4B70417B52F957A5C089A4CEB3A9B8245A43EB03CFB22C59" },
        { "remote/opf-format-corpus/distiller102-mac-bts-transportation-statistics-1994.pdf", 267, "07AABE7BD06D4A364AD2D2F40E462986FEB5C92C03DFC7E6F3AE8B786DC126DC" },
        { "remote/opf-format-corpus/jhove-hul-117-acrobat101-student-design-report.pdf", 7, "CC8CBA66E8438088B9B7F71C257BB416A6952D1D9D907E7F6FCB763643DAA9D3" },
        { "remote/opf-format-corpus/jhove-hul-124-freehep-supplementary-figure-s1b.pdf", 1, "DD01577D470F5C1C1E1F0DB77636D0702AB9D5AA2957C081544DFC074E88DF14" },
        { "remote/opf-format-corpus/jhove-hul-129-latex-distiller705-journal-article.pdf", 17, "69EFA8DFCC79AFFB1F5750D9F45617564A47BEFDD65325704473C3858990D199" },
        { "remote/opf-format-corpus/jhove-hul-164-pixel-translations-1972-journal-article-scan.pdf", 30, "2828D82A802BAE977A023DFF888393ADB57DFFE33E6F3480A27BC7C1F697CFA2" },
        { "remote/opf-format-corpus/jhove-hul-28-dvipdfm-pdfstamp-physics-article.pdf", 8, "BA3E2EAC68597AF5839EC50059891B452A7A3F4F68B66A89A8D068380284AAB9" },
        { "remote/opf-format-corpus/pdfwriter4-wordperfect-nureg-cr-nuclear-plant-owners.pdf", 90, "4CE232D378E59B87230641AB86A6100D931A876353E7FCE52A7F40BB19E59B28" },
        { "remote/pdfjs/sap-netweaver-form-bug1727053.pdf", 1, "C8C048083BD8AFDC398EBB20AE119FFB4698BA7AE44F68B5568FC2524EFA2571" },
        { "remote/pdfminer/sap-netweaver-invoice-issue1062.pdf", 6, "4AA1B3A1A5B60FA07BA001843A88C51F739F544586927BC64C089913D12A0BD7" },
        { "remote/eu-dss/excel365-nowina-25-signatures-dss-vri.pdf", 24, "886D64DF11EC5FBC65B328E79753158D5F24BD7BBA2E822ED9A8C2E5D12BDBF5" },
        { "remote/pdfjs/ricoh-3heights-scan-issue5747.pdf", 1, "EF6B1C4F545BEBBDAEE9BA96FD722097B7BD28097842482140935C7C8C11B02E" },
    };

    [Theory]
    [MemberData(nameof(Documents))]
    public void Decodes_every_stream_whose_filters_chain_or_are_read_through_a_reference_as_it_did(string file, int streams, string digest)
    {
        Assert.SkipUnless(Corpus.Paths.Contains(file), $"{file} is a remote document this machine did not fetch");
        var entry = Corpus.Get(file);
        using var document = PdfDocument.Open(Corpus.PathOf(file), CorpusReadingTests.OptionsFor(entry) with { ThrowOnEncrypted = false });
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var diagnostics = new PdfDiagnostics();
        var decoded = 0;

        foreach (var number in document.ObjectNumbers.Order())
        {
            if (document.GetObject(new PdfObjectId(number)) is not PdfStream stream || !Chains(stream.Dictionary))
            {
                continue;
            }

            decoded++;
            hash.AppendData(BitConverter.GetBytes(number));
            hash.AppendData(stream.Decode(diagnostics).Span);
        }

        decoded.Should().Be(streams);
        diagnostics.Select(d => d.ToString()).Should().Equal(Reports.GetValueOrDefault(file, []));
        Convert.ToHexString(hash.GetHashAndReset()).Should().Be(digest);
    }

    /// <summary>
    /// Determines whether a stream names a chain of filters, or reads its filters or parameters through a reference.
    /// </summary>
    private static bool Chains(PdfDictionary dictionary)
    {
        var filter = dictionary.GetRaw(PdfName.Filter);
        var parameters = dictionary.GetRaw(PdfName.DecodeParms);
        return filter is PdfReference || parameters is PdfReference ||
            (filter is PdfArray filters && (filters.Count > 1 || filters.Any(element => element is PdfReference))) ||
            (parameters is PdfArray array && array.Any(element => element is PdfReference));
    }
}
