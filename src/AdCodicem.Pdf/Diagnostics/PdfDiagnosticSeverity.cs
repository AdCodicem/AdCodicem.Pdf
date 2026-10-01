namespace AdCodicem.Pdf.Diagnostics;

/// <summary>How much a diagnostic entry should worry the caller.</summary>
public enum PdfDiagnosticSeverity
{
    /// <summary>Something worth knowing that changes nothing.</summary>
    Information,

    /// <summary>The file was not conforming and the reader worked around it.</summary>
    Repair,

    /// <summary>Something is wrong and the result may not be what the caller expects.</summary>
    Warning,

    /// <summary>A conformance guarantee such as PDF/A or PDF/UA was lost by an operation.</summary>
    ConformanceLoss,
}
