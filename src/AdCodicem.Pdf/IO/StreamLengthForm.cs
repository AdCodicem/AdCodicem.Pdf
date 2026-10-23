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

    /// <summary>
    /// A reference read while the cross-reference chain is read, to an object no section read so far places where it can
    /// be read without loading it: the data is taken up to its <c>endstream</c>, nothing is reported, and the length is
    /// read and checked once the chain is read (#182).
    /// </summary>
    Deferred,

    /// <summary>
    /// A reference to an object one of the reader's guards left out of the index, or that a chain of references from it
    /// leads to: whether the file holds it is unknown. The data ends where its <c>endstream</c> ends it, or as a stream with
    /// none ends; nothing is reported of the length, whose fault is the guard's, which reported it, and a missing
    /// <c>endstream</c> is still the file's (#224) — but where the search stops at an object header the data spells,
    /// or at an <c>endstream</c> its object's <c>endobj</c> does not follow, which only the length could tell from the
    /// end of the data: the data up to there is then the guard's.
    /// </summary>
    ObjectUnindexed,

    /// <summary>
    /// A reference to an object one of the reader's guards cut, or that a chain of references from it leads to: what it
    /// holds may lie past the cut. An integer read before the cut is only a place to look for <c>endstream</c> at, and
    /// never kept as a length; the data is otherwise taken up to its <c>endstream</c>. Nothing is reported of the length,
    /// whose fault is the guard's, which reported it; a missing <c>endstream</c> is still the file's (#224), but where
    /// the search stops as <see cref="ObjectUnindexed"/> says.
    /// </summary>
    ObjectCut,
}
