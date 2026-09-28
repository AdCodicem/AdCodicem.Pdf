// The encoding of the Arlington tables, shared by the core, which reads them, and by the generator in
// tools/AdCodicem.Pdf.Arlington, which writes them: the generator compiles this very file, so writer and reader of
// the tables cannot drift. It holds data definitions only, and nothing either side could not compile.

namespace AdCodicem.Pdf.Validation.Arlington;

/// <summary>
/// One of the Arlington model's 18 types, numbered in the model's own order: a row lists its types sorted, so the
/// first type of a row that accepts a value is the one with the lowest number.
/// </summary>
internal enum ArlingtonType : byte
{
    /// <summary><c>array</c>.</summary>
    Array = 0,

    /// <summary><c>bitmask</c>: an integer read as bits.</summary>
    Bitmask = 1,

    /// <summary><c>boolean</c>.</summary>
    Boolean = 2,

    /// <summary><c>date</c>: a string holding a date.</summary>
    Date = 3,

    /// <summary><c>dictionary</c>.</summary>
    Dictionary = 4,

    /// <summary><c>integer</c>.</summary>
    Integer = 5,

    /// <summary><c>matrix</c>: an array of six numbers.</summary>
    Matrix = 6,

    /// <summary><c>name</c>.</summary>
    Name = 7,

    /// <summary><c>name-tree</c>: a dictionary at the root of a name tree.</summary>
    NameTree = 8,

    /// <summary><c>null</c>.</summary>
    Null = 9,

    /// <summary><c>number</c>: an integer or a real.</summary>
    Number = 10,

    /// <summary><c>number-tree</c>: a dictionary at the root of a number tree.</summary>
    NumberTree = 11,

    /// <summary><c>rectangle</c>: an array of four numbers.</summary>
    Rectangle = 12,

    /// <summary><c>stream</c>.</summary>
    Stream = 13,

    /// <summary><c>string</c>.</summary>
    String = 14,

    /// <summary><c>string-ascii</c>.</summary>
    StringAscii = 15,

    /// <summary><c>string-byte</c>.</summary>
    StringByte = 16,

    /// <summary><c>string-text</c>.</summary>
    StringText = 17,
}

/// <summary>A set of <see cref="ArlingtonType"/>: bit <c>n</c> stands for the type numbered <c>n</c>.</summary>
[Flags]
internal enum ArlingtonTypes : uint
{
    /// <summary>No type.</summary>
    None = 0,

    /// <summary><c>array</c>.</summary>
    Array = 1u << (int)ArlingtonType.Array,

    /// <summary><c>bitmask</c>.</summary>
    Bitmask = 1u << (int)ArlingtonType.Bitmask,

    /// <summary><c>boolean</c>.</summary>
    Boolean = 1u << (int)ArlingtonType.Boolean,

    /// <summary><c>date</c>.</summary>
    Date = 1u << (int)ArlingtonType.Date,

    /// <summary><c>dictionary</c>.</summary>
    Dictionary = 1u << (int)ArlingtonType.Dictionary,

    /// <summary><c>integer</c>.</summary>
    Integer = 1u << (int)ArlingtonType.Integer,

    /// <summary><c>matrix</c>.</summary>
    Matrix = 1u << (int)ArlingtonType.Matrix,

    /// <summary><c>name</c>.</summary>
    Name = 1u << (int)ArlingtonType.Name,

    /// <summary><c>name-tree</c>.</summary>
    NameTree = 1u << (int)ArlingtonType.NameTree,

    /// <summary><c>null</c>.</summary>
    Null = 1u << (int)ArlingtonType.Null,

    /// <summary><c>number</c>.</summary>
    Number = 1u << (int)ArlingtonType.Number,

    /// <summary><c>number-tree</c>.</summary>
    NumberTree = 1u << (int)ArlingtonType.NumberTree,

    /// <summary><c>rectangle</c>.</summary>
    Rectangle = 1u << (int)ArlingtonType.Rectangle,

    /// <summary><c>stream</c>.</summary>
    Stream = 1u << (int)ArlingtonType.Stream,

    /// <summary><c>string</c>.</summary>
    String = 1u << (int)ArlingtonType.String,

    /// <summary><c>string-ascii</c>.</summary>
    StringAscii = 1u << (int)ArlingtonType.StringAscii,

    /// <summary><c>string-byte</c>.</summary>
    StringByte = 1u << (int)ArlingtonType.StringByte,

    /// <summary><c>string-text</c>.</summary>
    StringText = 1u << (int)ArlingtonType.StringText,

