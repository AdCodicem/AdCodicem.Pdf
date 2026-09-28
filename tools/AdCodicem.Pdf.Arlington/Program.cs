using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace AdCodicem.Pdf.Arlington;

/// <summary>
/// The command line:
/// <c>dotnet run --project tools/AdCodicem.Pdf.Arlington -- generate</c> checks the vendored model against its lock
/// and writes <c>ArlingtonModel.g.cs</c>; <c>verify</c> checks the lock and that the committed file is what
/// generating gives; <c>update --from &lt;clone&gt;</c> vendors the model from a clone of its repository, at the
/// commit the clone has checked out, rewrites the lock, and generates.
/// </summary>
internal static class Program
{
    private const string Usage =
        "usage: dotnet run --project tools/AdCodicem.Pdf.Arlington -- generate | verify | update --from <clone of arlington-pdf-model>";

    private static int Main(string[] args)
    {
        try
        {
            var repository = Repository.Find(AppContext.BaseDirectory);

            return args switch
            {
                ["generate"] => Generate(repository),
                ["verify"] => Verify(repository),
                ["update", "--from", var clone] => Update(repository, clone),
                _ => Fail(Usage),
            };
        }
        catch (InvalidDataException exception)
        {
            return Fail(exception.Message);
        }
    }

    private static int Generate(Repository repository)
    {
        var problems = repository.Model.Verify();

        if (problems.Count > 0)
        {
            return Fail("The vendored model does not match its lock:\n  " + string.Join("\n  ", problems));
        }

        var text = repository.Generate();
        File.WriteAllText(repository.GeneratedPath, text);
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Wrote {Path.GetRelativePath(repository.Root, repository.GeneratedPath)} ({Encoding.UTF8.GetByteCount(text)} bytes) from commit {repository.Model.ReadCommit()}."));
        return 0;
    }

    private static int Verify(Repository repository)
    {
        var problems = repository.Model.Verify();

        if (problems.Count == 0 && File.ReadAllText(repository.GeneratedPath).ReplaceLineEndings("\n") != repository.Generate())
        {
            problems.Add($"{Path.GetRelativePath(repository.Root, repository.GeneratedPath)} is not what generating gives: run the generate command.");
        }

        if (problems.Count > 0)
        {
            return Fail(string.Join("\n", problems));
        }

        Console.WriteLine("The vendored model matches its lock, and the tables match the model and the overrides.");
        return 0;
    }

    private static int Update(Repository repository, string clone)
    {
        var commit = Git(clone, "rev-parse", "HEAD");
        var source = Path.Combine(clone, VendoredModel.TsvDirectory);
        var target = Path.Combine(repository.Model.Directory, VendoredModel.TsvDirectory);

        if (!Directory.Exists(source))
        {
            return Fail($"{clone} has no {VendoredModel.TsvDirectory} directory.");
        }

        if (Directory.Exists(target))
        {
            foreach (var old in Directory.GetFiles(target, "*.tsv"))
            {
                File.Delete(old);
            }
        }

        Directory.CreateDirectory(target);

        foreach (var file in Directory.GetFiles(source, "*.tsv"))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var name in VendoredModel.OtherFiles)
        {
            File.Copy(Path.Combine(clone, name), Path.Combine(repository.Model.Directory, name), overwrite: true);
        }

        repository.Model.WriteLock(commit);
        Console.WriteLine($"Vendored the model at commit {commit}.");
        return Generate(repository);
    }

    private static string Git(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(directory);

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidDataException("git could not be started.");
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        return process.ExitCode == 0 && output.Length == 40
            ? output
            : throw new InvalidDataException($"{directory} is not a clone whose commit git can give.");
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
