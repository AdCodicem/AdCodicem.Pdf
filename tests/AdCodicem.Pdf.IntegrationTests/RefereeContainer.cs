using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

namespace AdCodicem.Pdf.IntegrationTests;

/// <summary>
/// A container holding the independent tools our acceptance conditions are checked against.
/// </summary>
/// <remarks>
/// The corpus promises that our claims are verified by tools we did not write. Running them in a
/// container rather than expecting them on the machine is what makes that promise reproducible: the same
/// qpdf version answers on a developer's laptop and in CI, and nobody has to install anything.
///
/// Where Docker is not available — a sandbox, a locked-down machine — the tests using this fixture skip
/// rather than fail. A skipped integration test is visible in the run; a broken build is noise.
/// </remarks>
public sealed class RefereeContainer : IAsyncLifetime
{
    private const string Image = "alpine:3.21";
    private const string CorpusMount = "/corpus";

    private IContainer? _container;

    /// <summary>Gets the reason the referee is unavailable, or null when it is ready.</summary>
    public string? Unavailable { get; private set; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new ContainerBuilder(Image)
                .WithBindMount(Corpus.Root, CorpusMount, AccessMode.ReadOnly)
                .WithEntrypoint("/bin/sh", "-c")
                // qpdf is installed at start-up rather than baked into an image, so the corpus needs no
                // registry of our own and the version is visible in the logs.
                .WithCommand("apk add --no-cache qpdf >/dev/null 2>&1 && sleep infinity")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("qpdf", "--version"))
                .WithStartupCallback((_, _) => Task.CompletedTask)
                .Build();

            await _container.StartAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Unavailable = $"the referee container could not start: {exception.Message}";
            _container = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Runs a command inside the container and returns its exit code and both output streams.</summary>
    /// <remarks>
    /// The streams are kept apart because a referee answers on one and complains on the other: qpdf prints a
    /// page count on standard output and its warnings about the same file on standard error.
    /// </remarks>
    public async Task<(long? ExitCode, string Stdout, string Stderr)> RunAsync(params string[] command)
    {
        if (_container is null)
        {
            throw new InvalidOperationException(Unavailable ?? "The referee container is not running.");
        }

        var result = await _container.ExecAsync(command).ConfigureAwait(false);
        return (result.ExitCode, result.Stdout, result.Stderr);
    }

    /// <summary>Maps a corpus-relative path to its path inside the container.</summary>
    public static string PathInContainer(string corpusRelativePath) =>
        $"{CorpusMount}/{corpusRelativePath}";
}

/// <summary>Shares one referee container across the whole integration suite.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "xUnit collection definitions are named after the collection they define.")]
[CollectionDefinition(Name)]
public sealed class RefereeCollection : ICollectionFixture<RefereeContainer>
{
    public const string Name = "referee";
}