    /// <summary>Every type the model has.</summary>
    All = (1u << 18) - 1,

    /// <summary>The types whose values a row's <c>Link</c> column may name objects for.</summary>
    Linkable = Array | Dictionary | Stream | NameTree | NumberTree,
}

/// <summary>Whether a row's key is required, as its <c>Required</c> column says once reduced.</summary>
internal enum ArlingtonRequirement : byte
{
    /// <summary><c>FALSE</c>.</summary>
    No = 0,

    /// <summary><c>TRUE</c>: required from the key's ISO <c>SinceVersion</c> on, and never for an extension-only key.</summary>
    Yes = 1,

    /// <summary>
    /// A predicate on the version alone (<c>fn:IsPDFVersion</c>, <c>fn:BeforeVersion</c>, <c>fn:SinceVersion</c>),
    /// reduced to the versions from <see cref="ArlingtonLayout.ExtraRequiredFrom"/> included to
    /// <see cref="ArlingtonLayout.ExtraRequiredBefore"/> excluded.
    /// </summary>
    InVersions = 2,

    /// <summary>Any other predicate. It is not evaluated, so the key is never reported missing.</summary>
    Conditional = 3,
}

/// <summary>The flags byte of a row.</summary>
[Flags]
internal enum ArlingtonRowFlags : byte
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>The two bits that hold the row's <see cref="ArlingtonRequirement"/>.</summary>
    RequirementMask = 0b11,

    /// <summary>The <c>Inheritable</c> column is <c>TRUE</c>.</summary>
    Inheritable = 1 << 2,

    /// <summary>The key is <c>*</c>: any other key of a dictionary, any other element of an array.</summary>
    Wildcard = 1 << 3,

    /// <summary>The key is <c>N*</c>: a member of an array's repeating group.</summary>
    Repeating = 1 << 4,

    /// <summary>
    /// The key points back up the object graph (a <c>/Parent</c>, the <c>/P</c> of a structure element or an
    /// annotation, an outline item's <c>/Prev</c>): the walk follows it but types nothing through it.
    /// </summary>
    BackLink = 1 << 5,

    /// <summary>
    /// The key of a dictionary is one of those that tell an object's type apart (<c>Type</c>, <c>Subtype</c>,
    /// <c>S</c>, <c>FunctionType</c>, <c>ShadingType</c>, <c>PatternType</c>, <c>HalftoneType</c>, <c>FT</c>,
    /// <c>TransformMethod</c>, <c>CFM</c>), which weigh more when candidates are scored.
    /// </summary>
    Discriminator = 1 << 6,

    /// <summary>The row has an entry in the extras table: a version range, or an override the walk applies.</summary>
    HasExtras = 1 << 7,
}

/// <summary>
/// What <c>tools/AdCodicem.Pdf.Arlington/overrides.tsv</c> asks of the walk for one row, beyond the edits the
/// generator makes to the tables themselves.
/// </summary>
[Flags]
internal enum ArlingtonOverride : byte
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary><c>object.key-missing</c> stays silent on this key: a hand-written rule reports the fault.</summary>
    KeyMissingSilent = 1 << 0,

    /// <summary><c>object.value-type-wrong</c> stays silent on this key: a hand-written rule reports the fault.</summary>
    ValueTypeWrongSilent = 1 << 1,

    /// <summary><c>object.type-value-wrong</c> stays silent on this key: a hand-written rule reports the fault.</summary>
    TypeValueWrongSilent = 1 << 2,

    /// <summary><c>object.key-deprecated</c> stays silent on this key: a hand-written rule reports the fault.</summary>
    KeyDeprecatedSilent = 1 << 3,

    /// <summary>
    /// The key is also inherited from the interactive form dictionary's <c>/DA</c>, the document-wide default of
    /// a variable-text field's <c>/DA</c> (ISO 32000-1, 12.7.2, Table 218), which the model does not encode.
    /// </summary>
    InheritsAcroFormDefaultAppearance = 1 << 4,

    /// <summary>
    /// The silences above hold only where the page tree's walk judged the object — a page or a node the tree lists,
    /// one of its arrays of kids, a <c>/Parent</c> it could compare with the node listing the object —: the rule that
    /// covers them is one of the <c>page-tree</c> family, which reports the fault there and nowhere else.
    /// </summary>
    SilentWherePageTreeJudges = 1 << 5,
}

/// <summary>
/// The versions the tables hold, one byte each: ten times the major version plus the minor one, so that bytes
/// compare as versions do (<c>1.7</c> is 17, <c>2.0</c> is 20).
/// </summary>
internal static class ArlingtonVersion
{
    /// <summary>No version: no deprecation, or no lower bound of a range.</summary>
    public const byte None = 0;

