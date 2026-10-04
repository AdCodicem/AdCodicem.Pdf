using System.Text;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Files whose trailer the chain does not give, so that only a rebuild finds it: each writes given entries in a trailer,
/// and says where the trailer a finding on them is located at lies (#126).
/// </summary>
internal static class RebuiltTrailers
{
    /// <summary>The shapes <see cref="Build"/> writes, one each.</summary>
    public static readonly TheoryData<string> Shapes =
    [
        "no startxref",
        "startxref past the end",
        "startxref at no section",
        "startxref past what a long holds",
        "first table without a trailer",
        "entries only in the older trailer",
        "first section a cross-reference stream that cannot be read",
    ];

    /// <summary>
    /// Writes <paramref name="shape"/> of the sound one-page document, <paramref name="entries"/> added to its trailer, and
    /// gives where the newest <c>trailer</c> keyword the rebuild merges starts.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><c>startxref past what a long holds</c> starts five bytes in, and its <c>startxref</c> gives the
    /// largest <see cref="long"/>, which the header's offset would wrap (#125).</description></item>
    /// <item><description><c>first table without a trailer</c> is read by the chain: its one table, which does not index
    /// the catalog, runs to the end of the file with no trailer, after the <c>startxref</c> that names it, so that the
    /// trailer the chain gives is empty; the catalog is looked for by rebuilding the index, which finds the older trailer,
    /// unlinked.</description></item>
    /// <item><description><c>entries only in the older trailer</c> adds an update whose trailer, the newest, names the
    /// older through <c>/Prev</c> and whose <c>startxref</c> points past the end: the rebuild merges the entries from the
    /// older trailer, under the newest, where the finding is located.</description></item>
    /// </list>
    /// </remarks>
    public static (byte[] File, long Trailer) Build(string shape, string entries)
    {
        var sound = PdfTemplate.Sound.Replace("/Size 4 /Root 1 0 R", "/Size 4 /Root 1 0 R " + entries, StringComparison.Ordinal);
        const string Tail = "startxref\n{xref:1}\n";

        var (template, occurrence) = shape switch
        {
            "no startxref" => (sound.Replace(Tail, string.Empty, StringComparison.Ordinal), 1),
            "startxref past the end" => (sound.Replace(Tail, "startxref\n999999\n", StringComparison.Ordinal), 1),
            "startxref at no section" => (sound.Replace(Tail, "startxref\n3\n", StringComparison.Ordinal), 1),
            "startxref past what a long holds" => (
                "JUNK\n" + sound.Replace(Tail, "startxref\n9223372036854775807\n", StringComparison.Ordinal),
                1),
            "first table without a trailer" => (
                sound.Replace(Tail, "startxref\n{xref:1}\n%%EOF\nstartxref\n{xref:2}\n", StringComparison.Ordinal) +
                "xref\n0 1\n{free}\n2 2\n{row:2}\n{row:3}\n",
                1),
            "entries only in the older trailer" => (
                sound + """
                    4 0 obj
                    (an update)
                    endobj
                    xref
                    4 1
                    {row:4}
                    trailer
                    << /Size 5 /Root 1 0 R /Prev {xref:1} >>
                    startxref
                    999999
                    %%EOF

                    """,
                2),
            "first section a cross-reference stream that cannot be read" => (
                sound + """
                    4 0 obj
                    << /Type /XRef /Size 5 /Root 1 0 R /W [1 2] /Length 3 >>
                    stream
                    abc
                    endstream
                    endobj
                    startxref
                    {off:4}
                    %%EOF

                    """,
                1),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "No such shape."),
        };

        var file = PdfTemplate.Build(template);
        var text = Encoding.Latin1.GetString(file);
        text.Should().Contain(entries);

        return (file, PdfTemplate.OffsetOf(file, "trailer\n", occurrence));
    }
}
