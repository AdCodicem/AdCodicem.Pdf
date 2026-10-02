using System.Globalization;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.FileTrailerValueWrong"/>: the values a section's trailer — a classic table's, or a
/// cross-reference stream's dictionary — gives the reader to read the chain are written as the specification asks:
/// direct where it makes them direct, and integers where it makes them integers.
/// </summary>
/// <remarks>
/// <para>
/// A cross-reference stream's <c>/Type</c>, <c>/Index</c>, <c>/Prev</c> and <c>/W</c> are direct, the elements of
/// <c>/Index</c> and <c>/W</c> with them, and so are its <c>/Filter</c> and <c>/DecodeParms</c> (ISO 32000-1, 7.5.8.2).
/// ISO 32000-2 adds, through an erratum the PDF Association approved (pdf-issues #246), its <c>/Length</c> and every
/// element and entry of its <c>/Filter</c> and <c>/DecodeParms</c>: judged in a file that declares PDF 2.0. A classic
/// trailer's <c>/Prev</c> is a direct integer (Table 15); its <c>/XRefStm</c> may be indirect (Table 19). Where Table 5,
/// Table 8, Table 15 or Table 17 make a value an integer, a real with no fractional part is reported.
/// </para>
/// <para>
/// The reader reads such a value all the same, as other readers do — a reference where a section already read places
/// its object, a real with no fractional part as the integer it equals —: a warning (ADR 45). A value it could not read
/// so — a reference no section read before it places, a real with a fractional part — is another fault, the section's
/// (<see cref="PdfValidationRuleIds.XRefSectionMalformed"/>, <see cref="PdfValidationRuleIds.XRefSectionNotFound"/>) or
/// the stream's; a <c>/Size</c> is <see cref="PdfValidationRuleIds.FileSizeWrong"/>'s. Only the sections the reader read
/// are judged.
/// </para>
/// </remarks>
internal sealed class TrailerValueWrongRule : IValidationRule
{
    /// <summary>What makes a cross-reference stream's own values direct.</summary>
    private const string Iso1 = "ISO 32000-1 makes it direct (7.5.8.2)";

    /// <summary>What makes, in a file that declares PDF 2.0, the rest of a cross-reference stream's values direct.</summary>
    private const string Iso2 = "ISO 32000-2 makes it direct (7.5.8.2)";

    /// <summary>The entries of a <c>/DecodeParms</c> dictionary that Table 8 makes integers.</summary>
    private static readonly PdfName[] IntegerParameters =
        [PdfName.Predictor, PdfName.Colors, PdfName.BitsPerComponent, PdfName.Columns, PdfName.EarlyChange];

    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.FileTrailerValueWrong;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var sections = context.Structure.Sections;
        var pdf20 = ArlingtonWalk.DeclaredVersion(context.Document) == 20;

