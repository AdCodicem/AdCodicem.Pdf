namespace AdCodicem.Pdf.IO.XRef;

/// <summary>What came of reading an object stream's header for the validation rules.</summary>
internal enum ObjectStreamHeaderResult : byte
{
    /// <summary>The header was read: it lists the objects the stream holds.</summary>
    Read,

    /// <summary>The stream is neither where the index places it nor near it: its own entry is broken.</summary>
    NotFound,

    /// <summary>What the index places there is no object stream.</summary>
    NotAnObjectStream,

    /// <summary>It is an object stream, and its header cannot be read from what it decodes to.</summary>
    Unreadable,

    /// <summary>One of the reader's limits stopped it reading the stream, its dictionary or its data, before its header ended.</summary>
    CutByLimit,
}
