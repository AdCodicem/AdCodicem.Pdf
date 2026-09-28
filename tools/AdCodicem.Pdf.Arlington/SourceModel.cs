namespace AdCodicem.Pdf.Arlington;

/// <summary>One TSV file of the model, by the name of the object it describes (its file name without <c>.tsv</c>).</summary>
internal sealed record SourceFile(string Name, string Text);

/// <summary>One row of a TSV file: its twelve cells, as written, and where it was written.</summary>
internal sealed class SourceRow
{
    private readonly Dictionary<string, string> _editors;

    public SourceRow(string objectName, int line, string[] cells)
        : this(objectName, line, cells, new Dictionary<string, string>(StringComparer.Ordinal))
    {
    }

    private SourceRow(string objectName, int line, string[] cells, Dictionary<string, string> editors)
    {
        ObjectName = objectName;
        Line = line;
        Cells = cells;
        _editors = editors;
    }

    /// <summary>Gets the object the row belongs to.</summary>
    public string ObjectName { get; }

    /// <summary>Gets the row's line in its file, the header being line 1.</summary>
    public int Line { get; }

    /// <summary>Gets the twelve cells, in the order of <see cref="Grammar.Columns"/>.</summary>
    public string[] Cells { get; }

    /// <summary>Gets the row's key.</summary>
    public string Key => Cells[0];

    /// <summary>Gets the cell of <paramref name="column"/>.</summary>
    public string this[string column] => Cells[IndexOf(column)];

    /// <summary>
    /// Returns a copy of the row with <paramref name="column"/> replaced, by <paramref name="editor"/> (an override,
    /// named in messages about that cell), or back to the model's own cell when <paramref name="editor"/> is null.
    /// </summary>
    public SourceRow With(string column, string value, string? editor)
    {
        var cells = (string[])Cells.Clone();
        cells[IndexOf(column)] = value;
        var editors = new Dictionary<string, string>(_editors, StringComparer.Ordinal);

        if (editor is null)
        {
            editors.Remove(column);
        }
        else
        {
            editors[column] = editor;
        }

        return new SourceRow(ObjectName, Line, cells, editors);
    }

    /// <summary>Says what wrote <paramref name="column"/>'s cell, for a message: an override, or nothing for the model itself.</summary>
    public string? EditorOf(string column) => _editors.GetValueOrDefault(column);

    /// <summary>Says where the row is, for a message: <c>PageObject.tsv, line 12 (MediaBox)</c>.</summary>
    public override string ToString() => $"{ObjectName}.tsv, line {Line} ({Key})";

    private static int IndexOf(string column)
    {
        for (var i = 0; i < Grammar.Columns.Count; i++)
        {
            if (Grammar.Columns[i] == column)
            {
                return i;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(column), column, "Not a column of the model.");
    }
}

/// <summary>The model's files read as rows, before any cell is interpreted.</summary>
internal static class SourceModel
{
    /// <summary>
    /// Reads every file: an ASCII text, lines ended by a line feed, a header naming the twelve columns, then one row
    /// of twelve cells per line. Returns the objects in ordinal order of their names.
    /// </summary>
    public static SortedDictionary<string, List<SourceRow>> Read(IEnumerable<SourceFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var objects = new SortedDictionary<string, List<SourceRow>>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            if (!Grammar.IsIdentifier(file.Name))
            {
                throw new InvalidDataException($"'{file.Name}' is not a name the model gives an object.");
            }

            if (!objects.TryAdd(file.Name, ReadRows(file)))
            {
                throw new InvalidDataException($"{file.Name}.tsv is given twice.");
            }
        }

        if (objects.Count == 0)
        {
            throw new InvalidDataException("The model has no file.");
        }

        return objects;
    }

    private static List<SourceRow> ReadRows(SourceFile file)
    {
        var text = file.Text;

        foreach (var c in text)
        {
            if (c is > '~' or (< ' ' and not '\t' and not '\n'))
            {
                throw new InvalidDataException($"{file.Name}.tsv holds a character other than printable ASCII, tabs and line feeds.");
            }
        }

        if (text.Length == 0 || text[^1] != '\n')
        {
            throw new InvalidDataException($"{file.Name}.tsv does not end with a line feed.");
        }

        var lines = text[..^1].Split('\n');

        if (lines[0] != string.Join('\t', Grammar.Columns))
        {
            throw new InvalidDataException($"{file.Name}.tsv does not start with the model's twelve columns.");
        }

        var rows = new List<SourceRow>(lines.Length - 1);

        for (var i = 1; i < lines.Length; i++)
        {
            var cells = lines[i].Split('\t');

            if (cells.Length != Grammar.Columns.Count)
            {
                throw new InvalidDataException($"{file.Name}.tsv, line {i + 1}: {cells.Length} cells where the model has {Grammar.Columns.Count}.");
            }

            rows.Add(new SourceRow(file.Name, i + 1, cells));
        }

        if (rows.Count == 0)
        {
            throw new InvalidDataException($"{file.Name}.tsv has no row.");
        }

        return rows;
    }
}