        foreach (var section in sections)
        {
            if (section.State is not (XRefSectionState.Read or XRefSectionState.Relocated) || section.Trailer is not { } trailer)
            {
                continue;
            }

            var judge = new Judge(context, this, section);

            if (section.Kind == XRefSectionKind.Stream)
            {
                judge.Value("its /Type", trailer.GetRaw(PdfName.Type), Iso1, integer: null);
                judge.Integers(trailer, PdfName.W);
                judge.Integers(trailer, PdfName.Index);

                if (IsFollowed(sections, section, PdfName.Prev))
                {
                    judge.Value("its /Prev", trailer.GetRaw(PdfName.Prev), Iso1, "Table 17");
                }

                judge.Value("its /Length", trailer.GetRaw(PdfName.Length), pdf20 ? Iso2 : null, "Table 5");
                judge.Decoding(trailer, pdf20);
            }
            else
            {
                if (IsFollowed(sections, section, PdfName.Prev))
                {
                    judge.Value("its /Prev", trailer.GetRaw(PdfName.Prev), "ISO 32000-1 makes it direct (Table 15)", "Table 15");
                }

                if (IsFollowed(sections, section, PdfName.XRefStm))
                {
                    judge.Value("its /XRefStm", trailer.GetRaw(PdfName.XRefStm), direct: null, "Table 19");
                }
            }
        }
    }

    /// <summary>
    /// Determines whether the chain went on through the <paramref name="key"/> of <paramref name="section"/>: one that
    /// names no section is <see cref="PdfValidationRuleIds.XRefSectionNotFound"/>'s to report.
    /// </summary>
    private static bool IsFollowed(IReadOnlyList<XRefSectionRecord> sections, XRefSectionRecord section, PdfName key)
    {
        var naming = FileQuote.Name(key);

        foreach (var other in sections)
        {
            if (other.NamedOffset < 0 && other.NamedFrom == section.Offset && other.NamedBy == naming)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Judges the values of one section's trailer, and reports each written as the specification does not allow.</summary>
    private readonly struct Judge(ValidationContext context, IValidationRule rule, XRefSectionRecord section)
    {
        private string Subject => section.Kind == XRefSectionKind.Stream
            ? string.Create(CultureInfo.InvariantCulture, $"The cross-reference stream at offset {section.Offset}")
            : string.Create(CultureInfo.InvariantCulture, $"The trailer of the cross-reference table at offset {section.Offset}");

        /// <summary>
        /// Reports a value written as a reference where <paramref name="direct"/> says the specification makes it direct, or
        /// as a real with no fractional part where <paramref name="integer"/> names the table that makes it an integer — the
        /// value a reference leads to included —, in one finding.
        /// </summary>
        /// <param name="what">The value, as the message names it: <c>its /W</c>, <c>element 1 of its /W</c>.</param>
        /// <param name="value">The value as the file wrote it, or null when there is none.</param>
        /// <param name="direct">What makes the value direct, or null when nothing does.</param>
        /// <param name="integer">The table that makes the value an integer, or null when none does.</param>
        public void Value(string what, PdfObject? value, string? direct, string? integer)
        {
            var reference = direct is not null ? value as PdfReference : null;
            var real = integer is not null && value?.Resolve() is PdfReal written && written.AsInteger() is not null;

            if (reference is null && !real)
            {
                return;
            }

            var id = reference is null
                ? string.Empty
                : string.Create(CultureInfo.InvariantCulture, $"the reference {reference.Id.Number} {reference.Id.Generation} R");
            var message = (reference, real) switch
            {
                (not null, true) => $"{Subject} writes {what} as {id}, to a real number: {direct}, and {integer} of ISO 32000-1 an integer.",
                (not null, false) => $"{Subject} writes {what} as {id}, where {direct}.",
                _ => $"{Subject} gives {what} as a real number, where {integer} of ISO 32000-1 asks for an integer.",
            };

            context.Report(
                rule,
                PdfValidationLocation.AtPosition(section.TrailerLocation),
                message,
                reference is null ? "Write the value as an integer." : "Write the value itself in place of the reference.");
        }

        /// <summary>
        /// Reports an array of integers — <c>/W</c>, <c>/Index</c> — written as a reference, and each element written as
        /// one or as a real, the array a reference leads to included.
        /// </summary>
        public void Integers(PdfDictionary trailer, PdfName key)
        {
            var name = FileQuote.Name(key);
            var value = trailer.GetRaw(key);
            Value("its " + name, value, Iso1, integer: null);

            if (value?.Resolve() is not PdfArray array)
            {
                return;
            }

            for (var i = 0; i < array.Count; i++)
            {
                Value(string.Create(CultureInfo.InvariantCulture, $"element {i} of its {name}"), array[i], Iso1, "Table 17");
            }
        }

        /// <summary>
        /// Reports a cross-reference stream's <c>/Filter</c> or <c>/DecodeParms</c> written as a reference, and, in a file
        /// that declares PDF 2.0, each element and entry written as one; and the integers of Table 8 written as reals.
        /// </summary>
        public void Decoding(PdfDictionary trailer, bool pdf20)
        {
            var inside = pdf20 ? Iso2 : null;

            foreach (var key in (ReadOnlySpan<PdfName>)[PdfName.Filter, PdfName.DecodeParms])
            {
                var name = FileQuote.Name(key);
                var value = trailer.GetRaw(key);
                Value("its " + name, value, Iso1, integer: null);

                switch (value?.Resolve())
                {
                    case PdfArray array:
                        for (var i = 0; i < array.Count; i++)
                        {
                            var what = string.Create(CultureInfo.InvariantCulture, $"element {i} of its {name}");
                            Value(what, array[i], inside, integer: null);

                            if (array[i].Resolve() is PdfDictionary parameters)
                            {
                                Parameters(parameters, what, inside);
                            }
                        }

                        break;

                    case PdfDictionary parameters:
                        Parameters(parameters, "its " + name, inside);
                        break;
                }
            }
        }

        private void Parameters(PdfDictionary parameters, string where, string? inside)
        {
            foreach (var (key, value) in parameters)
            {
                Value(FileQuote.Name(key) + " of " + where, value, inside, Array.IndexOf(IntegerParameters, key) >= 0 ? "Table 8" : null);
            }
        }
    }
}
