using System.Globalization;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO;

internal sealed partial class PdfFileReader
{
    /// <summary>Whether the document was found encrypted as it opened (<see cref="DecideEncryption"/>).</summary>
    private bool _encrypted;

    /// <summary>Whether <see cref="DecideEncryption"/> ran: what a rebuild merges afterward changes nothing of it.</summary>
    private bool _encryptionDecided;

    /// <summary>Whether the trailer the document opened with gave <c>/Encrypt</c>, whatever it resolved to.</summary>
    private bool _encryptGivenAtOpening;

    /// <summary>
    /// Gets a value indicating whether the document was found encrypted as it opened: its trailer's <c>/Encrypt</c>
    /// resolved to a dictionary (<see cref="DecideEncryption"/>).
    /// </summary>
    public bool IsEncrypted => _encrypted;

    /// <summary>
    /// Gets a value indicating whether a rebuild after opening merged into the trailer an <c>/Encrypt</c> the trailer the
    /// document opened with did not give (<see cref="PdfDiagnosticCodes.TrailerEncryptLate"/>): the document is read as it
    /// opened, and what an encrypted file holds may be encrypted.
    /// </summary>
    public bool EncryptFoundAfterOpening =>
        _encryptionDecided && !_encryptGivenAtOpening && Trailer.ContainsKey(PdfName.Encrypt);

    /// <summary>
    /// Decides, once, as the document opens, whether it is encrypted: its trailer's <c>/Encrypt</c> resolves to a dictionary,
    /// or names an object one of the reader's guards kept it from reading. One that names an object the file lacks, or one
    /// that is null, or that is a value of another type, leaves the document unencrypted, and is reported
    /// (<see cref="PdfDiagnosticCodes.TrailerEncryptInvalid"/>, #154).
    /// </summary>
    /// <remarks>
    /// Loading the object <c>/Encrypt</c> names may rebuild the index, its entry broken, as the document opens: the trailer
    /// the rebuild merges is read again, once, since a document is rebuilt once. A guard that load reaches, the document
    /// opened to throw on one, leaves opening. What a rebuild after this merges is reported, not followed
    /// (<see cref="PdfDiagnosticCodes.TrailerEncryptLate"/>).
    /// </remarks>
    internal void DecideEncryption()
    {
        var repaired = _repaired;
        var resolved = ResolveEncrypt(out var named);

        if (!repaired && _repaired)
        {
            resolved = ResolveEncrypt(out named);
        }

        // An object a guard kept the reader from reading is the reader's limit, not the file's fault: the file's /Encrypt
        // stands, unread.
        _encryptGivenAtOpening = Trailer.ContainsKey(PdfName.Encrypt);
        _encrypted = resolved is PdfDictionary || (named > 0 && resolved is PdfNull && IsLeftByGuard(named));
        _encryptionDecided = true;

        if (!_encryptGivenAtOpening || _encrypted)
        {
            return;
        }

        var what = named > 0
            ? string.Create(CultureInfo.InvariantCulture, $"names object {named}, which reads as {PdfObjectParser.KindOf(resolved)}")
            : "is " + PdfObjectParser.KindOf(resolved);
        _diagnostics.Warn(
            PdfDiagnosticCodes.TrailerEncryptInvalid,
            $"The trailer's /Encrypt {what}, not an encryption dictionary; the document is read as unencrypted.",
            TrailerLocation);
    }

    /// <summary>
    /// Reports that the trailer a rebuild after opening merged gives an <c>/Encrypt</c> the trailer the document opened with
    /// did not (<see cref="PdfDiagnosticCodes.TrailerEncryptLate"/>); nothing before the document has opened.
    /// </summary>
    private void ReportEncryptFoundLate()
    {
        if (EncryptFoundAfterOpening)
        {
            _diagnostics.Warn(
                PdfDiagnosticCodes.TrailerEncryptLate,
                "The trailer the rebuilt index merged gives /Encrypt, which the trailer the document opened with did not; the document is still read as unencrypted, as it opened.",
                TrailerLocation);
        }
    }

    /// <summary>
    /// Resolves the trailer's <c>/Encrypt</c>: null when the trailer gives none, and the number of the object it names, or
    /// 0, in <paramref name="named"/>.
    /// </summary>
    private PdfObject? ResolveEncrypt(out int named)
    {
        var value = Trailer.GetRaw(PdfName.Encrypt);
        named = 0;

        if (value is PdfReference reference)
        {
            named = reference.Id.Number;
            return GetObject(reference.Id);
        }

        return value;
    }
}
