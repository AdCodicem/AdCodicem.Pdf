using System.Diagnostics.CodeAnalysis;

// The generator is a tool, not the library: codecov.yml measures src/ only, and the copy of the core's
// ArlingtonEncoding.cs this assembly compiles would otherwise be reported twice, once half-used.
[assembly: ExcludeFromCodeCoverage(Justification = "A development tool; its tests assert what it generates, not how much of it runs.")]
