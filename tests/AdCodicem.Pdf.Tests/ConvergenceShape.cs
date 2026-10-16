using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// A file crafted so that what validating it found once depended on what was read before, the reader limits it is opened
/// under, and the reads that once made the difference.
/// </summary>
/// <param name="Name">What the file is, in a few words, unique among the shapes.</param>
/// <param name="File">The file.</param>
/// <param name="Limits">The reader limits it is opened under.</param>
/// <param name="Reads">Each way of reading it before validating, as the objects read, in order.</param>
internal sealed record ConvergenceShape(string Name, byte[] File, PdfReaderLimits Limits, PdfObjectId[][] Reads);
