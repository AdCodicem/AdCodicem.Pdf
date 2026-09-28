using System.Security.Cryptography;
using System.Text;

namespace AdCodicem.Pdf.Arlington;

/// <summary>
/// The copy of the model the repository keeps in <c>tools/AdCodicem.Pdf.Arlington/model/</c>, byte for byte as
/// upstream has it at the pinned commit (<c>tsv/latest/*.tsv</c>, <c>LICENSE</c>, <c>NOTICE.txt</c>), and its lock,
/// <c>model.lock</c>: the commit on the first line, then one line per vendored file, its SHA-256 and its path, in the
/// format <c>sha256sum</c> reads.
/// </summary>
internal sealed class VendoredModel
{
    /// <summary>The lock's file name.</summary>
    public const string LockName = "model.lock";

    /// <summary>Where the TSV files are, under the model's directory.</summary>
    public const string TsvDirectory = "tsv/latest";

    /// <summary>The files other than the TSV files that are vendored.</summary>
    public static readonly IReadOnlyList<string> OtherFiles = ["LICENSE", "NOTICE.txt"];

    public VendoredModel(string directory) => Directory = directory;

    /// <summary>Gets the model's directory.</summary>
    public string Directory { get; }

    /// <summary>Gets the lock's path.</summary>
    public string LockPath => Path.Combine(Directory, LockName);

    /// <summary>Reads the commit the lock pins.</summary>
    public string ReadCommit() => ParseLock(File.ReadAllText(LockPath)).Commit;

    /// <summary>Reads the TSV files, each by the name of its object.</summary>
    public List<SourceFile> ReadTsvFiles()
    {
        var files = new List<SourceFile>();

        foreach (var path in System.IO.Directory.GetFiles(Path.Combine(Directory, TsvDirectory), "*.tsv"))
        {
            files.Add(new SourceFile(Path.GetFileNameWithoutExtension(path), Encoding.UTF8.GetString(File.ReadAllBytes(path))));
        }

        return files;
    }

    /// <summary>
    /// Says what is wrong between the vendored files and the lock: a file whose hash differs, a file the lock does
    /// not name, a file it names that is missing. Empty when they agree.
    /// </summary>
    public List<string> Verify()
    {
        var problems = new List<string>();
        var (_, entries) = ParseLock(File.ReadAllText(LockPath));
        var present = new SortedSet<string>(ListFiles(), StringComparer.Ordinal);

        foreach (var (hash, path) in entries)
        {
            if (!present.Remove(path))
            {
                problems.Add($"{path} is in the lock but not in the model's directory.");
            }
            else if (Hash(Path.Combine(Directory, path)) != hash)
            {
                problems.Add($"{path} does not have the SHA-256 the lock gives it.");
            }
        }

        foreach (var path in present)
        {
            problems.Add($"{path} is in the model's directory but not in the lock.");
        }

        return problems;
    }

    /// <summary>Writes the lock for the files present, pinned to <paramref name="commit"/>.</summary>
    public void WriteLock(string commit)
    {
        var text = new StringBuilder().Append(commit).Append('\n');

        foreach (var path in ListFiles())
        {
            text.Append(Hash(Path.Combine(Directory, path))).Append("  ").Append(path).Append('\n');
        }

        File.WriteAllText(LockPath, text.ToString());
    }

    /// <summary>Reads a lock: its commit, then its files.</summary>
    public static (string Commit, List<(string Hash, string Path)> Entries) ParseLock(string text)
    {
        var lines = text.Split('\n');

        if (lines.Length < 2 || lines[^1].Length != 0 || lines[0].Length != 40 || !lines[0].All(char.IsAsciiHexDigitLower))
        {
            throw new InvalidDataException($"{LockName} does not start with a full commit hash, or does not end with a line feed.");
        }

        var entries = new List<(string Hash, string Path)>();

        for (var i = 1; i < lines.Length - 1; i++)
        {
            var line = lines[i];

            if (line.Length < 67 || line[64] != ' ' || line[65] != ' ' || !line[..64].All(char.IsAsciiHexDigitLower))
            {
                throw new InvalidDataException($"{LockName}, line {i + 1}: not a SHA-256, two spaces and a path.");
            }

            entries.Add((line[..64], line[66..]));
        }

        return (lines[0], entries);
    }

    /// <summary>The SHA-256 of a file, in lower-case hexadecimal.</summary>
    public static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private IEnumerable<string> ListFiles() =>
        System.IO.Directory.GetFiles(Directory, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(Directory, p).Replace('\\', '/'))
            .Where(static p => p != LockName)
            .Order(StringComparer.Ordinal);
}
