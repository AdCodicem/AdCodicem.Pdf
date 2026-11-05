namespace AdCodicem.Pdf.IO.XRef;

/// <summary>
/// What the chain had found where a <c>/Prev</c> looped back to it (<see cref="FileStructure.LoopTarget"/>): the
/// section it read there, one it reached there and could not read, or only an offset it named (#188).
/// </summary>
internal enum XRefLoopTarget : byte
{
    /// <summary>
    /// An offset the chain only named: it read no section there, nor reached one past the white space there — one that
    /// holds nothing, in the file or past its end, or an object that only gives a <c>/W</c> and cannot be read.
    /// </summary>
    Named,

    /// <summary>
    /// A section the chain reached there and could not read, though it declares itself one, where it starts or where the
    /// chain found it moved: a cross-reference stream whose <c>/Prev</c> it went on through (#208), or a section an
    /// <c>/XRefStm</c> names, reported where it is.
    /// </summary>
    Reached,

    /// <summary>A section the chain read there, where it was named, or found moved.</summary>
    Read,
}
