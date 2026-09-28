namespace AdCodicem.Pdf.Validation;

/// <summary>Something <see cref="CrossReferenceProbe"/> found, for the rule it belongs to to report: where, and what.</summary>
/// <param name="Location">Where the finding applies.</param>
/// <param name="Message">What was found, in English: a whole sentence, or a fragment the rule builds one around.</param>
internal readonly record struct ProbeFinding(PdfValidationLocation Location, string Message);
