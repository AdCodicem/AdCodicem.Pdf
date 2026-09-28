namespace AdCodicem.Pdf.Arlington;

/// <summary>Where the generator's inputs and output are, in a checkout of the repository.</summary>
internal sealed class Repository
{
    private Repository(string root) => Root = root;

    /// <summary>Gets the repository's root: the directory that holds <c>AdCodicem.Pdf.slnx</c>.</summary>
    public string Root { get; }

    /// <summary>Gets the generator's directory.</summary>
    public string ToolDirectory => Path.Combine(Root, "tools", "AdCodicem.Pdf.Arlington");

    /// <summary>Gets the vendored model.</summary>
    public VendoredModel Model => new(Path.Combine(ToolDirectory, "model"));

    /// <summary>Gets the reviewed overrides.</summary>
    public string OverridesPath => Path.Combine(ToolDirectory, "overrides.tsv");

    /// <summary>Gets the generated file the core compiles.</summary>
    public string GeneratedPath => Path.Combine(Root, "src", "AdCodicem.Pdf", "Validation", "Arlington", "ArlingtonModel.g.cs");

    /// <summary>Finds the repository that holds <paramref name="start"/>.</summary>
    public static Repository Find(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AdCodicem.Pdf.slnx")))
            {
                return new Repository(directory.FullName);
            }
        }

        throw new InvalidDataException($"No AdCodicem.Pdf.slnx above {start}: run the generator from a checkout of the repository.");
    }

    /// <summary>Generates the tables from the vendored model and the overrides, in memory.</summary>
    public string Generate()
    {
        var model = Model;
        return ArlingtonGenerator.Generate(model.ReadCommit(), model.ReadTsvFiles(), File.ReadAllText(OverridesPath));
    }
}
