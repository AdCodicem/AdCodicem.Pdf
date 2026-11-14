# Threat model

What the library defends against, how, where each defense lives in the code, and which test fails if it goes.
It is one document for the whole library ([ADR 46](adr/0046-every-milestone-ends-with-an-adversarial-review.md)).
A milestone that opens or widens a surface a hostile input reaches completes its section **in the same change**.
Every milestone's review checks the code against it (`docs/milestone-review.md`, *Security*). Read it before you
touch a parser, a decoder or a resource loader (`CLAUDE.md`).

This first version was written on 2026-09-30 and 2026-10-01 against `13e06bb` ([#135]). It covers what exists
there: the reader (M01) and the validator (M02, slices 1 to 3). Each defense below was checked against the code as
it stands, and each test it names was read. A defense found missing is not written as if it held. It is listed under the
*Known gaps* of its surface, with the issue that pays it.

On 2026-11-11 it was compacted to what each defense stops, where it lives and the test class that holds it
([ADR 54](adr/0054-review-and-documentation-effort-follow-what-protects-the-library.md)). How the reader and the
validator answer each damaged shape — which code, which severity, which objects — is written beside the code, and the
codes in the diagnostics reference (`docs/website/docs/reference/diagnostics.md`); git keeps the longer version.

## How to read it

- A **threat** is what a file can make the library do: exhaust memory, the stack or time, read out of bounds,
  throw something untyped, return a wrong answer, or reach the host.
- A **defense** is the code that stops it: a bound, a visited set, a check, an iterative walk.
- **Held by** names the test class whose tests fail if the defense goes. **Fuzzed** says whether the mutation
  campaign reaches the code at all (*What holds the defenses*, below).
- A bound is classified as [ADR 34](adr/0034-every-valid-pdf-is-readable-and-the-readers-guards-are.md) requires.
  A **guard** is a `PdfReaderLimits` property, on by default and reported under a `limit.*` code: a valid file
  may reach it. An **internal constant** is a bound that only an invalid file reaches, and the reason is written
  where the constant is declared. A bound on what the library repeats of a value, not on what it reads, is an
  internal constant too, though a valid file may reach it, and its declaration says so: a message quotes 127 bytes
  of a name ([ADR 34](adr/0034-every-valid-pdf-is-readable-and-the-readers-guards-are.md), amended 2026-10-01).

## Assets

What a caller entrusts to the library when it opens a file it did not produce:

- **The process's memory.** Memory follows the heaviest object the caller touches, not the size of the file
  (invariant 2). No allocation is sized by a value from the file without a checked bound (invariant 4). The eight
  guards cap what a valid but exceptional file may make the reader hold. Two of the validator's walks still hold, of a
  hostile file, more than the reader's cache ([#309], [#311]).
- **The process's time, and the calling thread.** The aim is that every call does work in proportion to the bytes
  it reads, under the guards. Some shapes break it today ([#181]). No call can be
  canceled yet ([M03] slice 9, [M23] slice 11). A .NET thread cannot be aborted, so a slow call holds its thread
  until it returns.
- **The stack.** A `StackOverflowException` cannot be caught in .NET: it ends the process. Every recursion whose
  depth a file chooses is bounded, and the reader, and the validator's walks of the object streams, also ask the runtime
  whether stack is left (`RuntimeHelpers.TryEnsureSufficientExecutionStack`) before they go one level deeper.
- **The caller's resources.**
  - `Open(string)` opens a read-only handle (`FileShare.Read`) and holds it until `Dispose`.
  - `Open(ReadOnlyMemory<byte>)` reads the caller's memory where it lies, without copying it.
  - `Open(Stream)` reads the caller's stream from its start when it can seek, from where it stands otherwise, and
    leaves it open. Only a `MemoryStream` itself is read in its buffer; a derived one, which may lend its buffer again
    once disposed, is copied.
  - `Open(PdfFileSource, …)` releases the source when it was given to own it, on failure too.
- **The truth of what the library returns.** A caller decides on the objects, the decoded data, the diagnostics
  and the validation report. Should an upload be accepted? Is the document encrypted? Did the reader cut
  something? A file that makes the report lie defeats the caller as surely as a crash does. That covers hiding its
  encryption, hiding a cut, or getting the reader's own limit blamed on the file.
- **The host's logs and terminals.** Diagnostic and finding messages quote names and keywords from the file, and
  hosts log and print them, as they print the objects a document hands out, through their `ToString`. A quote is
  printable ASCII on one line and at most 127 bytes of what the file wrote, and what an object says of itself is too,
  127 characters of a string's text at most, so what a host prints is the library's text, not the file's (*Reports*,
  below).

The library holds more later: the files it writes ([M03]), the original bytes and existing signatures an
incremental update preserves ([M03], [M04]), passwords and file keys ([M16]), signing keys and certificates
([M26]), access to the network ([M12]'s resource loader, [M26] and [M27]'s clients), native code in the process
([M12], [M25]), and caches shared across documents and renders ([M08], [M12]). *Surfaces still to come* lists
each with the milestone that brings it.

## Attackers

- **Whoever wrote the file's bytes.** That is the author of a PDF the caller opens: an upload, an e-mail
  attachment, a document received for a case file. They control every byte. They can iterate offline against the
  same package and the same public source. They want to crash, hang or exhaust the host. They want the library to
  return something other than what the file holds, or a report that hides what the file is. Later, they will want
  to reach the host: its files, its network, its secrets.
- **Not the caller.** The caller's code is trusted, and so are its options, the path it names, the stream or
  memory it passes, a `PdfFileSource` it writes, and the process's environment. A caller that raises a guard, or
  chooses `PdfReaderLimits.Unbounded`, accepts what the raised bound allows ([SECURITY.md](https://github.com/AdCodicem/AdCodicem.Pdf/blob/main/SECURITY.md)).
- **Whoever could alter the package** between this repository and nuget.org, or a dependency on the way in. See
  *The package and its supply chain*.

## Trust boundaries

Where a byte from outside the caller's code enters today:

| Boundary | Where | What crosses |
|---|---|---|
| The file source | `PdfDocument.Open(string)`, `Open(ReadOnlyMemory<byte>)`, `Open(Stream)`, `Open(PdfFileSource, PdfReaderOptions?, bool)` | Every byte of the file, read lazily through windows |
| What the reader derives | `GetObject`, `PdfReference.Resolve`, the `PdfObjectExtensions` accessors, `Trailer`, `Catalog`, `ObjectNumbers`, `PdfStream.GetRawBytes`, `Decode` | Values the file chose, handed to the caller's code. A later read that rebuilds the index changes what the next call gives, never what a call gave before it or gives after it; only a caller's own `PdfFileSource`, asking from inside the rebuilding read, can take a trailer the rebuild still changes ([#167]); a rebuild the source fails, in its scan or its finish, is run again by the next read or validation, and the calls between give the unfinished rebuild's index and trailer, and `Catalog` reads that trailer's `/Root`, which need not name a catalog |
| Messages | `PdfDiagnostic.Message` and `ToString`, `PdfValidationFinding.Message` and `ToString`; the `ToString` of the objects a document hands out — `PdfName`, `PdfDictionary`, `PdfStream`, `PdfString`, `PdfReal` | Text that quotes names and keywords from the file, escaped and cut; a string's text, escaped and cut; a real's shortest decimal |
| The validator | `PdfValidator.Validate` | The same document, plus bytes the validator reads itself: the file's tail, each entry's header read as a load reads it, object stream headers, and each member whose bytes hold `stream`, parsed as a load parses it |

Every surface still to come adds a boundary of its own: HTML, CSS and template data, the resource loader, font
programs, image files, content streams, XML, encryption, signatures and the network, DOCX packages and e-mail.
*Surfaces still to come* lists each one.

## Defenses every surface shares

### The guards

| `PdfReaderLimits` | Default | Code | Where it applies |
|---|---|---|---|
| `MaxDecodedStreamLength` | 256 MB | `limit.decoded-stream` | Every filter's output: the last filter of a chain alone, the filters before it together (`PdfFilterPipeline`) |
| `MaxStreamLength` | 256 MB | `limit.stream-length` | One stream's encoded data, read in one piece by `GetRawBytes` or a decoding (`FileStreamData.GetBytes`). Past it the data is cut, and what lies within is decoded as cut data. It, not `MaxDecodedStreamLength`, bounds data decoding hands back as it is. Its ceiling is `Array.MaxLength` ([#170]) |
| `MaxObjectLength` | 16 MB | `limit.object` | The window a regular object is parsed through, grown ×8 from 8 KB (`PdfFileReader.TryParseNumberedAt`), and an object stream member's (`ObjectStreamMemberWindow.Of`, `ReachMemberLimit`). A stream's data never counts toward it ([#174]) |
| `MaxXRefSectionLength` | 64 MB | `limit.xref-section-length` | The window a classic cross-reference table is read through, the padding before it included ([#289]) |
| `MaxXRefSectionCount` | 1,024 | `limit.xref-section-count` | The `/Prev` links the chain follows |
| `MaxTrailerLength` | 64 KB | `limit.trailer` | A trailer the table's window cut, and a cross-reference stream's dictionary, read from its header ([#220]); apart, the white space and comments before a section the chain names (`PdfFileReader.PaddingAt`) |
| `MaxIndexEntries` | 8,388,607 | `limit.index-entries` | The object numbers the index holds, refused ones counted, from the chain, a rebuild, or the room a section reserves (`PdfFileReader.AddFromChain`, `Refuse`, `HasRoomInRebuild`, `ReserveIndex`). Refused numbers are held apart until the chain is read |
| `MaxFilterChainLength` | 64 | `limit.filter-chain` | The filters a stream's `/Filter` names, `/Crypt` included: decoding stops before the first past them (`FilterChain`), and a cross-reference or object stream it ends is read as cut before its data (`PdfFilterPipeline.Decode`) |

Reaching a guard keeps what fits and reports its code (`PdfLimitGuard.Reach`), once per position, once per member of
each definition of an object stream, once per document for the index, and for a stream's decoding once in its document's
diagnostics and each time in diagnostics the caller passes ([#144], [#170]). What a guard leaves out of the index reads
as null until it is raised, and the index is not rebuilt for it: a guard's cut is no damage. A value read through a
reference, or a chain of references, to an object a guard cut or left out of the index is the guard's too, and the
reader judges none of it as the file's — but for a whole reference the cut left after the value, which is followed as a
load follows it ([#224], [#263], *Indexing*). This adds no bound. With `ThrowOnLimit`, reaching a guard throws
`PdfLimitExceededException` instead. A stream decodes under its document's guards, however long after opening.
`PdfReaderLimits` refuses a bound of zero or less and takes a length past `Array.MaxLength` as that length. Case by
case: reader limits (`docs/website/docs/reference/reader-limits.md`), and why each is a
guard (`docs/website/docs/concepts/reader-limits.md`). Held by `ReaderLimitsTests`; the mutation campaign opens with the
defaults, never with `ThrowOnLimit`.

### Iteration, visited sets, and the stack

- **The parser recurses**, one frame per container level, bounded by `PdfObjectParser.MaxDepth` (128) and a stack probe
  at each container. Past either, the container is skipped by counting brackets, with no recursion, and reported as
  `syntax.depth-exceeded`.
- **Loads nest**: an indirect `/Length`, an object stream's dictionary or a filter parameter loads an object mid-parse.
  `PdfFileReader.MaxNestedLoads` (64) and a stack probe before each load bound the chain; `_loading` turns a load that
  needs itself into null. A stream resolves its filters and their parameters before any of its data (`FilterChain`), so
  no level holds decoded data while the next loads. While the cross-reference chain is read, nothing loads (*Indexing*).
- **Every walk the validator makes across objects is iterative but one**, with a visited set by object number: the
  object graph, the page tree, the Arlington walk, and name and number trees. The walk of what decoding each object
  stream reads (`ObjectStreamDependencies`) follows chains of references in a loop but recurses as the reader's loads
  nest, at most `MaxNestedLoads` levels, and the walk of a stream's standing runs inside it, so no recursion goes deeper
  than twice that bound. Standings that wait on one another are worked out on a stack of their own, rid of stale entries
  whenever it holds more than 1,024 and twice as many as it kept the last time: it holds no more than twice the most
  streams waiting at once, or 1,024 entries, whichever is more, besides one walk's unknown streams ([#312];
  `HostileInputTests`). The deepest validation measured needs a thread of some 610 KB for the reader's probe to cut none
  of its loads, some 800 KB once tiered PGO has recompiled the walks; 1 MB has room for either. The walks ask the thread
  for room before each level, as the reader does before each load, since what the reader serves from its cache comes
  with no load and no probe ([#312]). On a thread with less room a probe cuts. The reader's cuts a load, and the walks
  keep what it gave for the rest of the validation ([#163]); the walks' own reads as null, takes the stream whose
  standing it was for one that does not decode alike, and is reported once under `syntax.depth-exceeded`. Either way,
  the report can depend on the thread (a cut's effects: `ObjectRuleTests`). Loops among object streams are found by
  Tarjan's algorithm on a stack of its own ([#277], [#168]). The object graph's two fault searches, and the writing of a
  page tree kid's path, loop too, however deep a caller nested the value ([#168]).
- **Chains are followed once.** The `/Prev` chain keeps the offsets it has read. `PdfReference.Resolve` fetches at most
  `MaxChainLength` (32) objects, so a chain of 31 references to references already reads as null ([#173]). The index is
  rebuilt at most once per document (`_repaired`).
- **Reports are made once.** A guard once per `(limit, position)`, a full index once per document, what decoding a
  stream met once per report its document's diagnostics hold ([#144]), a stream's length fault once per data start, a
  fault of the syntax once per code, position and message; a search for a lost `endstream` runs once per data start.

### Tables keyed by numbers

A file chooses its object numbers and offsets. An `int` is its own hash, a `long` its halves exclusive-ored, and .NET
buckets a key by its hash modulo a public prime: numbers a multiple of it apart share a bucket, every lookup walking
tens of thousands of entries ([#155]).

- **The index finds a number in a slot while numbers run in order** (`PdfXRefTable`): an array no longer than twice the
  entries it holds or has made room for, and 1,024 more, then a map hashed through the process's seed. A file that
  spreads its numbers out fills the map, which gives back the room of the numbers the slots come to reach once most of
  it is idle.
- **Every other table keyed by numbers hashes them through that seed** (`SeededComparer`), the one .NET draws once a
  process for `System.HashCode`, an offset's halves mixed in apart: the reader's cache, object streams, sets and memos,
  and the validator's walks. `PdfObjectId` and `PdfDiagnostic` hash through it too.
- **No order the hashes give reaches an output** (invariant 6). A table enumerates in the order of what was added and
  removed, never by hash, so a seed that differs between processes changes nothing reported or written. The library
  builds no table that enumerates by hash — concurrent, frozen, immutable, a `StringDictionary` — but `PdfName`'s
  interning table, keyed by strings, which nothing enumerates. It runs no parallel query, whose order neither a comparer
  nor the reading holds (`architecture.md`).
- **Two tests read the code** (`SeededComparerTests`, with a sample of each refused shape). One reads the compiled
  library and refuses every hash collection over a key a file can steer — a number wider than a byte, or a tuple, record
  or struct holding or hashing as one — unless its comparer is `SeededComparer.Instance`, handed to it straight on every
  path that reaches it. It refuses always a table keyed by any object (a `Hashtable` and its kin, a key typed `object`,
  `ValueType` or an interface); one that enumerates by hash (a concurrent, frozen or immutable one, a `StringDictionary`, an obsolete `XmlSchemaCollection`), but `PdfName`'s interning table, built once by its static
  constructor and reached only to find or add a name; one over a key holding a type parameter; such a table built for a
  type argument by `new()`, `Activator`, `Lazy`, `LazyInitializer`, `ConditionalWeakTable`, `BindingList` or
  `Marshal.PtrToStructure`, which hand it no comparer; and a generic JSON deserializer (the library reads no JSON). It
  does not follow a member reached through `object`, a non-generic interface or an expression tree, nor all that callers
  give generic code (a dispatch, a constrained call, a default implementation), nor read a collection built otherwise
  through reflection, an `UnsafeAccessor`'s among them, or one a framework method returns under another type; a type
  hashed by hand is held by its own tests. It reads through code coverage's probes by their shape: any other instruction
  between comparer and table refuses it. The other reads the sources, naming the file and line of each shape it
  recognizes.

### Everything read is bounded by the source

`PdfFileSource.GetWindow` returns an empty window for a range outside the source and clamps the rest. `FileSource.Read`
loops until the buffer is full or the file ends. A pooled window goes back to its pool at the end of its `using`, and
parsed values are copied out of it, so nothing a caller holds points into a rented buffer. The reader's side of a
source's contract is written on `PdfFileSource.Read`: it asks a source only for bytes the source holds, never for none,
however damaged the file, so a caller's source may refuse any other read; the validator's reads keep to it too ([#167]).
What a count shorter than the buffer means is [#52]'s. Held by `DocumentReaderTests`, `StreamLengthTests`,
`CrossReferenceRuleTests`, and every test that reads through `StrictCountingSource`, which throws on a read past its end
or of no byte.

### Reports

`PdfDiagnostics` keeps `PdfReaderOptions.DiagnosticCapacity` entries (1,000) and counts the rest in `SuppressedCount`.
`PdfValidationReport` keeps `PdfValidatorOptions.FindingCapacity` findings (1,000) and counts the rest by severity and
rule. Both bound the **number** of entries, and the validator's analyses write no more findings than there is room for
([#168]; *The validator*, below). A count past what an `int` holds stays at `int.MaxValue`, the findings not kept
included, which a file of tens of kilobytes decoding to gigabytes can reach.

An entry's **size** is bounded by how it quotes the file ([#159]). Every name and keyword a message quotes goes through
`FileQuote`, which writes it as a PDF writer writes a name: printable ASCII on one line, every other byte, the number
sign and the delimiters as `#xx`, cut past `FileQuote.MaxBytes` (127) with the whole's length, from a buffer on the
stack, so a name of 64 MB costs its quote and no copy. A path of keys keeps its first and last `RuleText.PathEnds` (4)
steps. A string from the file is never quoted. Numbers are written in the invariant culture, a count agrees with its
noun, and a distance is a size and the way it runs, never a negative number (`CountText`, [#241]), though some counts
group their digits and others do not ([#326]). Of a stream's length fault the reader keeps the value it found, not
words. See diagnostics (`docs/website/docs/reference/diagnostics.md`). Held by `FileQuoteTests`, `CountTextTests`,
`PropertyTests`, `HostileInputTests` (allocation budgets), and `CorpusReadingTests`, under a Persian culture.

What an object says of itself through `ToString` follows the same rules ([#212]): a name, and the type a dictionary or a
stream gives itself by, through `FileQuote`; a string's text between double quotes, every character outside printable
ASCII escaped as in a C# literal, cut past 127 characters with the whole's length, decoding no more than it shows; a
real as the shortest decimal that reads back to it, at most 327 characters. None copies what it cuts. `Value`, `Bytes`,
`ToText` and `GetRawBytes` stay the raw accessors. Held by `ObjectModelTests` and `PropertyTests`.

### What may be thrown

From a file, only a `PdfException`: `PdfFormatException` for an input that is empty or holds no object,
`PdfEncryptedException`, and `PdfLimitExceededException` when the caller asked for it. Everything else that escapes is
the caller's: an argument it passed, the I/O of its own file or stream, a document it disposed. Any other exception a
file can cause is a defect, and the ones found are in the gaps below.

### What the library never does with a file's content

It opens no path, URL or process that a file names. It loads no type and writes no log. The only host access in the core
is `File.OpenHandle` on the path the caller gives, read-only. A search of `src/` finds no `HttpClient`, `Process`,
`Assembly.Load`, `Type.GetType`, `Activator`, P/Invoke, `Console`, `Trace` or logger.

The only shared mutable state the library keeps is `PdfName`'s interned table, a `ConcurrentDictionary` safe to share
across threads, which grows for the life of the process ([#37]). Every other static value is an immutable record or
singleton. Windows and Flate buffers are rented from the process-wide `ArrayPool<byte>.Shared` and go back to it
uncleared ([#180]). A `PdfDocument` is not thread-safe, and nothing detects two threads using one.

## The reader

Everything below reads bytes the file chose: the index on opening, rebuilt by a scan if the chain fails, then
objects and stream data on demand (Lazy reading (`docs/website/docs/concepts/lazy-reading.md`)). Codes:
Diagnostics (`docs/website/docs/reference/diagnostics.md`); guards: Reader limits (`docs/website/docs/concepts/reader-limits.md`).

### Opening and the file source

| Threat | Defense | Held by | Fuzzed |
|---|---|---|---|
| An empty input, or one with no object | `PdfDocument.Open` refuses it with `PdfFormatException`, releasing a source it owns | `DocumentReaderTests`, `HostileInputTests`, `ReaderLimitsTests` | The no-object input |
| Junk before the header; a whole-file search for it or `startxref` | `%PDF-` is looked for in the first 4 KB, the bytes before it shifting every offset (without one, the file starts at its first byte); `startxref` in the last 4 KB, one missing, negative or not a number rebuilding the index | `DocumentReaderTests` | Yes |
| An offset the header's offset overflows, or puts before the header | Every offset the file gives is tested first ([#125]): negative or overflowing (read as `long.MaxValue`), it is outside the file; one the reader found names a byte even before the header. Not a guard: only an offset past the end overflows | `HeaderOffsetTests` | No |
| Indexing failing and leaking the file handle | A guard thrown while indexing releases an owned source; a rebuild finishes the index first | `ReaderLimitsTests` | No |
| Ciphertext handed out as content; an unencrypted file refused | `ThrowOnEncrypted`, on by default: `Open` refuses a document found encrypted (`PdfEncryptedException`), which is decided once, as it opens (`PdfFileReader.DecideEncryption`, [#154]): the trailer's `/Encrypt` resolves to a dictionary, or names an object one of the reader's limits kept it from reading. The trailer is the one a rebuild at opening makes where there is one, which merges a cross-reference stream's `/Encrypt` as a classic trailer's; where loading the object `/Encrypt` names sets that rebuild off, the load gives what the rebuilt index serves. One that names an object the file lacks or one that is null, which ISO 32000-1 reads as the key absent, or a value of another type leaves the document unencrypted, reported (`trailer.encrypt-invalid`). A rebuild after opening that merges an `/Encrypt` the opening trailer lacked changes nothing of it: the document is still read as unencrypted, reported (`trailer.encrypt-late`), and the validator takes its object streams for encrypted, unchecked (`CrossReferenceProbe`) | `CorpusReadingTests`, `CrossReferenceRuleTests`, `DocumentReaderTests`, `HostileInputTests`, `ReadOrderTests` | No: encrypted seeds are left out |
| Opening that reads the whole file | The index comes from the cross-reference sections; stream data is read only on demand | `DocumentReaderTests`, `CorpusReadingTests` | — |
| A caller's stream read from elsewhere than its start, or its buffer lent again | A seekable stream is read from its start, another from where it stands. Only a `MemoryStream` itself is read in its buffer; a derived one, which may lend it again, is copied | `DocumentReaderTests` | — |
| Use after dispose, or a buffer the caller then reuses | `Dispose` is idempotent; after it every member but `Diagnostics`, the validator, and whatever an object taken before reaches back to throw `ObjectDisposedException`, so no held reference reads the caller's memory. Disposal lets go of what the document kept; a reference's `Source` cannot close the file | `DocumentReaderTests`, `ValidatorTests` | — |
| Limits that allow nothing | Every `PdfReaderLimits` property refuses zero or less; `PdfReaderOptions.Limits` refuses null | `ReaderLimitsTests` | — |

Known gaps:

- [#180] Pooled buffers return to the shared pool uncleared; windows a hostile file grew stay pooled, per thread, after
  disposal.
- [#149] `Open(Stream)` copies a seekable stream into memory, as documented, where reading it in place would keep
  invariant 2.
- [#52] A source whose `Read` returns fewer bytes than asked is taken as the end of the data, unevenly: reads of ten
  bytes or fewer may hide a keyword, header or end of line from the search for a lost `endstream`, and short reads
  count several times toward `EndStreamSearchPasses`, so the search may stop short, an `endobj` unjudged, a later
  stream's data taken unsearched. The scan for the memo reads on past a short read that changes what it takes at most by
  leaving a stream unconfirmed — a piece it jumps past, inside an object its own reads delimit, or a read that
  confirms a stream's length or searches for its `endstream` —, its memo whole past it.
- [#331] The search for a classic table's trailer past rows a guard left unread rereads one window forever where it
  comes back 32 bytes or fewer before the file's end, or empty (`FileSource` on a file shrinking while open, a source
  whose `Length` exceeds its data); `StartsToken(long)` reads a byte of a window that may be empty. Looping reads
  ([#52]) do not close it.
- [#191] A reference resolves through the reader, not the document: the reader refuses once the document is disposed,
  but which source a reference carries, and what M03's change set shows through it, is unsettled.

### Object syntax

| Threat | Defense | Held by | Fuzzed |
|---|---|---|---|
| The lexer looping or reading past its buffer; superlinear look-ahead | Every token but the end consumes a byte; look-aheads check the length; an unclosed string ends at the buffer's end, told from a closed one by its length; look-ahead is a constant number of tokens, each lexed three times at most | `PropertyTests`, `LexerTests`, `ParserTests` | Property, over buffers of about 100 bytes |
| Nesting that exhausts the stack | `PdfObjectParser.MaxDepth` (128, not yet a guard: [#163]) and a stack probe at each container; past either, `SkipContainer` counts brackets without recursion, reported | `ParserTests`, `HostileInputTests` | No |
| A token or an object of any length, or one that never closes | The window grows ×8 from 8 KB up to `MaxObjectLength`, a guard (`limit.object`); what it leaves open is reported only at the data's end. Past the bound the object is kept as read, the guard reported beside what preceded the cut (ADR 34). An `endobj` ends the containers left open. One object's parses add up to about 1.3 times the last window | `HostileInputTests`, `ReaderLimitsTests`, `WindowEdgeTests`, `PropertyTests`, `SyntaxFaultTests`, `FileRuleTests` | Growth yes, the bound no; the window's edge by property |
| An object that lost its closing delimiter and `endobj`, or a trailer its `>>`, taking in what follows; a cut header read as keys | Where an element, key or value is expected, the next object's header (`HeaderText`, [#261]) ends every container left open, the object lacking its `endobj` (not in a member or a lone value). A header the window's edge cuts is the edge's; an `endobj` or `R` ending the window at `MaxObjectLength` is asked of the file: an object as long as the guard reads whole ([#174]), a parted reference is not followed ([#224], [#263]). Accepted: a closed container holding header text ends there; none of the corpus's 344,361 objects does ([#230]) | `SyntaxFaultTests`, `ParserTests`, `WindowEdgeTests`, `GuardCutValueTests` | Plausibly |
| A number past a `long` or a `double` | Never wraps: past a `long` it reads as the nearest real, past a double as an infinity, read as null, reported; a real, as the nearest double. The shape is checked before the framework's parse, which reads `NaN`; each digit is read twice at most, nothing allocated | `LexerTests`, `PropertyTests`, `ParserTests`, `HostileInputTests` | Property |
| An object number or a generation overflowing its cast | A header needs a number in 1..`int.MaxValue`, a reference 0..`int.MaxValue`, each a generation in 0..65,535 (`PdfObjectId.MaxNumber`, `MaxGeneration`: internal constants with their reasons), checked before the cast | `ParserTests`, `HostileInputTests` | No |
| Strings and names decoded past their buffer | A literal string's output is at most its input, a hex string's half plus one; `#xx` is decoded only before two hex digits in the token; a name up to 128 bytes is decoded on the stack. Refused bytes are skipped or kept, reported once a token | `LexerTests`, `ParserTests` | Property |
| Malformed syntax that throws | A key that is not a name, a missing value, an array closed by `>>`, a stray token: each becomes a null or a skipped token, reported | `PropertyTests`, `ParserTests` | Property |
| A key given many times, or many keys given null | A repeated key keeps its last value, a null removing it, in one lookup per entry, a repeat after a null seen in constant time; each repeat is reported or, past the capacity, counted | `ParserTests`, `HostileInputTests` | No |
| Faults over many objects, each formatted for a dropped report | A message, or a rebuild's note on a trailer keyword, is formatted only when the diagnostics have room, otherwise counted; one repeated as its object is reparsed is kept once | `HostileInputTests`, `SyntaxFaultTests` | No |
| Names chosen to collide in a hash table | `PdfName` hashes as an ordinal string, which the runtime randomizes per process | — | — |
| Identifiers or reports chosen to share a hash | `PdfObjectId` and `PdfDiagnostic` hash through the process's seed, not a record struct's linear hash (*Tables keyed by numbers*) | `ObjectModelTests`, `DiagnosticsTests` | No |
| A parsed value pointing into a pooled window | Values are copied out: strings into arrays, names into strings, stream data as an offset into the file | Structural | — |

Known gaps:

- [#163] Nesting past 128 levels, and loads nested past 64, are cut though a valid file can reach both; they become
  guards. The validator's dependency walk gets what such a cut left of a stream the reader first decoded at its deepest
  load, whichever read did; and as the cache is asked before the nesting, a `/Length` chain a cached link carries past
  the cut can reach a stream the reader holds, read as null (`ObjectRuleTests`).
- [#262] A reference a guard cuts between its generation and its `R` reads as two integers.
- [#279] A name, an integer or a real a guard's cut parts is kept as the part read; a `/Length` or filter value takes
  it for the guard's ([#224], [#263]). At an offset the chain names, a cut not taken for a section the guard cut (a
  `/Type` `MaxTrailerLength`, `limit.trailer`, parted; a `/W` or later `/Type` past it, [#263]; a header run past it,
  [#289]) sends the section to a nearby search, missing with nothing near, or the index to a rebuild at `startxref`,
  and may hide a `/Prev` or the chain's end at an `/XRefStm` ([#264]), no stream passed over reported. Reading a
  parted `/Type` as the guard's closes this part.
- [#36] `PdfString.ToText` misreads PDFDocEncoding, PDF 2.0 UTF-8 strings and UTF-16 language escapes.
- [#37] Interned names grow for the life of the process.

### Indexing: the cross-reference chain, trailers and the rebuild

How the chain is read is in Lazy reading (`docs/website/docs/concepts/lazy-reading.md`), the guards in
Reader limits (`docs/website/docs/concepts/reader-limits.md`), and the code or finding each shape receives in the
diagnostics reference (`docs/website/docs/reference/diagnostics.md`) and the validation rules (`docs/website/docs/reference/validation-rules.md`).
A member named without its type is `PdfFileReader`'s.

| Threat | Defense | Held by | Fuzzed |
|---|---|---|---|
| A `/Prev` chain that loops | `TryReadXRefChain` keeps every offset the file writes, and where each section the chain reached starts — read, found moved, or there and unreadable though it declares itself one ([#208]) —, and `ReachedAt` asks both: an offset that names either, or the white space before such a section, ends the chain at once with `xref.chain-cycle`, placed where the section starts; a section found moved and named again is a loop, not a place to search near again ([#188]). Two offsets past what a `long` holds are two, not one. White space at any other offset is crossed once per position, in windows grown no farther than a trailer's and 32 bytes past it | `CrossReferenceChainTests`, `CrossReferenceRuleTests`, `DocumentReaderTests`, `HeaderOffsetTests`, `HostileInputTests` | Yes |
| A chain without end | `MaxXRefSectionCount`, a guard (`limit.xref-section-count`), counts the `/Prev` links. A cut where the chain goes on outside the file is placed at the section that names it; an `/XRefStm` that names what the `/Prev` the cut falls at names is found wrong as the guard hides it, not as what the chain read there ([#156]) | `CrossReferenceChainTests`, `CrossReferenceRuleTests`, `HeaderOffsetTests`, `ReaderLimitsTests` | No |
| A section a guard stops — rows cut by `MaxXRefSectionLength` or `MaxDecodedStreamLength`, or the index filled at them — whose `/Prev` or `/XRefStm` names no section whatever the guard hid, passed over as the guard's and the guard blamed for the file's damage | The chain ends at the section, nothing past the guard read ([#264]). `JudgeLinksPastGuard` reads the links of the trailer past the rows, or of the stream's dictionary, as a whole read does, and the chain reports what a whole read reports — an offset outside the file, a loop, an `/XRefStm` that names what the chain or the table's `/Prev` reaches —, the index left incomplete; what may be sound stays the guard's: an offset in the file the chain has not reached, a reference to a row the cut left unread or to an object a guard cut ([#208], [#210]). The guards: `MaxXRefSectionLength` (`limit.xref-section-length`), `MaxDecodedStreamLength` (`limit.decoded-stream`), `MaxIndexEntries` (`limit.index-entries`). Gaps: links in a trailer `MaxTrailerLength` (`limit.trailer`) cut ([#328]), and the `/Prev` of a stream at which a cut value ends the chain ([#329]), go unjudged (below) | `CrossReferenceRuleTests`, `ReaderLimitsTests` | No |
| An `/XRefStm` that names no stream of its table's revision — a table, a stream the chain reads otherwise, what the table's `/Prev` names — passed over in silence, or read as the stream and the right `/Prev` blamed for a loop; a search near a `/Prev` taking another stream in its place; a stream passed over reported though a guard may hide what names it; a stream a thousand trailers name read again for each | Every naming is recorded in `TryReadXRefChain` ([#156]). One that names what the chain has reached (`ReachedAt`, [#188]) reads nothing: a stream a newer table named is recorded as named again, and anything else there is reported as `xref.hybrid-stream-lost`, as are a classic table read in the stream's place, its links unread ([#263]), and what cannot be read, the index left incomplete so that a rebuild finds what only the stream indexed. One that reaches what its own table's `/Prev` reaches, at the offset or where that `/Prev` keeps its place (`KeepsItsPlace`), is not followed: the `/Prev` reads the section whole, the naming reported first, or only recorded, as the guard's, where only a guard's cut stopped the search. The `/Prev`'s own search (`TryParseNearHybridPrev`, `MayBeTablesOwn`) takes a stream for the table's own only where it starts no sooner than the stream the `/XRefStm` read, told by where its digits start ([#327]), tries last one whose `/Prev` gives none or the table's, a stream before it keeping its nearest-first place, as appended updates put a revision's own stream past older sections; a dictionary a guard cut keeps that place too, the guard's section where it declared itself one (ADR 34). A stream passed over is reported once (`xref.stream-passed-over`, `ReportStreamsPassedOver`), only where it declares itself one ([#327]), the chain has not reached it, and no limit left part of the chain unread or may have hidden a link (ADR 34, [#264]): a trailer's window that cut the reading at an offset the chain names, whatever it cut ([#263], [#279], [#289]), or white space past that window ([#289]), withholds every such report in the document. A stream a thousand tables name is read once, reached places keyed through the process's seed. Gaps: [#260], [#330], [#332] and [#333], below; and the stream the `/XRefStm` read whose `/Prev` gives no offset in the file is taken, the naming reported and that `/Prev` found missing | `CrossReferenceChainTests`, `CrossReferenceRuleTests`, `GuardCutValueTests`, `HeaderOffsetTests`, `HostileInputTests` | No |
| A section offset outside the file; `/Prev` or `/XRefStm` given as a reference | Offsets are checked against the file before the header's offset is added. A `/Prev` or `/XRefStm` written as a reference is read where a section already read places its object, without loading it (`ReadWithoutLoading`), and a real with no fractional part as its integer; anything else names no section, but for one that leads to an object a guard cut or hid, which ends the chain for the guard. The `/Prev` by which the search near a hybrid table's `/Prev` tells the table's own stream is read so too ([#156]) | `CrossReferenceChainTests`, `CrossReferenceRuleTests` | Yes |
| A missing section tried at many places; the word `xref` or a header inside another object — an attached file's own table, a cross-reference stream that data quotes, a comment — taken for the section | `TryRelocateXRefSection` tries places within 512 bytes either side (`NearbySearchRadius`), nearest first, each whole within them wherever the offset falls ([#197]), the byte before them read so that a number their start cuts is no header ([#171]), a header counting where the first sign of its number lies within them — one whose run of signs starts before them, or with a `-`, is not tried —, its place reported at its digits ([#327]). Each place is asked of the memo of the rebuild's scan before anything is read there (`CheckPlace`, `ScanMemo`, below): one the scan does not take — the word or a header inside another object, in a stream's data an `endstream` confirms, a string or a comment, or in a comment or a string between objects — is no place, neither read, counted nor reported, so that a stream that data quotes is never `xref.stream-passed-over`'s ([#171]); one the scan takes as the bytes give it — in the data of a stream whose length is not confirmed, or past a string that runs past its object — is tried as any other, and a section found there is taken, unconfirmed (`XRefSectionRecord.Unconfirmed`), the validator's Error `xref.section-unconfirmed`: accepted, as the scan cannot tell such bytes from another object's without loading objects. 32 places read a document, each counted once, at its first reading (`MaxRelocationCandidates`): a place the memo skips is not read, and does not count. The radius and the count are internal constants: a valid file's sections are where its chain names them. Where a reached section starts, or a reached stream's digits past its signs ([#327]), is no candidate and spends nothing ([#188]), but for what a table's `/XRefStm` read in the stream's place ([#156]); a stream that may be a hybrid table's own is tried last, so that searches try places at most 64 times. A section a guard cut is taken where it was named; so is one that cannot be read and declares itself a section — the `xref` keyword or a `/Type /XRef` stream's header at a place the scan takes, its token starting its line as a load lexes it —, reported where it lies, such a stream handing its `/Prev` on ([#208], [#281]). The memo answers before `ScanMemo.WholeUntil`: past `MaxIndexEntries` places, a guard, or past the last object its scan passed before the end of a read that a source answered with fewer bytes than asked ([#52]), a search goes without it, and a section found there is unconfirmed; for a place it tries, within the 32, that the bound alone keeps the memo from answering, short of where a short read ended it, the search reports `limit.index-entries` once a document where the first place left out starts, or throws it under `ThrowOnLimit` (`ReachScanMemoBound`, `ScanMemo.IsPastBound`). There, at the places the chain names — what a hybrid table's `/XRefStm` read in the stream's place, and the first token at the offset its `/Prev` names, read as the chain reads what it names —, and at the places the memo holds, where the scan takes as the bytes give it a word a comment or a string holds, or what follows an end-of-file marker on its line, a place that cannot be read declares a section, and a stream passed over is reported, only where its token starts its line as a load lexes it, the line read back up to 4,096 bytes (`LineStartSearchLength`, an internal constant), past which a place that cannot be read declares nothing, whatever the memo says, and a stream passed over is reported where the scan takes it between objects, and a place the bytes the source gives end at declares nothing, as at the end of the file ([#52]). The memo is made the first time a search tries a place, one scan of the whole file once a document, which a later rebuild replays where it is whole and in the order the scan took its places (below): a sound file, and a search whose places are all passed over or named by the chain, scans nothing. An object that only gives a `/W` is a guess, dropped where it cannot be read ([#221], [#263]); one of another `/Type` is no section ([#281]). A place dropped gives back what its reading added to the index — its rows, the numbers it refused with their room under `MaxIndexEntries`, the index marked incomplete but where a guard the reading reached cut the chain ([#221]) —, its rows' offsets bounding no search meanwhile, so that dropping them sorts nothing (`PdfFileReader.RollBackIndex`, `PdfXRefTable.DropTentative`, [#320]) | `CrossReferenceChainTests`, `CrossReferenceRuleTests`, `DocumentReaderTests`, `GuardCutValueTests`, `HostileInputTests`, `ScannedPlacesTests` | Yes |
| A chain going on past cross-reference streams it cannot read: an older revision's row standing in silence for an object such a stream redefined; streams declaring thousands of subsections each, refused one after the other | A `/Type /XRef` stream whose dictionary was read whole hands its `/Prev` on ([#208]), counted by `MaxXRefSectionCount` (`limit.xref-section-count`) and reached, so that a `/Prev` naming it again loops. No older section's row stands for a number it declares: the object is found by rebuilding the index, which keeps the last definition the file writes. Refused numbers are sorted, joined ranges, merged in one pass from pooled arrays and searched by halves without allocating (`RefuseRanges`); past 4,096 ranges (`MaxRefusedRanges`, an internal constant: only a file whose sections cannot be read refuses a number) every number is refused (`RefuseEveryNumber`). 1,024 such streams cost about four times what 256 do, in time and in allocation, and a lookup among 4,096 ranges about what it costs among one. A stream a guard cut hands nothing on (ADR 34); nor does the section `startxref` names, for which the whole file is rebuilt ([#323]) | `CrossReferenceChainTests`, `CrossReferenceRuleTests`, `HostileInputTests`, `ReaderTimingTests` | Fixed-seed property |
| A section the chain keeps though it reads it only in part — a table the end of the file cuts, a row that is no row, a stream short of rows, a refused row — read as sound in silence, an older row standing for an object it lost; a table an `/XRefStm` names that cannot be read; a table with no subsection, or losing thousands; a recovered catalog reported again by each rebuild | A table the end of the file cuts is kept when it read a row of its own, and is otherwise a section that cannot be read ([#207]). A section read only in part is reported once, under `xref.section-unreadable`, the index left incomplete and nothing rebuilt at opening for it ([#210]). No section read after it stands for a number whose row it lost: those numbers are refused (`RefuseRowsLost`) as joined ranges up to `MaxRefusedRanges` (4,096, an internal constant), past which every number is, and so are those of an unreadable hybrid stream, or of a table an `/XRefStm` names that cannot be read ([#156]). A table with no subsection indexes nothing and is reported as `xref.section-empty` ([#222]). `trailer.root-recovered` is not reported again for the object a rebuild after opening takes again | `CrossReferenceChainTests`, `CrossReferenceRuleTests`, `GuardCutValueTests` | No |
| A classic table of any length | `TryReadXRefSection` grows the window ×4 from 64 KB up to `MaxXRefSectionLength` (`limit.xref-section-length`), keeping the rows read, a table it cuts the guard's whatever the index holds. A table the guard cuts, or whose rows run past a full index, keeps its trailer, found past the unread rows by `ReadTrailerPastRows` a megabyte at a time, each byte looked at once, so that its `/Encrypt` still refuses the document. The chain ends there, since older rows would stand for what the unread rows define, and what the trailer names is judged without reading past the guard ([#264]); what those rows place reads as null until the guard is raised | `CrossReferenceChainTests`, `DocumentReaderTests`, `ReaderLimitsTests`, `WindowEdgeTests` | No: seeds stay under 120 KB |
| A subsection claiming a huge count, or numbering objects past `int.MaxValue` | In `TryReadXRefSection`, the count is not negative, and the first number and the last, compared by subtraction so that a count as large as a `long` cannot wrap, are at most `int.MaxValue`; past either, the table is malformed. The rows are bounded by the window, the entries by `MaxIndexEntries` (`limit.index-entries`), and a count claiming more rows than the table holds makes it malformed where the rows end. A row giving an object in use a generation outside 0..65,535 is refused alone, left to the rebuild; a free row is read whatever its generation, as producers give the free list's head 65,536 | `CrossReferenceRuleTests`, `DocumentReaderTests` | Yes |
| A trailer, or a cross-reference stream's dictionary, of any length | `MaxTrailerLength`, a guard (`limit.trailer`), bounds a trailer and a stream's dictionary, read from its object's header, not its data ([#220]). The chain ends at a section the guard cut, or whose trailer it cut — but for the trailer of a classic table an `/XRefStm` names, whose links the chain never follows ([#263]) —, what only older sections index reading as null until the guard is raised. A `/Root` a guard hid is no fault of the file: the catalog is looked for among the indexed objects, nothing rebuilt, `file.root-invalid` silent; one naming an object a guard left out is taken as read ([#175]). Rows a guard cut that run to no trailer are the file's fault, rebuilt for. A link written as a reference that leads to an object `MaxObjectLength` (`limit.object`) cut, or to a row the guard may have left unread, ends the chain for the guard ([#263]); a hybrid stream's own `/Prev` is read only to tell its place ([#156]). A section the guard cut is not looked for nearby, but a `/Type` the cut parts is read as the part read ([#279]), and a header it cuts reads no object ([#289]), so the section is then looked for as missing; any such cut withholds every report of a stream passed over ([#264]). White space before a section is bounded apart by `MaxTrailerLength`, and a table's counts toward `MaxXRefSectionLength` (`limit.xref-section-length`) too; past the bound, the section is looked for nearby, and the index rebuilt for the one `startxref` names ([#289]) | `CrossReferenceChainTests`, `CrossReferenceRuleTests`, `GuardCutValueTests`, `HostileInputTests`, `ReaderLimitsTests` | Yes |
| What the chain refers to — a stream's dictionary written with references, an indirect `/Length`, a relocation candidate's, a trailer followed by `stream` — loading objects while the index is still being read, and a correction, a rebuild or a null cached then served as what the file wrote | Nothing loads while the chain is read (`ReadWithoutLoading`): an object is read where a section already read places it, under the reference's number and generation, within `MaxObjectLength` (`limit.object`) and `MaxNestedLoads` (64, an internal constant a valid file can reach, [#163]), not as a stream, its references followed no further than `PdfReference.Resolve` follows them; nothing is corrected, rebuilt, cached, recorded or reported. A value the rows need that leads to an object `MaxObjectLength` cut makes the section the guard's, the chain ending there, nothing rebuilt, the object loaded once the chain is read, which reports the guard or throws it under `ThrowOnLimit` ([#263], [#224]); a `/Length` that cannot be read so is checked once the chain is read ([#224]). An object read at a guessed place and dropped leaves nothing of what it read through ([#221], [#263]); one read where the chain names keeps its reading ([#156]). What makes a section unreadable whatever a cut value holds is the file's fault, judged before the cut is taken for the guard's (`FaultBesideCut`, [#221]) | `CrossReferenceChainTests`, `CrossReferenceRuleTests`, `DocumentReaderTests`, `GuardCutValueTests`, `HostileInputTests` | No |
| A cross-reference stream whose `/Length` the chain cannot read: its data cut short by rows that spell `endstream` or a header, or read to the end of the file as rows; a guard's cut of that `/Length` charged to the file | Its data runs to its first `endstream`, searched for once, up to the next object the sections read so far place or the end of the file, as any stream's whose length is not confirmed (*Stream data and its length*), but stopped by no object header its rows spell; with none before that stop, the section is malformed, the file's fault, or the guard's where the `/Length` leads to an object `MaxObjectLength` (`limit.object`) cut ([#224]). Rows that spell `endstream` are told from the data's end by the row its own rows give for the `/Length` ([#221]); where `MaxObjectLength` cut that row's object, the rows past the `endstream` are the guard's, unless what follows proves it the stream's own (`EndsAtOwnEndStream`), when the missing rows are the file's ([#224]). Data that lacks rows because `MaxStreamLength` (`limit.stream-length`), `MaxDecodedStreamLength` (`limit.decoded-stream`) or `MaxFilterChainLength` (`limit.filter-chain`) cut it is the guard's; where a guard hides where the data ends, it is decoded again as data a guard cut, so that no row is read from bytes a guess completed ([#220]) | `DocumentReaderTests`, `GuardCutValueTests`, `HostileInputTests`, `ReaderLimitsTests` | No |
| `/W` widths that overflow a field or give empty rows; `/Size` or `/Index` claiming rows the data lacks, or numbering objects no object can be; row fields an entry cannot hold; data its filters decode nothing of read as rows, the object such a row numbers served as null over an older section's row | In `TryReadXRefStream`: at least three widths, non-negative integers not all zero, narrowed only once known to lie within the decoded data, so that their sum stays within a `long` (`WidthFault`); `/Index` pairs of integers numbering rows within 0..`int.MaxValue`, compared by subtraction, or else a non-negative `/Size`; a fault here makes the section malformed before any row is read (`NumberingFault`). Data its filters decode nothing of holds no row, the stream unreadable, its `/Prev` handed on and its numbers refused ([#221]); data decoded in part holds the rows that part does ([#210]). Rows are read only while they fit in the decoded data. A row no entry can hold is refused alone; a row of a type past 2 is read as ISO 32000-1 reads it, a reference to the null object (7.5.8.3), its number indexed as free, its section reported once by the reader and the validator ([#221]). A row whose number was refused before stands for nothing ([#208], [#210]), and a stream an `/XRefStm` and a `/Prev` both read is judged as first read ([#156]) | `CrossReferenceChainTests`, `CrossReferenceRuleTests`, `FilterTests`, `HostileInputTests` | Seldom: the rows are compressed |
| Entries without end — rows of one byte decoding to 256 MB from a few hundred bytes, refused rows, rows of a reserved type, a rebuild's headers, the members of the object streams a rebuild expands | `MaxIndexEntries`, a guard (`limit.index-entries`), counts the distinct object numbers the index holds — in use, free, rows of a reserved type among them ([#221]), or refused — in `AddFromChain`, `Refuse` and `HasRoomInRebuild`: a number held already takes no room, so that millions of copies of one header hide nothing, and a rebuild reads the file to its end. The first number with no room is reported once; the chain stops at the section that filled the index, a stream's rows past it unread ([#164], [#183]), and what that section names is judged without reading past the guard ([#264]) | `CrossReferenceChainTests`, `HostileInputTests`, `ReaderLimitsTests` | Yes |
| A declared count the index makes room for before its rows are read | A section makes room once, before its rows are read (`ReserveIndex`): no more rows than a stream's data, or a classic table's bytes at 19 a row, hold; no more numbers than `MaxIndexEntries` (`limit.index-entries`) lets the index take, nor than the newest section's direct `/Size`. A `/Size` the sections read so far number past is stale, and from then on bounds nothing, so that a chain whose older sections each number one further doubles the index rather than copying it at each ([#155]). Room grows at least twofold, but for the last step, and at most fourfold. Rows that index nothing still get room ([#221]): at most 44 bytes a number the guard allows, and 4 KB once a document. A refused number is held apart, with the ordinal of the reading that refused it ([#210]), about 60 bytes a number, until the chain is read | `DocumentReaderTests`, `HostileInputTests`, `ReaderLimitsTests`, `XRefTableTests` | Yes |
| A rebuild run again and again | At most one per document (`Rebuild`), and none once a validation's first rule has begun: the validator has it run before, where a later read still could ([#237]). A missing object rebuilds only when the index is known to be incomplete through damage: what a guard left out — past `MaxXRefSectionCount`, `MaxXRefSectionLength`, `MaxStreamLength`, `MaxDecodedStreamLength` or `MaxIndexEntries`, or rows that need a value `MaxObjectLength` cut ([#263]), each reported under its `limit.*` code — reads as null, unknown to the validator rather than missing, each later lookup throwing under `ThrowOnLimit`. A rebuild damage forces on a chain a guard cut keeps the chain's entries the scan does not find. The chain ends wherever the index fills or a stream's rows are cut ([#264]); a cut is known by the count of guards reached, which full diagnostics do not drop. A catalog or an object stream a guard hid is no reason to rebuild | `CorpusValidationTests`, `CrossReferenceChainTests`, `CrossReferenceRuleTests`, `DiagnosticsTests`, `DocumentReaderTests`, `ReadOrderTests`, `ReaderLimitsTests` | Yes |
| A rebuild after opening, which a read or the start of a validation sets off, changing what a caller holds: an enumeration of `ObjectNumbers` or `Diagnostics` under way, or a trailer taken before ([#167]) | A rebuild changes what the document answers next, never what it handed out before or hands out after it: an enumeration of `ObjectNumbers` reads the numbers the index held at its first step, one of `Diagnostics` the entries recorded when it began, and trailers merge into a copy, so that a trailer taken before or after never changes, but for one a caller's own source takes inside the rebuilding read. A rebuild the source fails runs again at the next read or validation (`Rebuild`), settling at the next validation, a rebuild withdrawing what it reported since its scan began, or only what the loads in flight and the finish reported where they waited for it ([#273]). Rerunning a failed finish from the scan spares a sound rebuild a copy of the index, 40 bytes an entry, for a mark of the reports of some 800 bytes, plus about 35 a length fault or guard reported before. Until it runs again, `Trailer`, `Catalog`, `ObjectCount` and `ObjectNumbers` describe the unfinished rebuild, `Catalog` giving the object the file's own `/Root` names, a catalog or not; `Count`, `ObjectCount` and `WasRepaired` stay live, as documented. `IsEncrypted`, decided as the document opened, changes with no rebuild ([#154]) | `DiagnosticsTests`, `HostileInputTests`, `ReadOrderTests`, `XRefTableTests` | No |
| The rebuild's scan: a header a piece's edge cuts or parts from its line; a header or a `trailer` keyword inside another object — an attached PDF written as it is, a string that quotes one, a comment — taken for the file's own, its objects served in place of the document's; a header whose numbers carry signs lost or misread; a piece read through a window twice its size | `ScanForObjects` reads the file forward in pieces of `ScanChunkSize` (1 MiB) exactly, each from `ScanLead` (64) bytes before where its search resumes, the last byte of a piece read only to tell an `obj`, an `xref` or a `trailer` that ends the bytes searched from a longer word, so that an `obj` white space, a delimiter or the end of the file does not end is no header ([#189], [#350]); a keyword that runs past the bytes searched is the next piece's to find whole. A header is read backwards from its `obj` as a load lexes its tokens (`ReadHeaderBackwards`): its number and its generation are integer tokens, a run of digits after the run of signs it may be written with, the first sign deciding ([#327]), each judged by its value as the parser judges it, a number in 1..`int.MaxValue`, a generation in 0..65,535; the byte before the number ends a token — white space, `)`, `>`, `[`, `]`, `{`, `}` (`PdfCharacters.EndsToken`) —, so that digits a regular character, a string, a name or a comment runs into are none, but for the end-of-file marker `%%EOF`, a comment that ends with itself, to which a writer that appends an update with no end of line glues its first header ([#171]); and a comment may lie between its tokens, a line that holds a `%` lexed from its start as a load reads it ([#261]). A walk lexes a line once at most, linear in the bytes it is given and allocating nothing (`ObjectScanBenchmarks`). One that reaches the start of a piece undecided is read again from further back (`ReadHeaderFurtherBack`), in windows eight times larger each time, no further than `MaxObjectLength` from its `obj`, past which it is none, unreported, as a load reads an object no further, nor than where the object before it ends, from where a load's lexer reads tokens anew — in its piece as from further back —, nor past what one window holds, and a window the source gives shorter than asked ends it with no header, the scan going on past the keyword in either mode, so that a rebuild that replays the memo takes what one that scans takes ([#52]): `ScanLead` sizes a read and bounds nothing, and where a piece starts changes nothing of what the scan takes, a `trailer` keyword a piece's end cuts included, once skipped and its trailer left out of the merge ([#171]). Each object taken is delimited as a load reads it (`DelimitObject`, `DelimitWithin`): its header and value lexed, nothing built, the token after a value lexed only where a run of regular characters can change where it ends — a generation, `R`, `obj`, `stream` —, never a string after it, and no comment that holds the word `obj` crossed, inside a value or after it, no more lexed than `MaxObjectLength` whatever the piece holds, a value its piece cuts read again from its header in windows eight times larger up to `MaxObjectLength`, and, for a stream, its data, where an `endstream` past white space and comments confirms its direct `/Length` (`ConfirmStream`, white space and comments read as a load reads them, past a first read with the scan's own reads past declared lengths, bounded apart from the loads' searches at `EndStreamSearchPasses` times the file and nothing kept of them, [#174]); the headers and `trailer` keywords within are none, and the scan goes on past the object ([#171]); a string that runs past its object's end — one that holds `endobj`, as an unbalanced parenthesis left unescaped runs it into the next objects, or one the end of the data leaves open — delimits nothing (`IsRunaway`), and what it runs into is read as the bytes give it, no comment nor string swept there; one it cannot delimit so — a value that runs past `MaxObjectLength`, a header or a value no load reads — has what follows its header read as the bytes give it, and the headers within what its delimiting read are taken undelimited, so that no byte is lexed for one object after another. The next `obj` and the next `trailer`, and in a scan for the memo the next `xref`, are each searched for once in a piece, whichever comes first taken. Between objects, the bytes are swept as a load lexes a line (`SweepLine`): a comment, or a string, runs to the end of its line, which ends both — `%%EOF` ends with itself —, and a header or a `trailer` keyword that starts in one is none; the scan's own sweep crosses each byte between objects once, from the window where it holds them, and from the file, a piece at a time, where it no longer does, and a scan for the memo sweeps ahead to each `xref` keyword as well, a byte once more at most, which the scan's own sweep never takes up, so that a rebuild sweeps as before (`IsHiddenAhead`). A `trailer` keyword counts only as a whole token. Accepted: a stream whose length the scan cannot confirm — an indirect `/Length`, or one no `endstream` follows — has its data end where a load's search for its end stops, at the first header, which is taken, or at its first `endstream`, each byte searched for it once however many such streams precede it, the bytes the scan holds first, then the file in reads that grow from 64 KB to a piece (`FindEndStream`); the headers and `trailer` keywords before that `endstream` — or, for a direct `/Length`, before where a load ends the data at that length or at its object's `endobj`, its search finding no `endstream` before the first header (`DataEndWithoutEndStream`, the load's own search, read through the file from the data's start, its reads counted with the scan's reads past declared lengths and none made once those reach their bound) — are taken as the bytes give them, comments unread. Every search near an entry's offset, and near an offset the chain names for a cross-reference stream, reads a header as the walk does. What the scan takes — each header, each `xref` keyword that starts a token outside the objects it delimits and the comments and strings between them, swept apart so that a header is told as before, and each `trailer` keyword — is kept for the document's life (`ScanMemo`), made only when something asks for it — the searches near an entry's offset and near an offset the chain names (`GetScanMemo`) —, by a scan of its own from the file's start, which depends on nothing read before; a rebuild never makes it. 16 bytes a place, sorted, places of one header at one distance in two slots however many, a header whose tokens run back across places taken before it put in its place, the array doubling as it grows, then trimmed, and, where the memo is whole, the trailers kept with it, 8 bytes for each `trailer` keyword and for each cross-reference stream's header, in lists trimmed too, which keep up to a ninth more room than they use: under 25 bytes a place, about 201 MB at most at the default bound, and from two to five times what the memo keeps allocated while it grows (`ScanMemo`; `ObjectScanBenchmarks` measures the scan for the memo beside the rebuild's); at most `MaxIndexEntries` places, each definition of a number and each keyword counted, a guard, at the first of which a scan for the memo stops, the memo whole only before it (`ScanMemo.WholeUntil`), and, where a source answers with fewer bytes than asked a read that tells what the scan takes — the piece it stops at, the walk back from an `obj`, the sweep between objects or ahead to an `xref` keyword, the reading again of a value its piece cuts, or, with nothing, the search for where a load ends a stream's data —, only before the end of the data it gave or where the bytes the scan's last object left to read anew start, if sooner, since a header that end cuts may start there, an integer value the end of the data follows within the tokens after it ending before that integer ([#52], `ScanMemo.Builder.EndAt`, `DelimitInteger`); making the memo reports and throws nothing, the guard reported under `limit.index-entries`, apart from the index's report, only by a search that has to go without the memo's word past there, for a place the bound alone keeps it from answering, short of where a short read ended it (`ReachScanMemoBound`, `ScanMemo.IsPastBound`). A rebuild takes its headers and trailers from a memo that is whole and in the order the scan took them, reading no byte for its scan, and replays it again where the source failed it and it runs again; with no memo, or one not whole or not in that order, it scans exactly as before there was a memo — no search for `xref` keywords, nothing kept, nothing allocated beyond the index —, so that the memo's bound changes nothing a rebuild serves or reports. A sound document pays nothing for the memo; one whose searches ask for it before a rebuild, one scan of the file; one rebuilt first and asked for it afterwards, two ([#171]). Redefinitions are counted, with the first ten numbers, in a fixed-size record, reported once as `object.redefined`; the definition the file writes last is kept, an object stream's member ranking where its stream lies, through a map of the streams taken in, one entry each ([#189]) | `CorpusReadingTests`, `CrossReferenceChainTests`, `CrossReferenceRuleTests`, `DocumentReaderTests`, `HostileInputTests`, `PropertyTests`, `ScannedPlacesTests`, `WindowEdgeTests` | Partly |
| The rebuild's expansion of the object streams: a stream whose own number a later object stream redefines giving members, an earlier readable copy hidden behind them; what was read through a definition the expansion replaces kept; a result that depends on the order the numbers were met in | `ExpandEachObjectStream` loads every object the scan placed to tell the object streams, then takes them in from the last written to the first, each member taking a number where it outranks the definition held — a later position wins, a stream taken in ranking where it lies —, so that a stream whose number a later stream lists is no longer one and gives no members ([#348]). A stream whose dictionary names an object the index does not hold yet, within three levels of its filters' parameters (`NamedDepth`, an internal constant: what lies deeper decodes no stream), waits until the others are taken in, then is taken in with those that wait, in the order the streams were met in, loaded again first. A replaced number lets go of its object, its notes and its decoded contents, and leaves the object streams a guard hid (`ForgetObject`); once any definition was replaced, every object read is let go and read again from the final index (`ForgetObjectsRead`), a stream's indirect `/Length` or an object stream's `/N` among them. The members take room under `MaxIndexEntries` (`limit.index-entries`) as the scan's headers do | `DocumentReaderTests`, `ObjectRuleTests`, `ReadOrderTests` | No |
| A catalog a guard cut — its `/Type` or its `/Pages` past the cut —, which leads to no catalog as read | The object `/Root` names, through references too, is taken for the catalog as read — one an object stream's cut leaves unread ([#259]), one written in a trailer `MaxTrailerLength` (`limit.trailer`) cut —, at opening and after any rebuild: no search among the indexed objects, which could take a superseded catalog, and no rebuild, which would read it cut again; `FileStructure.RootCutByGuard` records it, and `file.root-invalid` does not judge it ([#175]). A rebuild after a search reads the file's own `/Root` again. A rebuild keeps the members the chain's index places in an object stream a guard hides, unread for the guard's reasons | `FileRuleTests`, `ReaderLimitsTests` | No |
| A guard reached in the middle of a rebuild; the trailers it merges taken from inside other objects, or missing a revision's | The guard's exception is held and thrown once the index is whole, the first reached the one thrown. The merge of the trailers (`MergeScannedTrailers`) reaches none. It merges, newest first, the trailers the scan took: the dictionary after each `trailer` keyword, one the end of a piece of the scan cuts among them ([#171]), and the `/Root`, `/Encrypt`, `/Info` and `/ID` of each cross-reference stream's dictionary, which ISO 32000-1 makes a revision's trailer (7.5.8.2), its own keys left out ([#154]); none inside an object the scan delimits, nor in a comment or a string between objects, nor at the end of a name ([#171]). The newest merged is where the rebuilt trailer lies (`TrailerLocation`). It parses the dictionary after a keyword alone and loads nothing, a stream's data and `/Length` left unread, a reference in its place noted, not followed, and a cross-reference stream's dictionary after its header, noting nothing. A stream nested in a keyword's dictionary ends the parse at its whole `stream` keyword, not one the 64 KB window's edge cuts out of a longer one ([#49]), the keys from it on not merged, which is noted; past the parser's nesting (`PdfObjectParser.MaxDepth`, 128, or the stack probe) the container is skipped as too deep ([#163]), what follows a `stream` keyword left after it merged. Every key such a dictionary gives is merged, not only a trailer's; the merge adds only keys a trailer the chain read lacks, and replaces only a `/Root` that leads to no catalog (`trailer.root-recovered`). So it merges the same trailers whatever reads are in flight, and whether the document was opened to throw on a guard or not | `CorpusReadingTests`, `DocumentReaderTests`, `HostileInputTests`, `ParserTests`, `ReadOrderTests`, `ReaderLimitsTests` | No |
| Settling the index before a validation, reading every entry the file gives | Once per document (`SettleIndex`), over the entries in use that place an object in the file: each header is read as a load looks for it, through the one decision the validator's probe makes too (`PlaceHeader`, [#175], [#256]) — 64 bytes, then windows eight times larger up to `MaxObjectLength` (`limit.object`), and the 512 bytes either side when it is not there. A header past `MaxObjectLength` is reported under `limit.object`, looked for nearby, then rebuilt for, or under `ThrowOnLimit` left to the read that reaches it. Up to sixteen runs of white space crossed are remembered, so that rows placed in blocks of zeros read each once. It stops at the first entry a load would rebuild for, and rebuilds then; nothing is parsed past a header, allocated or recorded. Past each entry's first windows, read free over the entries `MaxIndexEntries` (`limit.index-entries`) bounds, the readings consume at most twice the file's length (`HeaderReadingFactor`), beside the one scan of the whole file a search near a row may ask for, once a document (`GetScanMemo`), past which it rebuilds in one pass: an internal bound, since a valid file's headers, each at its own offset, consume the file's length at most together ([#237], [#175]) | `CrossReferenceRuleTests`, `HostileInputTests`, `ReadOrderTests` | No |
| Object numbers chosen to share a bucket of the index | `PdfXRefTable` finds a number in a slot while numbers run in order, and past them through a map hashed through the process's seed: subsections, or objects with no table, numbered to share a bucket of the index's map or the rebuild's, open in about the time the same numbered in order do, and the subsections validate so too ([#155], *Tables keyed by numbers*) | `ReaderTimingTests`, `XRefTableTests` | No |
| Searches for the next object turning quadratic | `SortedOffsets` keeps sorted runs, a logarithm of them, and searches each by halves | `SortedOffsetsTests` | Fixed-seed property |
| Validation judging an index the reader repaired | The chain's index is copied before the first change the reader makes (`PreserveChainIndex`); the walk of the object streams' dependencies (`ObjectStreamDependencies`) reads that copy for each lookup, not the index the reader holds now ([#128]). Each object it leads through is read outside the reader's cache, once a validation however many streams lead through it, and kept for as many objects as that cache holds, one let go read again when met again ([#201]). Keeping the chain's index beside a rebuild's doubles what the index holds, within twice what `MaxIndexEntries` (`limit.index-entries`) allows: settling an 845-byte file whose one-byte rows fill the guard takes the 320 MiB it holds open to 640 MiB before the first rule ([#181]) | `CrossReferenceRuleTests`, `HostileInputMemoryTests`, `HostileInputTests`, `ObjectRuleTests`, `StreamLengthTests` | — |

Known gaps:

- [#47] Each section is read through a window of up to 64 KB, whatever its size; and the probe for a classic table's
  `xref` keyword past the white space an offset falls short by reads again from the offset as it grows, eight times
  larger each time up to `MaxTrailerLength` and 32 bytes past it: less than ten times what it crosses, once per section,
  and never at a place a search near an offset tries ([#188]).
- [#49] A rebuild reads a 64 KB window at every `trailer` keyword.
- [#322] A classic table whose `trailer` keyword no dictionary follows is read as sound: its `/Prev` is lost and the chain
  ends there, but nothing marks the index incomplete, so what only the older sections index reads as null, unrebuilt.
- [#324] A classic table a `/Prev` names that holds a row that is no row, a subsection header that gives no count, or a
  stray token among its rows ends the chain there, though its trailer, a few bytes on, could be found: every older section
  goes unread and unjudged, and what only they index costs a rebuild of the whole file.
- [#323] When `startxref` names a section that is there and cannot be read at all, or an object that gives a `/W` and
  another `/Type`, the index is rebuilt as the document opens and only `xref.rebuilt` is reported, not why: the fault
  reaches the validator alone. A section `startxref` names that was read only in part is kept and reported ([#210]).
- [#328] In a classic trailer `MaxTrailerLength` cut, a `/Prev` written whole before the cut is never judged, where a whole
  read reports a section outside the file or a loop; and an `/XRefStm` there is followed as if whole, though the cut may
  have parted its digits, and what it then names is blamed on the file.
- [#329] A cross-reference stream at which a guard's cut of a value its rows are read with ends the chain never has its
  `/Prev` judged, though its dictionary was read whole: a `/Prev` that is no offset, lies outside the file or names the
  stream itself, which a whole read reports, goes unreported under the guard.
- [#330] A hybrid file's newest table whose `/XRefStm` names the stream an older table's `/Prev` names has that stream read
  as its own: the older table's sound `/Prev` is then reported as a loop, `xref.chain-loop` an error at that table, and
  nothing reports the `/XRefStm`, which is the fault — the order [#156] reports, mirrored.
- [#332] A `/Prev` a few bytes off a stream a newer table's `/XRefStm` read never takes it, passing it over as reached,
  whether the table's own `/XRefStm` names it again or not. Where the search takes the table's own stream past it, which
  gives no `/Prev`, the chain ends there, and where the catalog is lost with the older sections the index is rebuilt as
  the document opens and the validator says the file holds none (`file.root-invalid`, [#260]); where it finds nothing
  else within the 512 bytes searched, the section is reported missing, the index rebuilt, and the validator says it is
  not found (`xref.section-not-found`), where `main` reads the chain whole.
- [#333] An older revision's cross-reference stream before the one a hybrid table's `/XRefStm` read, nearer the offset the
  table's `/Prev` names than its own revision's table, is taken by the search near that offset, and that table is never
  read. Where the stream gives a `/Prev`, the chain goes on through it, what only the table indexes reading as null,
  nothing rebuilt, nothing reported but the section found moved. Where it gives none, the chain ends there, what only
  the sections it skips index reading as null or as the stream's older revision, unless the catalog is lost with them:
  the index is then rebuilt as the document opens, and the validator says the file holds no catalog
  (`file.root-invalid`, [#260]). Where the stream the `/XRefStm` read, and every stream past it, gives no `/Prev` or the
  table's, a first revision's stream may be taken farther off within the 512 bytes searched, the revisions between lost
  with nothing reported but the section found moved and the streams passed over, where `main` rebuilt the index as the
  document opened and served them.

### Resolving objects and object streams

| Threat | Defense | Held by | Fuzzed |
|---|---|---|---|
| A load needing itself (a `/Length` naming its stream), or loads chained without end | `PdfFileReader._loading` reads a load of an object already loading as null; `MaxNestedLoads` (64, not yet a guard: [#163]) and a stack probe bound the chain, reported once | `HostileInputTests` | The load path, not the bound; `/Length`: no |
| Decodes nested through filter parameters, each holding its output | A stream resolves every value its filters read before its data: a chain of loads holds what `ObjectStreamBudget` keeps, the last object stream decoded and one decoding ([#165]); none keeps its raw data ([#170]) | `FilterTests`, `HostileInputMemoryTests` | No |
| Objects numbered to share a cache bucket | The cache, decoded object streams and every set the reader keys by number or offset hash with the process's seed (*Tables keyed by numbers*, [#155]) | `ReaderTimingTests`, `SeededComparerTests` | No |
| References to references that loop | `PdfReference.Resolve` reads null after 32 fetches (`MaxChainLength`), unreported ([#173]) | `ObjectModelTests` | No |
| An object stream needing an object it holds, or two needing each other | While an object stream is decoded, an object it holds reads as null, reported once, remembered for later decodes | `HostileInputTests`, `HeaderOffsetTests` | Object-stream seeds, not the cycle |
| An offset table that lies | `/First` is checked against the data; `/Type` must be `/ObjStm` ([#274]). The header is read, whatever `/N` says, to its first pair that is no valid number at a rising offset in the data ([#259]), by reader, rebuild and validator alike ([#202]), the last counting the objects lost ([#275]); its tables grow within what `/First`'s bytes hold ([#160]). A wrong index falls back on a lookup by number built once (`ObjectStreamPlaces`), shared by the validator: linear time | `ReaderTimingTests`, `DocumentReaderTests`, `HostileInputTests`, `CrossReferenceRuleTests`, `ReadOrderTests` | Yes |
| A member that runs into the next, or for megabytes | A member is read to the next one's start, so reads cover the data once ([#200]), and no further than `MaxObjectLength` (`limit.object`, [#160]), a token at the window's end asked of the byte past it ([#224]); a cut is reported once per member and definition, or thrown each time if the document throws | `SyntaxFaultTests`, `ReaderLimitsTests`, `HostileInputTests`, `ReaderTimingTests` | Yes |
| A stream inside an object stream, which ISO 32000 forbids | The parser stops at `stream`: the object reads as null, nothing decoded nor kept, reported once, as is its entry by the validator ([#161]) | `DocumentReaderTests`, `HostileInputTests`, `HostileInputMemoryTests`, `StreamLengthTests`, `CrossReferenceRuleTests`, `ReadOrderTests` | Object-stream seeds, not a stream member |
| Many large object streams held at once | `PdfFileReader.ObjectStreamBudget` (32 MB, an internal constant with its reason) weighs each stream's data with its header's tables ([#160]); the oldest let go, decoded again on demand, and no member keeps their data alive ([#161]) | `HostileInputTests`, `HostileInputMemoryTests` | No |
| A stream decoded again and again, reporting each time | The document holds each decoding's reports once (`PdfDiagnostics.MoveDecodingReportsTo`); a guard is counted, or thrown, each time, so its cut is never taken for damage ([#144]) | `HostileInputTests`, `DiagnosticsTests`, `GuardCutValueTests`, `CorpusReadingTests` | No |
| Parsed objects piling up | The cache is FIFO, bounded by count (`PdfReaderOptions.ObjectCacheCapacity`, 8,192), not weight ([#37]), keeps no stream's data ([#170]), and is keyed by number: an object named under many generations is parsed once | `HostileInputTests`, `StreamLengthTests` | No |
| An object not where its entry says; a header inside another object — a string, a comment, an attached file's data —, or a stale definition near it, served in its place | The entry is checked against the file: a row the file gives that cuts a number — a digit before it, and a digit or a sign at it, as `14 0 obj` named from its `4` — is not read first, for a load, settling and the validator alike, a row's first read taking the byte before it for that and reading nothing more there (`PdfFileReader.RowCutsNumber`, `PdfFileReader.ByteBeforeRow`), and a place the reader found itself is not asked. Else `PdfFileReader.TryFindObjectHeader` looks within 512 bytes either side (`PdfFileReader.NearbySearchRadius`, [#197]); no search for an offset more than 512 bytes before the file's start or past what a `long` holds. Its candidates are the headers of the object's number the rebuild's scan takes there (`ScanMemo`), each lying whole within them: none inside a string, a comment or a stream's data an `endstream` confirms, nor one whose number the window's start cuts, but one glued to an end-of-file marker that the window's start cuts, the 5 bytes before the window read for it ([#171]); comments between a header's tokens read as a load does ([#261]). The memo is asked for only where those bytes hold a header of the number, read back from its `obj`: a row with none near it costs no scan. A candidate the scan takes between objects ranks before one it takes as the bytes give it; within the best rank, one a row beside the entry in the chain's index as written supports — the header of that row's own object as far from it, as the memo holds it, nothing read — wins, else the nearest, the one before the offset on a tie, so that the choice depends on nothing read before; 129 candidates at most (`PdfFileReader.MaxNearbyHeaders`, an internal constant: a header takes 8 bytes, the bytes searched 1,024). A choice the search cannot vouch for — a candidate taken as the bytes give it, or one of several between objects that no row supports — is served all the same, and is the validator's Error `xref.entry-unconfirmed` (`HeaderConfirmation`). Where the row cuts a number and the search finds no candidate, the row is read as written, a header glued to the digits before it — an update appended with no end of line to a file cut after a digit — taken, as a reader that lexes from the row takes it, and the validator's Error `xref.entry-unconfirmed` as well (`PdfFileReader.TryParseWhereEntryPlaces`, `PdfFileReader.PlaceHeader`): the reader cannot tell it from a row inside another header's number, whose own header no search found. Past where the memo is whole (`ScanMemo.WholeUntil`), a header is taken as the bytes give it — past the last object the scan passed before the end of a read that a source answered with fewer bytes than asked ([#52]), or past `MaxIndexEntries` places, a guard —, and, for a header the bound alone keeps the memo from answering for, short of where a short read ended it (`ScanMemo.IsPastBound`), the bound is reported under `limit.index-entries` once a document where the first place left out starts — by a load, which throws it under `ThrowOnLimit`, by settling but under `ThrowOnLimit`, and by the validator's reads of what the chain places, never by its probe of the entries (`PdfFileReader.ReachScanMemoBound`). Then the index is rebuilt once: three counted attempts. A moved entry records the generation of the header chosen. Accepted: a row that names a header exactly is trusted, whatever lies around it, a comment or a stream's data included, and makes no memo: a load cannot tell without the scan, which lazy reading forbids for each load | `CrossReferenceRuleTests`, `DocumentReaderTests`, `HeaderOffsetTests`, `HostileInputTests`, `ReadOrderTests`, `ScannedPlacesTests` | Yes |
| A reference to what the index lacks, rebuilding each time | A rebuild runs only when damage shows the index incomplete, and once; what a guard left out reads as null without one | `CrossReferenceChainTests`, `ReaderLimitsTests` | Yes |

Known gaps:

- [#181] Nothing bounds the work one document asks: two large object streams evict each other on every member, and
  objects are parsed again through windows grown to 16 MB.
- [#173] A chain or a loop of references reads as null with no report, one reference short of its bound.
- [#37] The cache is bounded by count, not weight, and a parsed object weighs about thirty times its syntax.
- [#121] Each object is read through its own 8 KB window, however small.
- [#185] A reference's generation is compared with nothing: `5 1 R` reads `5 0 obj`, unreported.

### Stream data and its length

| Threat | Defense | Held by | Fuzzed |
|---|---|---|---|
| A `/Length` negative, past `int.MaxValue`, not an integer or unresolvable | Only 0..`int.MaxValue` is a length; anything else sends the stream to the search | `StreamLengthTests` | Partly |
| A `/Length` a guard cut or left out of the index (hidden), judged by what is left | Keyed on the object the reference chain leads to, not the value read (`StreamLengthForm.ObjectCut`, `ObjectUnindexed`): the data ends at its `endstream`, the length's fault is the guard's, a missing `endstream` the file's but for a stop the data may spell (below, [#224]) | `GuardCutValueTests` | No |
| A declared length past the file | Checked before the file is asked, it confirms nothing: the stream goes to the search; its length is clamped to the file | `HostileInputTests`, `WindowEdgeTests` | Yes |
| A wrong `/Length` growing the window to `MaxObjectLength`, then reported cut | Stream data never counts toward `MaxObjectLength`: no window grows to find `endstream`; data past the bytes held is searched in the file, its length's fault the file's ([#174]) | `StreamLengthTests`, `WindowEdgeTests`, `HostileInputTests`, `ParserTests` | No |
| Confirming a length past the buffer or file, or through endless white space; a `%` of data read as a comment | An `endstream` after the length confirms it past any white space and comments ([#174]), a comment only after white space. Past the bytes held: a first read of 64 bytes (`PdfObjectParser.EndObjLookahead`, no bound), clamped to the file, then reads doubling to 64 KB while white space runs, counted with the searches below, past which the first read decides | `WindowEdgeTests`, `StreamLengthTests`, `ParserTests` | Property |
| A search for a lost `endstream` running into other objects, or past its object's end | One search for the first `endstream`, to the nearest of the next object header (`HeaderText`, its number after a byte that ends a token, comments read as a rebuild does, `PendingEndStream`: [#261], [#52], [#171]), the next object the index places, or the data's end; linear, allocating nothing (`StreamEndSearchBenchmarks`). Under a hidden `/Length`, data a stop ends is the guard's unless what follows proves the `endstream` its own (`IPdfStreamDataProvider.IsOwnEndStream`), white space before it then an end the guard hides (`PdfStreamData.EndHidden`, [#224]). With no `endstream`, the object's `endobj`, followed through the search's reads (`EndObjTail`, `EndObjTailCopies`), keeps a length it follows or ends the data ([#174]); else a shorter declared length stands, or the data runs to the stop. Accepted ([#171]): the search reads the data by its digits, the `obj` keyword's line included, so that a header a comment or a string on that line holds stops it, as the rebuild's scan, which reads a stream's unconfirmed data as the bytes give it, takes such a header too; and the offset the index gives the next object stops it, a header there in a comment or another stream's data included, a row that names a header exactly being trusted, since telling would take a scan of the file for each load | `StreamLengthTests`, `HeaderTextTests`, `EndObjTailCopiesTests`, `PropertyTests`, `WindowEdgeTests`, `ParserTests`, `GuardCutValueTests`, `ReaderTimingTests` | Plausibly |
| Many searches and reads over one stretch of the file | Past the bytes held they read four times the file at most (`PdfFileReader.EndStreamSearchPasses`, an internal constant with its reason), a valid file about three; past it only the bytes held are searched, the data reported as taken unsearched, or the guard's under a hidden `/Length` ([#224]) | `StreamLengthTests`, `DocumentReaderTests`, `GuardCutValueTests`, `HostileInputTests` | No |
| One search reading without end | Reads doubling up to 64 KB (`PdfFileReader.EndStreamSearchChunk`), each further on, ending at a stop, an `endstream` or an empty read | `StreamLengthTests` | No |
| The same search made at every new parse | Kept by data start for the document's life; whether `endobj` follows the length is reread only when a rebuild changes it | `StreamLengthTests`, `ReadOrderTests` | — |
| Encoded data of any length, or handed back as it is | `MaxStreamLength`, a guard (`limit.stream-length`): one stream's data is read no further, the rest cut and reported (to `Decode`'s diagnostics too), what was read decoded as cut; it bounds data handed back undecoded, unlike `MaxDecodedStreamLength` ([#170]) | `ReaderLimitsTests` | No |
| Raw data held with its stream, or copied per stream sharing it | Nothing keeps encoded data (`FileStreamData`): it is read each time, lent by a source in memory (a view keeping that memory alive while held, past disposal, and changing with it), copied otherwise ([#170]) | `DocumentReaderTests`, `HostileInputMemoryTests`, `HostileInputTests` | No |
| A stream decoded long after opening escaping the guards | Its data carries its document's guard and diagnostics | `ReaderLimitsTests` | Yes |

Known gaps:

- [#280] Streams sharing a stretch of the file each read it again from a source not in memory: time, not memory,
  bounded per read by `MaxStreamLength` alone.
- [#138] A stream searched for its `endstream` can keep a length that depends on the object read first.
- [#287] Under a hidden `/Length`, an `endstream` an object only a superseded row places follows is not proven the
  stream's own: its data, whole, is the guard's.
- [#53] Encoded data past about 2 GB cannot be represented: such a `/Length` is cut at `MaxStreamLength`, whose ceiling
  is `Array.MaxLength`.
- [#48] Data that decodes past about 2 GB cannot be read whole.
- Accepted: a classic file's last stream that lost its `endstream`, with no usable length, takes all to the
  file's end, table and trailer included ([#174]).
- Accepted, under a hidden `/Length` ([#224]): data spelling `endstream` then `xref` or `startxref`, and going on,
  ends there as the file's; data a proven `endstream` ends past white space decodes up to that run, what the filters
  meet of that end unreported, a checksum reaching into it included, and what a binary filter decodes from last bytes
  like such a run cut as the guard's. Only the hidden length could tell.
- Accepted: where the chain names a section, an object of another type giving a `/W` before `MaxTrailerLength` cuts
  it is no section ([#281]); a stream of that type whose data the cut hides would be read as the section ([#263]).

### Decoding

Flate is inflated by the runtime's native `System.IO.Compression.Native` (zlib-ng in Microsoft's builds, the system's
zlib in distributions'): a host patching both patches part of the reader.

| Threat | Defense | Held by | Fuzzed |
|---|---|---|---|
| A decompression bomb | Every decoder writes through `PdfBoundedOutput`, or one capped alike, at `MaxDecodedStreamLength` (a guard, `limit.decoded-stream`) | `FilterTests`, `HostileInputTests` | Not at the bound |
| A `/Filter` array of any length, each decoding to the bound | The filters before the last share one bound, the last has its own: two at most. `/Crypt` is neither charged nor the last; a filter that decodes nothing ends the chain ([#166]) | `FilterTests`, `HostileInputTests`, `FilterDamageTests` | Property |
| Millions of filters, each costly whatever its data | `MaxFilterChainLength` (64), a guard (`limit.filter-chain`): decoding, `HasImageFilter` and the cross-reference stream's check read nothing past it; a chain keeps nothing for a name its array holds, and only the name an element written as a reference leads to and the parameters that differ from none (an array of forty million names once ran a decode out of memory) | `HostileInputTests`, `FilterDamageTests`, `FilterTests`, `CrossReferenceRuleTests`, `ReaderLimitsTests` | No |
| A guard's cut reported as damage by later filters | A filter after one the bound stopped, or after `MaxStreamLength` cut, gets data the reader cut: no lost tail, checksum or end marker; a unit the cut parted is dropped. A fault in what was read stays the file's ([#199], [#141]) | `FilterDamageTests`, `FilterTests` | Property |
| A filter value a guard cut or left out of the index | The chain ends there, as past `MaxFilterChainLength` (`FilterChainEnd.Guard`): earlier steps run, the data is left encoded (`leftEncoded`), nothing more reported; an object stream so left is the guard's. It hides none of the stream's own faults ([#263]); `HasImageFilter` says no where it ends the chain first | `GuardCutValueTests`, `FilterTests` | No |
| An output buffer sized from the file | The first buffer is an estimate from the input, capped at the bound, that doubles, never past it | `FilterTests` | Yes |
| Predictor parameters that overflow or size a row unchecked | The row length is computed in 64 bits and checked against the data before any allocation | `FilterTests` | Seeds only |
| An LZW code past the table | A fixed table of 4,096 codes; an undefined code stops decoding, keeping what preceded, reported | `FilterDamageTests`, `FilterTests` | Two seeds |
| Data that breaks its filter's format, read in silence | ASCII85, ASCIIHex and RunLength decode it as other readers do and report each kind once a step; data without its end marker, LZW's included, decodes to its end, reported unless a guard cut it ([#141]); counting allocates nothing | `FilterDamageTests`, `TextFilterRefereeTests` (qpdf) | No ([#176]) |
| Damaged Flate data read again and again | Read again only after a fault, into the same output, byte by byte only over the 8 KB piece of the fault (`FlateInput.ChunkLength`): at most eight readings, three byte by byte, in the faulting filter alone. Accepted: about 40 times slower, still linear (#135) | `FilterDamageTests` | Yes |
| The inflater's exceptions escaping | Only its complaints about the data (`InvalidDataException`, `IOException`) are faults; a preset dictionary is refused | `FilterDamageTests` | Yes |
| Image codecs | DCT, JPX, JBIG2 and CCITT data is never decoded: the chain stops, the data returned within `MaxStreamLength`. An unknown filter, or an element naming none, stops it too, reported ([#159], [#209]); no cross-reference rows are read from data still to decode ([#221]) | Structural; `FilterTests` | — |

Known gaps:

- [#162] Predictors and filter parameters the reader does not apply decode to wrong bytes with no report.
- [#146] A Flate reading allocates its output before the zlib header says it can go on.
- [#142], [#143], [#145] Damaged Flate data is repaired and checked less than it could be.
- [#192] LZW decoding allocates an array for nearly every code, several times its output, within the output's guard.

## The validator

`PdfValidator.Validate` reads a document the caller opened, under its `PdfReaderOptions`, through the reader, its guards
and its cache. Its own reads: the headers and the rebuild settling needs before the first rule
(`PdfFileReader.SettleIndex`); the last 1,024 bytes (`EndOfFileMarkerRule`); every entry's header where it places its
object (`CrossReferenceProbe`, `PdfFileReader.PlaceHeader`), and the scan of the whole file a search near an entry's
offset asks for, once a document (`PdfFileReader.GetScanMemo`); each object stream's header, decoded again outside the
reader's budget (`PdfFileReader.ReadObjectStreamHeader`); and each object stream's dictionary, with the objects its
decoding keys lead through, parsed where the chain's index places them, outside the reader's cache
(`PdfFileReader.ReadWhereChainPlaces`). It decodes no content or image stream.

The rules share five analyses, each built once per validation (`ValidationContext`): the cross-reference probe, the page
tree walk, the object graph, the Arlington walk and the dependencies between object streams. Rule by rule: validation
rules (`docs/website/docs/reference/validation-rules.md`).

| Threat | Defense | Held by | Fuzzed |
|---|---|---|---|
| A report, and the analyses that build it, that grow with the file's faults | The report keeps `FindingCapacity` findings (1,000), counts the rest by severity and rule, and refuses a negative capacity. Each analysis gets the room left when a rule first asks for it, writes of each kind of fault at most that many and counts the rest, so the report is the one every finding would have made, cut to its first ([#168]). A rule writing a finding per item asks for room before formatting it, and a path or a description is built for a written finding only. Four analyses keep more than the room: the probe, the object graph and the dependency walk keep an entry per fault, bounded by what the index names, and the Arlington walk a tally per rule and row of the model | `ValidatorTests`, `FileRuleTests`, `CorpusValidationTests`, `HostileInputTests`, `ObjectRuleTests`, `PageTreeRuleTests`, `ArlingtonRuleTests` | Yes |
| Cycles and depth in the object graph: `/Parent`, `/P`, destinations; a value a caller nested deeper than the stack | `ObjectGraph` walks with an explicit stack and resolves each number once; the trailer's `/Prev` and `/XRefStm` are not followed, and the two searches that place a fault loop over its array of containers ([#168]). A value a caller built that holds itself is walked without end, until memory runs out ([#310]); no file builds one | `PageTreeRuleTests`, `HostileInputMemoryTests`, `ArlingtonRuleTests` | Yes |
| Memory held along a long chain of references | `ObjectGraph` reads each object whole, in written order, and keeps of an object still to read 16 bytes — its number, the object that named it first and that reference's role —, never a value ([#168]), and counts by object, not objects: what stays live is the reader's cache and windows. The Arlington walk is the exception ([#309], below) | `HostileInputMemoryTests`, `ObjectRuleTests` | No |
| A page tree deeper than the stack, with cycles, nodes listed many times, or large values along a deep path | `PageTreeWalk` keeps one heap frame per node on the path, holding the `/Count` it gives and its array of kids, never the node, so a deep path holds what the reader's cache does ([#201]); those arrays are held ([#311], below). A kid on the path is a loop and counts nothing; a node already walked is counted, not walked again; an array of kids that is an object of its own is walked once, in a set of its own. The pages counted stop at the greatest `long`, past which no `/Count` is judged ([#167]). A kid written in its node's array is placed by its node's frame and index, its path written for a finding only, in a loop ([#129], [#168]) | `PageTreeRuleTests`, `HostileInputMemoryTests`, `ReaderTimingTests` | Yes |
| A report that depends on what was read before, or on a rebuild halfway through | The index is settled before the first rule ([#237]), and the rules judge the structure the document opened with. A rebuild set off while loads are in flight is finished once the outermost returns or throws, nothing read through its unfinished index cached, what was reported meanwhile withdrawn ([#273]); a guard its scan reaches throws at once, and a rebuild the source fails runs again at the next read or validation ([#167]). Reading an object stream where the chain places it notes nothing of a definition the reader no longer serves ([#255]), and the object graph reads again the object it walked. The dependency walk follows another object stream's object only where that stream decodes alike ([#277], [#312]). Three exceptions remain, in crafted files: where a damaged stream's data ends ([#138]); what the reader's deepest load gives, cut or served from its cache, which a validation's own reads can decide ([#163]); what the probe reads of an object stream on a loop ([#313], below). The crafted files of `ReadOrderConvergenceTests` (from #128, #237 and #312) and the corpus give one report whatever was read before, the crafted files whatever the cache | `ReadOrderTests`, `ReadOrderConvergenceTests`, `ObjectRuleTests`, `HostileInputTests`, `ReaderLimitsTests`, `CorpusValidationTests`; `ReadOrderSweepTests` (integration) | No |
| A section's `/Size` judged without the rows it counts; a table whose `/XRefStm` names no stream judged as if its stream held nothing | `SizeWrongRule` counts a hybrid file's stream for each table that names it, and judges the stream's own `/Size` once, where the oldest names it. No `/Size` is judged where a table's `/XRefStm` names no stream, nor where the chain ends in a loop. `HybridStreamWrongRule` reports each table whose `/XRefStm` names no stream, an Error (ADR 45) ([#156]) | `FileRuleTests`, `PageTreeRuleTests`, `CrossReferenceRuleTests` | No |
| The orphaned-page sweep rebuilding the index | `PageTreePageOrphanedRule` runs only on a chain read whole, with no loop, no cut, no repair and no broken entry, and on a page tree no limit cut any part of ([#175]) | `PageTreeRuleTests` | Yes |
| Re-checking in the Arlington walk: shared trees, ties, `/Parent` chains | Breadth-first over a queue, each indirect object typed once (its queue: [#309], below). Tree nodes and arrays are expanded once, ties weighed once per candidate set, inherited keys memoized per ancestor and key, and a `/Parent` cycle ends the search. Back-links are checked, never followed | `ArlingtonRuleTests` | Yes |
| Judging what a guard cut | The Arlington walk and the object stream probe skip an object the reader cut at a limit, while the diagnostics have room to say so ([#169]). The page tree rules judge nothing a node, page, array of kids or catalog so cut lacks or holds, nor the Arlington walk a trailer so cut, nor `file.root-invalid` such a catalog, and a value naming an object so cut is unknown, neither absent nor judged ([#175]). An object a guard left out of the index, itself or with its object stream, is unknown, not missing (`ObjectPresence.Unindexed`); `xref.checked-in-part` says where the index filled or a header ran past `MaxObjectLength`. Under `ThrowOnLimit`, a guard throws out of `Validate` | `ArlingtonRuleTests`, `CrossReferenceRuleTests`, `ReaderLimitsTests`, `PageTreeRuleTests`, `FileRuleTests`, `ValidatorTests` | Defaults only |
| Circular object streams: a loop missed or wrongly named, a verdict that depends on read order or the cache, work that grows with what streams share, recursion past the stack | `ObjectStreamDependencies` walks each object stream as the reader decodes it and records, in the reader's order, what that read needs: its `/Length`, `/Type`, `/N` and `/First`, then, only when they let it decode, what `FilterChain` reads of its filters, within `MaxFilterChainLength`, through a recording `IFilterValueReader` ([#277], [#263]). A stream not of `/Type /ObjStm` is judged by its `/Type` alone ([#274]). *Iteration, visited sets, and the stack* bounds its recursion ([#312]). An object the stream holds is a need; one another object stream holds is followed through the reader only where that stream decodes alike in every read order, as `ObjectStreamDependencies`' remarks define it (a stream met again in the reader's loads reads as null, [#51]), but at the reader's deepest load ([#163]), and is otherwise a need read as null ([#312]). A stream keeps at most one need per value its decoding reads. Standings are worked out once a validation, one walk at a time: a walk that met a stream not known yet is made again once it is known, within about three walks per object stream ([#315], below), the waiting streams' dictionaries kept for as many as `ObjectCacheCapacity`. What objects, and nests of streams by `/Length`, give the walk is kept for as many as the reader caches ([#201]), but for nests whose parse depends on the walk, followed again ([#181], below). A loop that closes only through an object whose stream does not decode alike is left to the reader ([#314], below). The walk decodes nothing. The circular streams are the strongly connected components of the needs, by Tarjan's algorithm on a stack of its own, linear in streams and needs ([#168]) | `ObjectRuleTests`, `HostileInputTests`, `HostileInputMemoryTests`, `ReaderTimingTests`, `ReadOrderConvergenceTests` | Yes |
| Reading the file's bytes | The tail read is at most 1,024 bytes. The probe reads each entry's header as settling does, 64 bytes in a sound file, never outside the file, past the source's end or for no byte ([#167]), and searches 512 bytes either side as a load does ([#197]). Past each header's first 64 bytes it consumes at most twice the file's length a validation, remembering up to sixteen runs of white space it crossed; the entries undecided by then are said not checked, once ([#175], [#256]). The header version echoed is quoted as a keyword is | `EndOfFileMarkerRuleTests`, `CrossReferenceRuleTests`, `HostileInputTests`, `PropertyTests` | Yes |
| An offset outside the file handed to a location, which throws on a negative one | The probe and the object stream walk test a row as the reader does, before the header's offset is added: a row negative, past the end or past what a `long` holds locates its finding at the object alone ([#125], [#167]) | `CrossReferenceRuleTests`, `HeaderOffsetTests` | No |
| Numbers chosen to share a bucket of the walks' sets | The walks' sets and maps hash through the process's seed, so numbers chosen to share a bucket validate in about the time of numbers in order ([#155], *Tables keyed by numbers*) | `ReaderTimingTests`, `SeededComparerTests` | No |
| Shared state between validations | `PdfValidator` holds only its immutable options, rules hold no fields, and the Arlington tables are read-only spans | — | — |

The validator has no guard of its own: `FindingCapacity` is an option on the report, visible through `SuppressedCount`,
not a `limit.*`. Its cost follows the index, which `MaxIndexEntries` bounds, hostile or not ([#175], [#168]): about 3 µs
and 350 bytes an entry on sound files, some 24 s and 3 GB near the default bound. An 845-byte cross-reference stream of
one-byte rows, deflated twice, fills the bound: settling holds 640 MiB (*Indexing*) and validation allocates 34 MB
([#181]). An application validating uploads lowers the guard to what it accepts.

Known gaps:

- [#309] The Arlington walk's queue holds the values written inside each object still to check, past the reader's cache:
  354 MiB for 2,000 pages each writing 5,000 integers of resources, under a cache of 64 objects.
- [#311] The page tree walk holds the `/Kids` array of every node on its path, some 40 bytes a kid, and a kid written in
  an array keeps its array until its node is left: 89 MiB for a path of 500 such nodes. In a valid tree it is about what
  the walk keeps for pages.
- [#314] A loop between object streams that closes only through the value of an object whose stream does not decode
  alike where it is met goes unseen, as does what decoding reads after that value; the reader reports it as
  `stream.self-reference`. The walk, parsing each stream once a walk, can name a loop the reader meets in no read order,
  or miss one.
- [#313] The probe reads what an object stream's decoding reads as the reader serves it: where a value is held by a
  stream on a loop, or the stream is on one, that depends on which stream of the loop the reader decoded first, and so
  do `xref.object-stream-broken`, `xref.entry-broken`, `xref.entry-shifted` and `xref.entry-unconfirmed`, for the
  stream or its members' entries.
- [#181] The dependency walk follows again, on each walk, a nest of streams by `/Length` that ends at an object of an
  object stream, loops back on itself or meets a stream another nest parsed: once per object stream, once more per
  holder a waiting stream's walk is made again for ([#315]), and for every nest when more nest heads than
  `ObjectCacheCapacity` are met in turn. Under the default limits it measured up to about 67 MB and 0.5 s a stream for
  Flate, 84 MB and 0.65 s for LZW. A test in `HostileInputTests` pins it; closing #181 flips the pin.
- [#315] A waiting object stream taken up again makes its whole walk again, once for each holder that hid from it. The
  walks stay within about three per object stream, so the total stays linear, but one stream can carry what many paid:
  one hiding 90 holders, its nests not kept, in 1.07 MB, costs 1.36 GB and 4 to 7 s. Keeping each value's outcome across
  one stream's walks closes it, as `ObjectStreamDependencyBenchmarks` is to show.
- [#169] Once the diagnostics are full, a guard reached is neither reported nor seen, and the validator blames the file
  for the reader's cut.
- [#173] The Arlington walk takes a reference to a reference for a missing key.
- [#218] A chain read whole that gives no row is rebuilt as the document opens, and no rule reports it.
- [#132] A document with no catalog at all earns no finding when its index was rebuilt at opening.
- [#229] A catalog whose own load rebuilds the index is tested by neither search for it: the trailer keeps a `/Root`
  that is no catalog, and `file.root-invalid` says no object is one.
- [#260] `file.root-invalid` says no object of the file is a catalog when one may lie past a guard, or when a rebuild
  made `/Root` one.
- [#325] `xref.section-malformed` judges a section only where its trailer has no fault: a section the reader could not
  read for a fault of its own, whose trailer is at fault too, earns the trailer's finding alone.
- [#321] A hybrid table that names through `/XRefStm` an older revision's cross-reference stream two or more revisions
  late has that stream read before the revisions between, its rows standing over theirs, and no rule yet judges where
  the recorded namings lie ([#156]).

## What holds the defenses

- **Hand-written hostile tests**, on every commit: `HostileInputTests`, `HostileInputMemoryTests` (memory left live,
  sampled as the file is read), `ReaderLimitsTests`, `ReaderTimingTests` (a ratio, not a wall-clock budget),
  `WindowEdgeTests`, `StreamLengthTests`, `GuardCutValueTests`, `FilterTests`, `FilterDamageTests`, `DiagnosticsTests`
  and the validation rule tests. They hold the bounds a mutation of a small file cannot reach.
- **A reading of the code** (`SeededComparerTests`), compiled and as written, so that no table a later change adds
  hashes numbers a file chose as themselves (*Tables keyed by numbers*).
- **Budgets on real documents and on a generated index.** `CorpusReadingTests` holds what indexing a 1,000-page document
  and walking its page tree allocate, and each operation on every corpus document to 20 s; `CorpusValidationTests` holds
  validating the largest document to 6 MB; `DocumentReaderTests` holds opening a valid index of 300,000 objects to 5 %
  over its figure ([#193]), and a chain of a thousand sections to resizing the index no more often than doubling it
  ([#164]). Every job that runs the tests on a change, and the documentation's deployment, stops after fifteen minutes.
  Throughput is not held in CI yet ([#38]).
- **The mutation campaign** (`FuzzingTests`, `FuzzingSeeds`, `.github/workflows/fuzz.yml`) has three targets: open a
  mutated document, resolve every indexed object and decode every stream without an image filter; validate every mutant
  the reader opens, no exception allowed; and parse the mutated bytes as one object, in most seeds only the first
  object's number ([#158]). Mutations flip bits, rewrite digits, truncate, overwrite 1 to 64 random bytes, or break an
  `endstream` or `obj`. Each input must finish within 5 seconds, checked once it returns, and allocate under 64 MB.
  Seeds are unencrypted corpus documents of 120 KB at most, read under the default limits. Every commit runs 104 seeds
  60 times each; each night runs the smallest seed of each of the 32 reader structures plus 16 in rotation, 20,000 times
  each, every seed reached within five runs.
- **Properties** (`PropertyTests`, FsCheck): the lexer, the parser, the number parsers, text strings, window edges, the
  limits' validation and the end-of-file rule; 200 cases per commit with a fixed seed, 50,000 a night seeded from the
  run number.
- **Referees**: qpdf, in a container, judges the corpus as we do (`QpdfRefereeTests`, `FlateRefereeTests`,
  `StreamLengthRefereeTests`, `ValidationRefereeTests`).

What none of them reaches is the subject of the gaps below. A mutation never makes a file longer, so nothing
size-dependent is fuzzed: the guards, the budgets, the rebuild's pieces. FsCheck's default sizes keep generated buffers
near 100 bytes and integers within ±100. No coverage-guided fuzzer exists yet ([M23] slice 11).

Known gaps:

- [#158] The parser target, three properties and three hostile-input tests hold less than their names claim, and several
  defenses have no test.
- [#176] The campaign never reaches RunLength, ASCIIHex, most predictor rows, any guard, encrypted files or inputs past
  120 KB, and cannot see a read past the end.
- [#38] Throughput is not held in CI.
- [#194] The nightly campaign starts its seeds at 0 every night, so it runs the same mutants of a document each time.

## The package and its supply chain

- **The core has no dependency** (invariant 1): it references no package and no other project. `ci.yml` fails a pull
  request whose packed core declares a dependency in its nuspec; a native one, through `[DllImport]`, is kept out by
  review alone ([#179]). The AOT and trimming analyzers run, warnings fail the build, and unsafe code is off.
- **Workflows** declare read-only permissions at their top and raise one per job where needed. Every action is pinned to
  a commit SHA. CI and the commit checks run on `pull_request`, so a fork's pull request gets no secret and no write
  token. The one `pull_request_target` workflow, Dependabot's auto-merge, checks out no code and queues only what
  `fetch-metadata` classifies as a patch or a minor. Every Dependabot entry waits a week before proposing a version.
- **Publishing** uses trusted publishing ([ADR 25](adr/0025-trusted-publishing-rather-than-an-api-key.md)): a key valid
  one hour, exchanged for the job's OIDC token just before the push, and no NuGet secret stored. The `AdCodicem.*`
  prefix is reserved on nuget.org. Builds are deterministic and carry their sources' location and symbols.
- **A preview is built where nothing can publish** ([ADR 49](adr/0049-previews-weekly-when-a-package-input-changed.md)):
  `preview.yml` tests and packs in jobs that hold no credential, the pack restoring only the packable projects under
  `src/`. The publish job, alone in the `nuget` environment, runs no project code: it checks the artifact against the
  pack's digests, the set and that the plan is still current, attests every package and assembly, and pushes. The
  release tooling's npm packages run in a job of their own, whose only outputs the plan computes again. An assembly
  restored from nuget.org verifies with `gh attestation verify`.
- **A stable release is built where nothing can publish, and approved before anything does.** `release.yml` tests and
  packs in jobs with no credential. Its release job, in `nuget-stable`, waits for the reviewer, builds nothing, checks
  the pack's files against their digests, and runs semantic-release from the committed lock without install scripts,
  beside the NuGet key and the release App's token; its own `GITHUB_TOKEN` only reads. Packages and assemblies are
  attested, the bundle attached to the GitHub Release before it is published. The pipelines' detail is in
  `releasing.md`.
- **Test data is pinned**: remote corpus documents by size and SHA-256, the Arlington model by a lock file a test
  checks.
- **Measured, not asserted**: OpenSSF Scorecard runs weekly and on every push to `main`; CodeQL runs as GitHub's default
  setup, which the ruleset on `main` requires.

Known gaps:

- [#177] The benchmarks' `filter` input still reaches `run:` by template expansion, and the qpdf referee's and the
  commitlint action's images are pinned by tag, not by digest.
- [#178] A branch can publish a package without review: nothing ties the `nuget` environment to `main`.
- [#179] Only the packed nuspec is checked for a dependency of the core: not the assembly's references, a
  `FrameworkReference` or a `[DllImport]`.
- [#43] The NuGet restore is not locked.
- [#41] The release App on the ruleset's bypass list, and the CI checks required by name, are settings the maintainer
  has yet to make; until then the stable release stops at its first step.
- [#44] The project has no OpenSSF Best Practices badge.
- [#196] Dependabot's auto-merge queues only patches and minors, but nothing requires CI before the merge it queues
  until the checks are required by name (#41).
- [#195] The corpus build's Python producers are pinned by version, without hashes, and the packages they pull in are
  not pinned.

## Surfaces still to come

Each surface below is written by the milestone that opens it, in the form of the sections above, in the same change. The
last column names what the specifications decide today and the questions they leave open, which that milestone settles
before it writes code.

| Surface | Opens in | Decided by | What the specifications decide, and what they leave open |
|---|---|---|---|
| XMP packets: the first XML the library parses | [M02] slice 5 ([#61]) | ADR 34 | [M03] and [M14] give the parser's settings: DTDs prohibited, no resolver, iterative, size under the stream's guard, billion-laughs and external-entity packets read as malformed. Slice 5 comes first and must adopt them. |
| The writer: recovered values, the incremental copy, the files a save writes | [M03] | ADR 6, 13, 18 | Nesting written without recursion past the parser's depth; a lying `/Length` not propagated; a save writes a temporary file beside its target, and saving over the source is refused. `OpenAsync(Stream)` buffers a non-seekable stream whole, with no bound stated. |
| Revisions and signature coverage: `/ByteRange`, `/Contents` | [M04] | ADR 18 | Byte ranges checked against the file before use, none sizing an allocation; a revision opened on a slice that ends at its end; iterative walks. |
| Repair | [M05] | ADR 22 | Hostile documents end in a report or a typed exception within time and allocation budgets. No bounds table yet. |
| Copying objects between documents, catalog structures, embedded files, portfolios | [M06], [M07] | ADR 17, 37 | Copies never follow `/Parent`, `/P` or outline links, with an explicit stack; attachments streamed under the source's guards. Open: how a file name from the PDF becomes an output path in the tool's `attachments … extract`, and the depth bound on nested portfolios, whose value is not given. |
| Image files as pages: JPEG, JPEG 2000, TIFF, PNG | [M07] | ADR 42 | Container parsers in the core, bounded and fuzzed. Open: `PdfImagePageOptions` carries no limits record for a file read outside any document. |
| Content streams: tokenizing, rewriting, interpreting | [M07], [M08], [M15] | ADR 15, 34 | Guards on operations, glyphs and graphics state depth arrive with the interpreter ([M15]). Open: received content rewritten through `PdfContentBuilder`, whose state machine throws `InvalidOperationException` on sequences damaged pages hold. |
| Font programs: sfnt, CFF charstrings, WOFF, WOFF2, Type 1, CMaps | [M08] | ADR 11, 34, 43 | `MaxFontLength` (64 MB) and `MaxCharstringOperations` (65,536) join `PdfReaderLimits` (ADR 34, amended); composite glyphs walked iteratively; WOFF2 sizes checked before allocation; fonts fuzzed nightly. |
| Barcode payloads, and `barcode:` URLs from template data | [M10], [M12] | ADR 38 | Payloads end in the typed exception or a parse error within a time and allocation budget. |
| Annotations, rich text (`/RC`), optional content | [M11] | ADR 34, 37 | `/RC` parsed with `XmlReader`, DTDs prohibited; iterative walks with visited sets. |
| HTML and CSS | [M12] | ADR 2, 4, 37, 38 | Scripting disabled. Guards on box depth, pages, imports, custom property expansion and selector cost. Open: the length of a `@counter-style` representation, and of a source given as one string. |
| The resource loader and web fonts | [M12] | ADR 11, 38 | Deny by default; addresses checked after name resolution, redirects kept within an allowed origin, `file:` confined to a root; resource guards apply. Open: how `CachingResourceResolver`, keyed by URL, composes with each render's policy, and which reader options a PDF fetched as a page image opens under. |
| Native code in `AdCodicem.Pdf.Html`: HarfBuzz reading fonts, Skia decoding images | [M12] | ADR 5, 43 | Images checked against `MaxImagePixels` by our own header parser before Skia sees them. Open: the tables HarfBuzz reads and M08's parser does not (GSUB, GPOS, morx) are neither validated nor bounded. [M25] keeps every font program away from native parsers. |
| SVG | [M12] | ADR 37, 38 | `MaxSvgElements`; DTDs prohibited in referenced files. Open: group nesting depth, and cycles among `clipPath`, `mask`, `pattern` and `marker`. |
| Factur-X, CII, UBL and XRechnung XML | [M14] | ADR 9, 37 | XMP's reader settings; schemas from embedded copies only. Open: the third-party model's own XML settings, and escaping in the HTML rendition. |
| Markup generated from file text (Markdown, JSON, HTML renditions); regular-expression search | [M15] | ADR 15, 38 | Text escaped so it cannot make structure; `NonBacktracking` by default. Open: [M31] does not yet state escaping for the values it places into CSS. |
| ICC profiles | [M15], [M29] | ADR 34, 42 | Bounded header and tag-table readers; CLUT sizes computed in 64 bits before allocation; fuzzed over fourteen nights. |
| Encryption and passwords | [M16] | ADR 41 | `/O`, `/U`, `/OE`, `/UE`, `/Perms` and `/Length` checked before use; decryption block by block through pooled buffers. Open: whether passwords and file keys are cleared from memory and kept out of exceptions and diagnostics. The managed MD5, RC4 and AES are not constant-time. |
| Forms, scripts and XFA; FDF and XFDF | [M16] | ADR 34, 37 | Scripts recognized by pattern, never run; XFA and XFDF parsed with `XmlReader`, DTDs prohibited; field trees walked iteratively. |
| E-mail: EML and MSG | [M18] | ADR 34, 38 | `EmailLimits` guards; CFB chains cut at the sector count; LZFu sizes checked before allocation; every remote reference refused. |
| Redaction and sanitization | [M19] | ADR 22, 37 | Always a full rewrite; a stream cut at a guard fails verification rather than passing it. |
| Conformance profiles over hostile documents | [M20], [M28] | ADR 36, 45 | Bounded header readers; the engine proved total by FsCheck. |
| Received documents converted to PDF/A | [M21] | ADR 22, 41 | `MaxAttachmentDepth` (3), with the attached streams' hashes. |
| Image codecs: CCITT, JBIG2, JPEG, JPEG 2000; color spaces and functions | [M22] | ADR 34, 35, 42 | `MaxImageWorkingSet`, `MaxJpegScans`, JBIG2 counts in 64 bits, a bounded PostScript stack, every decoder fuzzed. Open: the cost of a type 4 function evaluated per sample, and the lookup table's size for an n-input `DeviceN`. |
| OCR text layers: hOCR, ALTO, TSV | [M22] | ADR 42 | `XmlReader` with DTDs prohibited; `MaxInputLength` 64 MB, typed, since it is the caller's input. |
| Optimization, the piecewise decode, hardening | [M23] | ADR 34, 35 | Cancellation within 100 ms on hostile input, the cache weighed in bytes, coverage-guided fuzzing, the container profile. |
| Comparison and templates | [M24] | ADR 15 | Bounded edits per window, `NonBacktracking`. |
| Rasterization | [M25] | ADR 16, 42, 43 | `MaxRasterPixels`, band and layer bytes, bounded mesh subdivision; no font program or codec reaches Skia. |
| Signing: TSA and remote-signing responses, deferred requests | [M26] | ADR 18, 41 | Network off by default, responses read up to a bound, the prepared file checked again before the second step. |
| Signature validation: CMS, X.509, OCSP, CRL, trusted lists, and the URLs they name | [M27] | ADR 41 | OCSP and CRL parsed by our own `AsnReader` code under explicit bounds, fuzzed from the day they are written; path building uses a visited set. Open: no address, scheme or redirect policy is stated for the URLs a signed file names (caIssuers, OCSP, CRL); ADR 38's rules cover only the HTML engine. |
| DOCX packages | [M31] | ADR 37, 38 | ZIP limits checked before reading, part names validated, DTDs refused in every part, external relationships refused by default. |
| JSON inputs: plans, models, policies, templates, requests | [M06] onward | ADR 8 | `Utf8JsonReader` and source generation, no reflection. Open: several specifications state no depth or size bound. |

## Out of scope

- **Active content** ([ADR 37](adr/0037-out-of-scope-active-content-and-pdf-to-office.md)). JavaScript is never
  executed, dynamic XFA never rendered, 3D and rich media never authored. A file that carries them is read, validated
  and manipulated like any other, and what cannot be kept is reported.
- **The caller's own code and choices.** A `PdfFileSource`, a resolver, a codec, an OCR engine, a key source or an
  `HttpClient` the caller supplies is trusted. A caller that raises a guard, or chooses `Unbounded`, lets a file use
  what those bounds allow: by design, not a vulnerability.
- **A file changed under an open document.** `Open(string)` shares the file for reading, which Unix treats as advisory,
  and `Open(ReadOnlyMemory<byte>)` reads the caller's memory in place. Bytes that change after opening are read as they
  are then; a caller who cannot keep them copies them first.
- **Concurrent use of one `PdfDocument`.** It is not thread-safe, as its documentation says, and nothing detects two
  threads using it.
- **Hard limits on a process.** The library does not sandbox itself. Until [M23]'s container profile, a host that must
  bound time or memory absolutely runs the library in a process or container it can stop.
- **What a page shows as opposed to what the file says.** The reader returns what the file holds. Whether a page's
  appearance misleads (an overlay, a field over signed text, white text) is for the milestones that render, compare or
  judge signatures to report.
- **Timing side channels.** None exists today. [M16] records that its managed MD5, RC4 and AES are not constant-time.

## Keeping this document

- A change that opens or widens a surface completes its section in the same pull request (`CLAUDE.md`). A new
  bound is classified when it is added (ADR 34). A defense that is missing is filed as a `debt` issue and named
  under *Known gaps*, never written as if it held.
- A defense named here is one that was read in the code. A test class named here is one whose tests were read.
- An entry says what a defense stops, where it lives (type and member, never a line), how its bounds are classified,
  which test class holds it and whether it is fuzzed, in a few lines. It names no test method and enumerates no
  hand-made shape; how each shape is answered is written once, in the XML documentation of the code that answers it
  ([ADR 54](adr/0054-review-and-documentation-effort-follow-what-protects-the-library.md)).
- A milestone's review checks the code against this document (`docs/milestone-review.md`, axis 5). Any
  difference it finds is a finding.

[M02]: milestones/M02.md
[M05]: milestones/M05.md
[M06]: milestones/M06.md
[M07]: milestones/M07.md
[M10]: milestones/M10.md
[M11]: milestones/M11.md
[M14]: milestones/M14.md
[M15]: milestones/M15.md
[M18]: milestones/M18.md
[M19]: milestones/M19.md
[M20]: milestones/M20.md
[M21]: milestones/M21.md
[M22]: milestones/M22.md
[M24]: milestones/M24.md
[M28]: milestones/M28.md
[M29]: milestones/M29.md
[M31]: milestones/M31.md

[M03]: milestones/M03.md
[M04]: milestones/M04.md
[M08]: milestones/M08.md
[M12]: milestones/M12.md
[M16]: milestones/M16.md
[M23]: milestones/M23.md
[M25]: milestones/M25.md
[M26]: milestones/M26.md
[M27]: milestones/M27.md

[#36]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/36
[#37]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/37
[#38]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/38
[#41]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/41
[#43]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/43
[#44]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/44
[#47]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/47
[#48]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/48
[#49]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/49
[#52]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/52
[#51]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/51
[#53]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/53
[#61]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/61
[#121]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/121
[#125]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/125
[#128]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/128
[#129]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/129
[#132]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/132
[#135]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/135
[#138]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/138
[#141]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/141
[#142]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/142
[#143]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/143
[#144]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/144
[#145]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/145
[#146]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/146
[#149]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/149
[#154]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/154
[#155]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/155
[#156]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/156
[#158]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/158
[#159]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/159
[#160]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/160
[#161]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/161
[#162]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/162
[#163]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/163
[#164]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/164
[#165]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/165
[#166]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/166
[#167]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/167
[#168]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/168
[#169]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/169
[#170]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/170
[#171]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/171
[#173]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/173
[#174]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/174
[#175]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/175
[#176]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/176
[#177]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/177
[#178]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/178
[#179]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/179
[#180]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/180
[#181]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/181
[#183]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/183
[#185]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/185
[#188]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/188
[#189]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/189
[#191]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/191
[#192]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/192
[#193]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/193
[#194]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/194
[#195]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/195
[#196]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/196
[#197]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/197
[#199]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/199
[#200]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/200
[#201]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/201
[#202]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/202
[#207]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/207
[#208]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/208
[#210]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/210
[#209]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/209
[#212]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/212
[#218]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/218
[#220]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/220
[#221]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/221
[#222]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/222
[#224]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/224
[#229]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/229
[#230]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/230
[#237]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/237
[#241]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/241
[#255]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/255
[#256]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/256
[#259]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/259
[#260]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/260
[#261]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/261
[#262]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/262
[#263]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/263
[#264]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/264
[#273]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/273
[#274]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/274
[#275]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/275
[#277]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/277
[#279]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/279
[#280]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/280
[#281]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/281
[#287]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/287
[#289]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/289
[#309]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/309
[#310]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/310
[#311]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/311
[#312]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/312
[#313]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/313
[#314]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/314
[#315]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/315
[#320]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/320
[#321]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/321
[#322]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/322
[#323]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/323
[#324]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/324
[#325]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/325
[#326]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/326
[#327]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/327
[#328]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/328
[#329]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/329
[#330]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/330
[#331]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/331
[#332]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/332
[#333]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/333
[#348]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/348
[#350]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/350
