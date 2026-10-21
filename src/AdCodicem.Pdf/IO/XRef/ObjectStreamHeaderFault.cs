namespace AdCodicem.Pdf.IO.XRef;

/// <summary>What ended an object stream's header before the count its <c>/N</c> declares.</summary>
internal enum ObjectStreamHeaderFault : byte
{
    /// <summary>The header lists every object its <c>/N</c> declares.</summary>
    None,

    /// <summary>The header ends: its data reaches <c>/First</c>, or an object number has no offset after it.</summary>
    Ended,

    /// <summary>The header holds something other than an object number and an offset.</summary>
    Stray,

    /// <summary>A pair names a number no object can have, or an offset no member can start at.</summary>
    Impossible,

    /// <summary>A pair's offset is not past the offset of the pair before it (ISO 32000-1 7.5.7).</summary>
    NotIncreasing,

    /// <summary>A pair's member would start at or past the end of the stream's decoded data.</summary>
    PastData,
}
