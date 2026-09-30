using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// A tutorial promises an exact result at every step (ADR 47), so the first one is held to the program it builds:
/// every C# block of <c>docs/website/docs/tutorials/first-steps.md</c> is in <c>samples/FirstSteps/Program.cs</c>,
/// which the build compiles, and every output the tutorial shows is what that program prints, as far as the step
/// it follows. A message or an API that changes breaks this test rather than the tutorial.
/// </summary>
public partial class FirstStepsTutorialTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(Corpus.Root, "..", ".."));

    [Fact]
    public void Every_code_block_of_the_tutorial_is_in_the_sample_it_builds()
    {
        var program = Normalize(File.ReadAllText(Path.Combine(RepositoryRoot, "samples", "FirstSteps", "Program.cs")));
        var blocks = Blocks("csharp");

        blocks.Should().NotBeEmpty("the tutorial builds its program from C# blocks");
        foreach (var block in blocks)
        {
            program.Should().Contain(block, "the sample is the program the tutorial builds, block by block");
        }
    }

    [Fact]
    public async Task The_sample_prints_what_the_tutorial_shows_after_each_step()
    {
        var printed = await RunSample();
        var outputs = Blocks("text", "title=\"Output\"");

        outputs.Should().NotBeEmpty("the tutorial shows what the program prints");
        foreach (var output in outputs)
        {
            printed.Should().StartWith(output, "each step runs the program as far as the tutorial has built it");
        }

        printed.Should().Be(outputs[^1], "the last step runs the whole program");
    }

    /// <summary>The fenced blocks of the tutorial in a language, whose info string holds <paramref name="meta"/>.</summary>
    private static List<string> Blocks(string language, string meta = "")
    {
        var tutorial = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "website", "docs", "tutorials", "first-steps.md"));
        var blocks = new List<string>();

        foreach (Match block in FencedBlock().Matches(Normalize(tutorial)))
        {
            if (block.Groups["language"].Value == language && block.Groups["meta"].Value.Contains(meta, StringComparison.Ordinal))
            {
                blocks.Add(block.Groups["body"].Value);
            }
        }

        return blocks;
    }

    /// <summary>
    /// Runs the sample the way a reader of the tutorial does, in a process of its own, through the application host the
    /// build copies beside this assembly, and returns what it printed.
    /// </summary>
    private static async Task<string> RunSample()
    {
        var host = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FirstSteps.exe" : "FirstSteps");
        var start = new ProcessStartInfo(host)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {host}.");
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        (await error).Should().BeEmpty("the sample writes nothing to the error stream");
        process.ExitCode.Should().Be(0);
        return Normalize(await output);
    }

    /// <summary>Line ends as the repository writes them, and no trailing blank line.</summary>
    private static string Normalize(string text) => text.ReplaceLineEndings("\n").TrimEnd('\n');

    [GeneratedRegex(@"^```(?<language>\w+)(?<meta>[^\n]*)\n(?<body>.*?)\n```$", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex FencedBlock();
}
