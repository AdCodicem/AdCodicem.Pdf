using System.Diagnostics.Tracing;
using System.Globalization;

namespace AdCodicem.Pdf.InliningCheck;

/// <summary>
/// Reads the runtime's events of the compiles of the rebuild's scan for objects: the tier each compile is at, and whether
/// it inlined the lexer.
/// </summary>
/// <remarks>
/// A compile's events arrive in order on the thread that compiles: its start, the inlining decisions, then its load, which
/// carries its tier. Compiles of the scan on two threads at once — on-stack replacement in the loop, a tier in the
/// background — are told apart by the thread.
/// </remarks>
internal sealed class InliningListener : EventListener
{
    private const string RuntimeSource = "Microsoft-Windows-DotNETRuntime";

    /// <summary>The runtime's JIT and JIT tracing keywords: compiles, and their inlining decisions.</summary>
    private const EventKeywords JitKeywords = (EventKeywords)0x1010;

    private const string ScanType = "AdCodicem.Pdf.IO.PdfFileReader";
    private const string ScanMethod = "ScanForObjects";
    private const string LexerType = "AdCodicem.Pdf.IO.PdfLexer";
    private const string LexerMethod = "Read";

    private readonly Lock _gate = new();
    private readonly Dictionary<long, Compile> _compiling = [];
    private readonly List<Compile> _compiles = [];

    /// <summary>Gets a value indicating whether the scan had its final optimized compile: its last tier, or the only one.</summary>
    public bool SawFinalCompile
    {
        get
        {
            lock (_gate)
            {
                return _compiles.Exists(static compile => compile.Final);
            }
        }
    }

    /// <summary>Gives the scan's compiles that have loaded, in the order they did.</summary>
    public IReadOnlyList<Compile> Compiles()
    {
        lock (_gate)
        {
            return [.. _compiles];
        }
    }

    /// <inheritdoc/>
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == RuntimeSource)
        {
            EnableEvents(eventSource, EventLevel.Verbose, JitKeywords);
        }
    }

    /// <inheritdoc/>
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        var name = eventData.EventName;

        if (name is null || eventData.Payload is null)
        {
            return;
        }

        lock (_gate)
        {
            if (name.StartsWith("MethodJittingStarted", StringComparison.Ordinal))
            {
                if (IsScan(eventData, "MethodNamespace", "MethodName"))
                {
                    _compiling[eventData.OSThreadId] = new Compile();
                }
            }
            else if (name.StartsWith("MethodJitInlining", StringComparison.Ordinal))
            {
                if (_compiling.TryGetValue(eventData.OSThreadId, out var compile)
                    && IsScan(eventData, "MethodBeingCompiledNamespace", "MethodBeingCompiledName")
                    && Text(eventData, "InlineeNamespace") == LexerType
                    && Text(eventData, "InlineeName") == LexerMethod)
                {
                    compile.Decide(name.StartsWith("MethodJitInliningSucceeded", StringComparison.Ordinal), Text(eventData, "FailReason"));
                }
            }
            else if (name.StartsWith("MethodLoadVerbose", StringComparison.Ordinal)
                && IsScan(eventData, "MethodNamespace", "MethodName")
                && _compiling.Remove(eventData.OSThreadId, out var loaded))
            {
                loaded.Load(Convert.ToUInt32(eventData.Payload[eventData.PayloadNames!.IndexOf("MethodFlags")], CultureInfo.InvariantCulture));
                _compiles.Add(loaded);
            }
        }
    }

    private static bool IsScan(EventWrittenEventArgs eventData, string type, string method) =>
        Text(eventData, type) == ScanType && Text(eventData, method) == ScanMethod;

    private static string Text(EventWrittenEventArgs eventData, string payload)
    {
        var index = eventData.PayloadNames?.IndexOf(payload) ?? -1;
        return index < 0 ? string.Empty : eventData.Payload![index]?.ToString() ?? string.Empty;
    }
}
