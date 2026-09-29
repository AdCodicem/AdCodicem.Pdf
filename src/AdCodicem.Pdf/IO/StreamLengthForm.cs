namespace AdCodicem.Pdf.IO;

/// <summary>How a stream's <c>/Length</c> was written, or what the object it names holds.</summary>
internal enum StreamLengthForm : byte
{
    /// <summary>An integer from 0 to <see cref="int.MaxValue"/>, which the parser can take as a length.</summary>
    Integer,

    /// <summary>No <c>/Length</c> at all.</summary>
    Absent,

    /// <summary>A value that is not an integer: a real number with a fraction, a name, a string, a dictionary…</summary>
    NotAnInteger,

    /// <summary>An integer below zero, or above <see cref="int.MaxValue"/>, which no stream the reader reads can have.</summary>
    OutOfRange,

    /// <summary>A reference to an object the file lacks: the object reads as null (ISO 32000-1, 7.3.10).</summary>
    ObjectMissing,

    /// <summary>
    /// A reference to an object the reader could not produce: the index holds it and it could not be read, or it
    /// could not be read from where the <c>/Length</c> was — the stream itself, an object stream being decoded.
    /// </summary>
    ObjectUnreadable,
}