    /// <summary>A <c>SinceVersion</c> that names an extension only: the key belongs to no ISO version.</summary>
    public const byte ExtensionOnly = 0xFF;

    /// <summary>No upper bound of a range.</summary>
    public const byte Unbounded = 0xFF;

    /// <summary>
    /// Reads one of the nine versions ISO 32000 defines, <c>1.0</c> to <c>1.7</c> and <c>2.0</c>, exactly as the
    /// model writes them.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> text, out byte version)
    {
        version = None;

        if (text.Length != 3 || text[1] != '.' || !char.IsAsciiDigit(text[0]) || !char.IsAsciiDigit(text[2]))
        {
            return false;
        }

        var code = (byte)(((text[0] - '0') * 10) + (text[2] - '0'));

        if (code is < 10 or > 17 and not 20)
        {
            return false;
        }

        version = code;
        return true;
    }

    /// <summary>Writes a version as the model does: <c>1.7</c>.</summary>
    public static string Format(byte version) => version switch
    {
        None => "none",
        ExtensionOnly => "extension",
        _ => string.Create(3, version, static (span, v) =>
        {
            span[0] = (char)('0' + (v / 10));
            span[1] = '.';
            span[2] = (char)('0' + (v % 10));
        }),
    };
}

/// <summary>The names the model gives its types.</summary>
internal static class ArlingtonTypeNames
{
    /// <summary>The model's name for <paramref name="type"/>: <c>name-tree</c>.</summary>
    public static string Of(ArlingtonType type) => type switch
    {
        ArlingtonType.Array => "array",
        ArlingtonType.Bitmask => "bitmask",
        ArlingtonType.Boolean => "boolean",
        ArlingtonType.Date => "date",
        ArlingtonType.Dictionary => "dictionary",
        ArlingtonType.Integer => "integer",
        ArlingtonType.Matrix => "matrix",
        ArlingtonType.Name => "name",
        ArlingtonType.NameTree => "name-tree",
        ArlingtonType.Null => "null",
        ArlingtonType.Number => "number",
        ArlingtonType.NumberTree => "number-tree",
        ArlingtonType.Rectangle => "rectangle",
        ArlingtonType.Stream => "stream",
        ArlingtonType.String => "string",
        ArlingtonType.StringAscii => "string-ascii",
        ArlingtonType.StringByte => "string-byte",
        ArlingtonType.StringText => "string-text",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not an Arlington type."),
    };

    /// <summary>Reads the model's name of a type.</summary>
    public static bool TryParse(ReadOnlySpan<char> name, out ArlingtonType type)
    {
        for (var candidate = ArlingtonType.Array; candidate <= ArlingtonType.StringText; candidate++)
        {
            if (name.SequenceEqual(Of(candidate)))
            {
                type = candidate;
                return true;
            }
        }

        type = default;
        return false;
    }
}

/// <summary>
/// The layout of the tables in <c>ArlingtonModel.g.cs</c>: every table is a span of bytes (or of <see cref="ushort"/>
/// for the name offsets and the candidate sets' members), little-endian, with fixed-size records.
/// </summary>
/// <remarks>
/// <para>
/// <b>Names</b> is every distinct string, ASCII: first the objects' names in ordinal order, so that an object's
/// number is also its name's, then the keys and values in ordinal order. <b>NameStarts</b> holds where each name
/// starts, plus the end of the last one.
/// </para>
/// <para>
/// <b>Objects</b>, one record per object in ordinal order of their names. <b>Rows</b>, one record per row: a
/// dictionary's rows in ordinal order of their keys, so that a key is found by binary search; an array's in the
/// model's order, its fixed elements first, then its repeating group, then its wildcard. A row holds how many link
/// groups and values it has, not where they start: they follow those of the rows before it in the same object, from
/// where its object's record says the object's own start, so that a change to one row changes one line of each table.
/// </para>
/// <para>
/// <b>Links</b>, one record per type of a row that links objects: the type, then either an object's number or, for
/// more than one candidate, <see cref="CandidateSetFlag"/> and a candidate set's number. <b>CandidateSets</b> hold
/// the set's first member in <b>SetMembers</b>, the number of members, and the key that tells its array candidates
/// apart and the key that tells the others apart, as names, or <see cref="NoPlan"/>. <b>Values</b>, one record per
/// plain value of a row: the value as a name, and the type it is a value of. <b>Extras</b>, one record per row that
/// has <see cref="ArlingtonRowFlags.HasExtras"/>, in order of rows: the row, its <see cref="ArlingtonOverride"/>, and
/// the range of versions a version-only requirement holds in.
/// </para>
/// </remarks>
internal static class ArlingtonLayout
{
    /// <summary>The most rows an object may have: the walk marks the keys present in one 64-bit mask.</summary>
    public const int MaxRowsPerObject = 64;

