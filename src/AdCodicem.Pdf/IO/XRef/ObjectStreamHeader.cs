using System.Globalization;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO.XRef;

/// <summary>
/// The pairs an object stream's header lists — each member's object number and offset —, read alike by the reader, the
/// rebuild and the validator, up to the first that no member can be.
/// </summary>
internal readonly struct ObjectStreamHeader
{
    /// <summary>
    /// The pairs the tables hold before they grow. Not a bound: what a header of up to that many pairs costs is allocated
    /// once, exactly; a longer one grows the tables as its pairs are read, so that a <c>/N</c> no header holds allocates
    /// nothing for it (#160).
    /// </summary>
    private const int FirstCapacity = 1024;

    private ObjectStreamHeader(int[] numbers, int[] offsets, ObjectStreamHeaderFault fault, long faultNumber, long faultOffset)
    {
        Numbers = numbers;
        Offsets = offsets;
        Fault = fault;
        FaultNumber = faultNumber;
        FaultOffset = faultOffset;
    }

    /// <summary>Gets the object numbers of the pairs read, in order.</summary>
    public int[] Numbers { get; }

    /// <summary>Gets the offsets of the pairs read, from <c>/First</c>, in order and increasing.</summary>
    public int[] Offsets { get; }

    /// <summary>Gets how many pairs were read.</summary>
    public int Count => Numbers.Length;

    /// <summary>Gets what ended the header before its <c>/N</c>, if anything did.</summary>
    public ObjectStreamHeaderFault Fault { get; }

    /// <summary>Gets the object number of the pair that ended the header, for a pair no member can be.</summary>
    public long FaultNumber { get; }

    /// <summary>Gets the offset of the pair that ended the header, for a pair no member can be.</summary>
    public long FaultOffset { get; }

    /// <summary>
    /// Reads the pairs of a header of <paramref name="first"/> bytes that declares <paramref name="count"/> objects.
    /// </summary>
    /// <remarks>
    /// The header ends at the first token that is not an integer, and at the first pair no member can be: a number no
    /// object has, an offset past what an <c>int</c> holds or not past the one before it — the specification asks offsets
    /// to increase, which is what lets each member be read to the next one's start —, or a start at or past the end of
    /// the decoded data. The pairs before it are kept. A start past data a guard cut is the guard's, not the header's: the
    /// pair is kept, and the member is unread for the guard's reasons (#259). Each pair takes at least <c>0 0</c> and the
    /// blank after it, so a header lists at most a quarter of its bytes and one: the tables never grow past it, whatever
    /// <paramref name="count"/> says.
    /// </remarks>
    /// <param name="data">The stream's decoded data.</param>
    /// <param name="count">The objects its <c>/N</c> declares, positive.</param>
    /// <param name="first">Its <c>/First</c>, within <paramref name="data"/>.</param>
    /// <param name="cutByGuard">Whether a guard cut the data: its end is the limit's, not the file's.</param>
    public static ObjectStreamHeader Read(ReadOnlySpan<byte> data, long count, int first, bool cutByGuard)
    {
        var lexer = new PdfLexer(data[..first]);
        var room = Math.Min(count, ((long)first + 1) / 4);
        var capacity = (int)Math.Min(room, FirstCapacity);
        var numbers = new int[capacity];
        var offsets = new int[capacity];
        var fault = ObjectStreamHeaderFault.None;
        long faultNumber = 0;
        long faultOffset = 0;
        var read = 0;

        while (read < count)
        {
            var numberToken = lexer.Read();
            var offsetToken = lexer.Read();

            if (numberToken.Kind != PdfTokenKind.Integer || offsetToken.Kind != PdfTokenKind.Integer)
            {
                // A stray token is the fault whatever follows it; the header ends where the first missing token is.
                fault = numberToken.Kind == PdfTokenKind.EndOfInput ||
                    (numberToken.Kind == PdfTokenKind.Integer && offsetToken.Kind == PdfTokenKind.EndOfInput)
                    ? ObjectStreamHeaderFault.Ended
                    : ObjectStreamHeaderFault.Stray;
                break;
            }

            faultNumber = numberToken.Integer;
            faultOffset = offsetToken.Integer;

            // Narrowed, a number past an int would name another object, and an offset start the member elsewhere.
            fault = faultNumber is <= 0 or > PdfObjectId.MaxNumber || faultOffset is < 0 or > int.MaxValue
                ? ObjectStreamHeaderFault.Impossible
                : read > 0 && faultOffset <= offsets[read - 1]
                    ? ObjectStreamHeaderFault.NotIncreasing
                    : !cutByGuard && first + faultOffset >= data.Length
                        ? ObjectStreamHeaderFault.PastData
                        : ObjectStreamHeaderFault.None;

            if (fault != ObjectStreamHeaderFault.None)
            {
                break;
            }

            // A header holds at most room pairs, which the tables reach before they would need to grow past it.
            if (read == numbers.Length)
            {
                var grown = (int)Math.Min(2L * numbers.Length, room);
                Array.Resize(ref numbers, grown);
                Array.Resize(ref offsets, grown);
            }

            numbers[read] = (int)faultNumber;
            offsets[read] = (int)faultOffset;
            read++;
        }

        if (read != numbers.Length)
        {
            Array.Resize(ref numbers, read);
            Array.Resize(ref offsets, read);
        }

        return new ObjectStreamHeader(numbers, offsets, fault, faultNumber, faultOffset);
    }

    /// <summary>
    /// Says why the pair that ended the header can be no member, for a fault that names a pair: what follows its offset
    /// in a message.
    /// </summary>
    /// <param name="dataLength">How many bytes the stream decodes to.</param>
    public string Why(int dataLength) => Fault switch
    {
        ObjectStreamHeaderFault.NotIncreasing => string.Create(
            CultureInfo.InvariantCulture, $"not past the offset of the object before it, {Offsets[^1]}"),
        ObjectStreamHeaderFault.PastData => string.Create(
            CultureInfo.InvariantCulture, $"past what its {dataLength:N0} bytes of decoded data hold"),
        _ => "which no member can be",
    };
}
