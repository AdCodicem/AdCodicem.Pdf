using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO.Filters;

/// <summary>A filter of a chain as decoding runs it.</summary>
/// <param name="Index">Where in the chain the filter stands.</param>
/// <param name="Name">The filter.</param>
/// <param name="Parameters">The values of its parameters, when it is a Flate or LZW filter.</param>
internal readonly record struct FilterStep(int Index, PdfName Name, FilterParameters Parameters);
