namespace AdCodicem.Pdf.Validation;

/// <summary>What keeps an object stream from serving the objects the index places in it (<see cref="BrokenObjectStream"/>).</summary>
internal enum BrokenObjectStreamKind : byte
{
    /// <summary>The index holds no object of its number in use.</summary>
    NotHeld,

    /// <summary>The index places it in an object stream in turn, which cannot hold one.</summary>
    InAnotherStream,

    /// <summary>The object is not an object stream, as the reader's fault says.</summary>
    NotAnObjectStream,

    /// <summary>Its header cannot be read, from its first entry on.</summary>
    Unreadable,

    /// <summary>Its header breaks after listing some objects, and some the index places in it are not among them.</summary>
    ReadInPart,
}
