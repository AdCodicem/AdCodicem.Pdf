namespace AdCodicem.Pdf.Arlington;

/// <summary>
/// Generates <c>src/AdCodicem.Pdf/Validation/Arlington/ArlingtonModel.g.cs</c> from the Arlington PDF Model's TSV
/// files and the reviewed overrides. A plain function of its inputs: the same model and overrides give the same text,
/// whatever order the files come in, which is what lets a unit test regenerate the tables and compare them with the
/// committed file byte for byte.
/// </summary>
internal static class ArlingtonGenerator
{
    /// <summary>Generates the C# text of the tables.</summary>
    /// <param name="commit">The commit of the model the files come from, which the text names.</param>
    /// <param name="files">The model's TSV files (<c>tsv/latest</c>), each by the name of its object.</param>
    /// <param name="overrides">The text of <c>overrides.tsv</c>.</param>
    /// <exception cref="InvalidDataException">The model or the overrides hold what the tables cannot encode.</exception>
    public static string Generate(string commit, IEnumerable<SourceFile> files, string overrides) =>
        TableEmitter.Emit(Compile(commit, files, overrides));

    /// <summary>Reduces the model and applies the overrides: what <see cref="Generate"/> encodes.</summary>
    /// <exception cref="InvalidDataException">The model or the overrides hold what the tables cannot encode.</exception>
    public static CompiledModel Compile(string commit, IEnumerable<SourceFile> files, string overrides) =>
        ModelCompiler.Compile(commit, files, OverrideFile.Parse(overrides));
}
