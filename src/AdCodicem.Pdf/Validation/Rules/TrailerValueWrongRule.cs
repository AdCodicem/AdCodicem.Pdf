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
                judge.Direct(trailer, PdfName.Type, "7.5.8.2");
                judge.DirectIntegers(trailer, PdfName.W);
                judge.DirectIntegers(trailer, PdfName.Index);
                judge.Offset(trailer, PdfName.Prev, IsFollowed(sections, section, PdfName.Prev), direct: true, "Table 17");
                judge.Length(trailer, pdf20);
                judge.Decoding(trailer, pdf20);
            }
            else
            {
                judge.Offset(trailer, PdfName.Prev, IsFollowed(sections, section, PdfName.Prev), direct: true, "Table 15");
                judge.Offset(trailer, PdfName.XRefStm, IsFollowed(sections, section, PdfName.XRefStm), direct: false, "Table 19");
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

        /// <summary>Reports <paramref name="key"/> written as a reference, where ISO 32000-1 makes it direct.</summary>
        public void Direct(PdfDictionary trailer, PdfName key, string clause)
        {
            if (trailer.GetRaw(key) is PdfReference reference)
            {
                Indirect("its " + FileQuote.Name(key), reference, "ISO 32000-1", clause);
            }
        }

        /// <summary>
        /// Reports an array of integers — <c>/W</c>, <c>/Index</c> — written as a reference, and each element written as
        /// one or as a real, the array an indirect one leads to included.
        /// </summary>
        public void DirectIntegers(PdfDictionary trailer, PdfName key)
        {
            var name = FileQuote.Name(key);
            var value = trailer.GetRaw(key);

            if (value is PdfReference reference)
            {
                Indirect("its " + name, reference, "ISO 32000-1", "7.5.8.2");
            }

            if (value?.Resolve() is not PdfArray array)
            {
                return;
            }

            for (var i = 0; i < array.Count; i++)
            {
                var element = array[i];
                var what = string.Create(CultureInfo.InvariantCulture, $"element {i} of its {name}");

                if (element is PdfReference indirect)
                {
                    Indirect(what, indirect, "ISO 32000-1", "7.5.8.2");
                }

                if (element.Resolve() is PdfReal real)
                {
                    Real(what, real, "Table 17");
                }
            }
        }

        /// <summary>
        /// Reports a <c>/Prev</c> or an <c>/XRefStm</c> the chain went on through, written as a reference where the
        /// specification makes it direct, or as a real.
        /// </summary>
        public void Offset(PdfDictionary trailer, PdfName key, bool followed, bool direct, string table)
        {
            if (!followed || trailer.GetRaw(key) is not { } value)
            {
                return;
            }

            var what = "its " + FileQuote.Name(key);

            if (value is PdfReference reference && direct)
            {
                Indirect(what, reference, "ISO 32000-1", table);
            }

            if (value.Resolve() is PdfReal real)
            {
                Real(what, real, table);
            }
        }

        /// <summary>
        /// Reports a cross-reference stream's <c>/Length</c> written as a real, or, in a file that declares PDF 2.0, as a
        /// reference.
        /// </summary>
        public void Length(PdfDictionary trailer, bool pdf20)
        {
            var value = trailer.GetRaw(PdfName.Length);

            if (value is PdfReference reference && pdf20)
            {
                Indirect("its /Length", reference, "ISO 32000-2", "7.5.8.2");
            }

            if (value?.Resolve() is PdfReal real)
            {
                Real("its /Length", real, "Table 5");
            }
        }

        /// <summary>
        /// Reports a cross-reference stream's <c>/Filter</c> or <c>/DecodeParms</c> written as a reference, and, in a file
        /// that declares PDF 2.0, each element and entry written as one; and the integers of Table 8 written as reals.
        /// </summary>
        public void Decoding(PdfDictionary trailer, bool pdf20)
        {
            foreach (var key in (ReadOnlySpan<PdfName>)[PdfName.Filter, PdfName.DecodeParms])
            {
                var name = FileQuote.Name(key);
                var value = trailer.GetRaw(key);

                if (value is PdfReference reference)
                {
                    Indirect("its " + name, reference, "ISO 32000-1", "7.5.8.2");
                }

                switch (value?.Resolve())
                {
                    case PdfArray array:
                        for (var i = 0; i < array.Count; i++)
                        {
                            var what = string.Create(CultureInfo.InvariantCulture, $"element {i} of its {name}");

                            if (array[i] is PdfReference element && pdf20)
                            {
                                Indirect(what, element, "ISO 32000-2", "7.5.8.2");
                            }

                            if (array[i].Resolve() is PdfDictionary parameters)
                            {
                                Parameters(parameters, what, pdf20);
                            }
                        }

                        break;

                    case PdfDictionary parameters:
                        Parameters(parameters, "its " + name, pdf20);
                        break;
                }
            }
        }

        private void Parameters(PdfDictionary parameters, string where, bool pdf20)
        {
            foreach (var (key, value) in parameters)
            {
                var what = FileQuote.Name(key) + " of " + where;

                if (value is PdfReference reference && pdf20)
                {
                    Indirect(what, reference, "ISO 32000-2", "7.5.8.2");
                }

                if (Array.IndexOf(IntegerParameters, key) >= 0 && value.Resolve() is PdfReal real)
                {
                    Real(what, real, "Table 8");
                }
            }
        }

        private void Indirect(string what, PdfReference reference, string standard, string clause) => Report(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Subject} writes {what} as the reference {reference.Id.Number} {reference.Id.Generation} R, where {standard} makes it direct ({clause}); the reader read it where a section read before it places the object."),
            "Write the value itself in place of the reference.");

        /// <summary>
        /// Reports a value Table 5, 8, 15 or 17 makes an integer, written as a real with no fractional part, which the
        /// reader reads as the integer it equals. One with a fractional part is not read so (ADR 45: another fault).
        /// </summary>
        private void Real(string what, PdfReal real, string table)
        {
            if (real.AsInteger() is not null)
            {
                Report(
                    $"{Subject} gives {what} as a real number, where {table} of ISO 32000-1 asks for an integer; the reader read it as the integer it equals.",
                    "Write the value as an integer.");
            }
        }

        private void Report(string message, string remedy) =>
            context.Report(rule, PdfValidationLocation.AtPosition(section.TrailerLocation), message, remedy);
    }
}
