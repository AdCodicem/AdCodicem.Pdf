using System.Globalization;
using AdCodicem.Pdf.IO.XRef;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.FileTrailerMalformed"/>: a trailer is a well-formed dictionary.
/// </summary>
/// <remarks>
/// A trailer the <c>trailer</c> keyword does not introduce a dictionary to, or one read despite syntax errors — a
/// key that is not a name, a dictionary or a string left unclosed, a hexadecimal string holding bytes that are neither
/// digits nor white space,
/// a name whose number sign is no escape, a key given twice —, is salvaged as far as it goes: an entry may be lost with the syntax, or read
/// otherwise than its writer meant, and the reader cannot vouch that it read the trailer as written: an error (ADR 45). qpdf rejects such an
/// index outright. A cross-reference stream's dictionary is its trailer, and is held to the same.
/// </remarks>
internal sealed class TrailerMalformedRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.FileTrailerMalformed;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Error;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var section in context.Structure.Sections)
        {
            string message;

            if (section.TrailerFault == XRefTrailerFault.NotADictionary)
            {
                message = string.Create(
                    CultureInfo.InvariantCulture,
                    $"The trailer keyword at offset {section.TrailerPosition} is not followed by a dictionary.");
            }
            else if (section.TrailerFault == XRefTrailerFault.Malformed)
            {
                message = section.Kind == XRefSectionKind.Stream
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"The dictionary of the cross-reference stream at offset {section.Offset} is not well formed: the reader read it despite syntax errors.")
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"The trailer at offset {section.TrailerPosition} is not a well-formed dictionary: the reader read it despite syntax errors.");
            }
            else
            {
                continue;
            }

            context.Report(
                this,
                PdfValidationLocation.AtPosition(section.TrailerLocation),
                message,
                "Rewrite the trailer as a well-formed dictionary.");
        }
    }
}
