namespace AdCodicem.Pdf.IO;

/// <summary>What the bytes after a stream's data say of its end (<see cref="StreamEnds.ReadEnd"/>).</summary>
/// <param name="Kind">Whether <c>endstream</c> follows the data, does not, or the bytes cannot say.</param>
/// <param name="EndObj">What follows the <c>endstream</c>, when one does.</param>
/// <param name="KeywordEnd">Where the <c>endstream</c> token ends in the bytes, when one does.</param>
internal readonly record struct StreamEnd(StreamEndKind Kind, EndObjState EndObj = EndObjState.Unseen, int KeywordEnd = 0)
{
    /// <summary>No <c>endstream</c> follows the data.</summary>
    public static StreamEnd None => new(StreamEndKind.None);

    /// <summary>The bytes end in the white space, the comment or the keyword that may lead to <c>endstream</c>.</summary>
    public static StreamEnd Undecided => new(StreamEndKind.Undecided);
}