    /// <summary>Marks a link to a candidate set rather than to one object.</summary>
    public const ushort CandidateSetFlag = 0x8000;

    /// <summary>A candidate set whose candidates of one kind no single plain value tells apart.</summary>
    public const ushort NoPlan = 0xFFFF;

    /// <summary>The size of an object's record.</summary>
    public const int ObjectSize = 8;

    /// <summary>Offset of the object's first row (u16).</summary>
    public const int ObjectFirstRow = 0;

    /// <summary>Offset of the object's number of rows (u8).</summary>
    public const int ObjectRowCount = 2;

    /// <summary>Offset of the object's flags (u8): bit 0 is set for an array.</summary>
    public const int ObjectFlags = 3;

    /// <summary>Offset of the object's first link group (u16).</summary>
    public const int ObjectFirstLink = 4;

    /// <summary>Offset of the object's first value (u16).</summary>
    public const int ObjectFirstValue = 6;

    /// <summary>The object flag of an array.</summary>
    public const byte ObjectIsArray = 1;

    /// <summary>The size of a row's record.</summary>
    public const int RowSize = 10;

    /// <summary>Offset of the row's key, as a name (u16).</summary>
    public const int RowKey = 0;

    /// <summary>Offset of the row's types, an <see cref="ArlingtonTypes"/> (u24).</summary>
    public const int RowTypes = 2;

    /// <summary>Offset of the row's ISO <c>SinceVersion</c>, or <see cref="ArlingtonVersion.ExtensionOnly"/> (u8).</summary>
    public const int RowSince = 5;

    /// <summary>Offset of the row's <c>DeprecatedIn</c>, or <see cref="ArlingtonVersion.None"/> (u8).</summary>
    public const int RowDeprecated = 6;

    /// <summary>Offset of the row's <see cref="ArlingtonRowFlags"/> (u8).</summary>
    public const int RowFlags = 7;

    /// <summary>Offset of the row's number of link groups (u8).</summary>
    public const int RowLinkCount = 8;

    /// <summary>Offset of the row's number of plain values (u8).</summary>
    public const int RowValueCount = 9;

    /// <summary>The size of a link group's record.</summary>
    public const int LinkSize = 3;

    /// <summary>Offset of the link group's <see cref="ArlingtonType"/> (u8).</summary>
    public const int LinkType = 0;

    /// <summary>Offset of the link group's target (u16): an object, or a candidate set with <see cref="CandidateSetFlag"/>.</summary>
    public const int LinkTarget = 1;

    /// <summary>The size of a candidate set's record.</summary>
    public const int SetSize = 7;

    /// <summary>Offset of the set's first member in the members table (u16).</summary>
    public const int SetFirstMember = 0;

    /// <summary>Offset of the set's number of members (u8).</summary>
    public const int SetMemberCount = 2;

    /// <summary>Offset of the key that tells the set's array candidates apart, as a name, or <see cref="NoPlan"/> (u16).</summary>
    public const int SetArrayPlan = 3;

    /// <summary>Offset of the key that tells the set's other candidates apart, as a name, or <see cref="NoPlan"/> (u16).</summary>
    public const int SetOtherPlan = 5;

    /// <summary>The size of a value's record.</summary>
    public const int ValueSize = 3;

    /// <summary>Offset of the value's text, as a name (u16).</summary>
    public const int ValueName = 0;

    /// <summary>Offset of the <see cref="ArlingtonType"/> the value is a value of (u8).</summary>
    public const int ValueType = 2;

    /// <summary>The size of an extras record.</summary>
    public const int ExtraSize = 5;

    /// <summary>Offset of the row the extras are of (u16).</summary>
    public const int ExtraRow = 0;

    /// <summary>Offset of the row's <see cref="ArlingtonOverride"/> (u8).</summary>
    public const int ExtraOverride = 2;

    /// <summary>Offset of the first version a version-only requirement holds in, or <see cref="ArlingtonVersion.None"/> (u8).</summary>
    public const int ExtraRequiredFrom = 3;

    /// <summary>Offset of the version it stops holding in, or <see cref="ArlingtonVersion.Unbounded"/> (u8).</summary>
    public const int ExtraRequiredBefore = 4;
}
