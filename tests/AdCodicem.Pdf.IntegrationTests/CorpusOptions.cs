using AdCodicem.Pdf.Documents;

namespace AdCodicem.Pdf.IntegrationTests;

/// <summary>How the integration tests open a corpus document.</summary>
internal static class CorpusOptions
{
    /// <summary>
    /// Gives the options <paramref name="entry"/> is opened with: the reader limits its manifest entry raises, and an
    /// encrypted document opened rather than refused — its structure is readable without its key.
    /// </summary>
    public static PdfReaderOptions For(CorpusDocument entry)
    {
        var limits = PdfReaderLimits.Default;

        if (entry.ReaderLimits is { } raised)
        {
            limits = limits with
            {
                MaxDecodedStreamLength = raised.MaxDecodedStreamLength ?? limits.MaxDecodedStreamLength,
                MaxStreamLength = raised.MaxStreamLength ?? limits.MaxStreamLength,
                MaxObjectLength = raised.MaxObjectLength ?? limits.MaxObjectLength,
                MaxXRefSectionLength = raised.MaxXRefSectionLength ?? limits.MaxXRefSectionLength,
                MaxXRefSectionCount = raised.MaxXRefSectionCount ?? limits.MaxXRefSectionCount,
                MaxTrailerLength = raised.MaxTrailerLength ?? limits.MaxTrailerLength,
                MaxIndexEntries = raised.MaxIndexEntries ?? limits.MaxIndexEntries,
                MaxFilterChainLength = raised.MaxFilterChainLength ?? limits.MaxFilterChainLength,
            };
        }

        return new PdfReaderOptions { Limits = limits, ThrowOnEncrypted = false };
    }
}
