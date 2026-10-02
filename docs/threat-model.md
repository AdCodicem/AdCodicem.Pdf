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

## How to read it

- A **threat** is what a file can make the library do: exhaust memory, the stack or time, read out of bounds,
  throw something untyped, return a wrong answer, or reach the host.
- A **defense** is the code that stops it: a bound, a visited set, a check, an iterative walk.
- **Held by** names the test that fails if the defense goes. **Fuzzed** says whether the mutation campaign
  reaches the code at all (*What holds the defenses*, below).
- A bound is classified as [ADR 34](adr/0034-every-valid-pdf-is-readable-and-the-readers-guards-are.md) requires.
  A **guard** is a `PdfReaderLimits` property, on by default and reported under a `limit.*` code: a valid file
  may reach it. An **internal constant** is a bound that only an invalid file reaches, and the reason is written
  where the constant is declared. A bound on what the library repeats of a value, not on what it reads, is an
  internal constant too, though a valid file may reach it, and its declaration says so: a message quotes 127 bytes
  of a name ([ADR 34](adr/0034-every-valid-pdf-is-readable-and-the-readers-guards-are.md), amended 2026-10-01).

## Assets

What a caller entrusts to the library when it opens a file it did not produce:

- **The process's memory.** Memory follows the heaviest object the caller touches, not the size of the file
  (invariant 2). No allocation is sized by a value from the file without a checked bound (invariant 4). The five
  guards cap what a valid but exceptional file may make the reader hold.
- **The process's time, and the calling thread.** The aim is that every call does work in proportion to the bytes
  it reads, under the guards. Several shapes break it today ([#155], [#166], [#168], [#181]). No call can be
  cancelled yet ([M03] slice 9, [M23] slice 11). A .NET thread cannot be aborted, so a slow call holds its thread
  until it returns.
- **The stack.** A `StackOverflowException` cannot be caught in .NET: it ends the process. Every recursion whose
  depth a file chooses is bounded, and the reader also asks the runtime whether stack is left
  (`RuntimeHelpers.TryEnsureSufficientExecutionStack`) before it goes one level deeper.
- **The caller's resources.**
  - `Open(string)` opens a read-only handle (`FileShare.Read`) and holds it until `Dispose`.
  - `Open(ReadOnlyMemory<byte>)` reads the caller's memory where it lies, without copying it.
  - `Open(Stream)` reads the caller's stream and leaves it open.
  - `Open(PdfFileSource, …)` releases the source when it was given to own it, on failure too.
- **The truth of what the library returns.** A caller decides on the objects, the decoded data, the diagnostics
  and the validation report. Should an upload be accepted? Is the document encrypted? Did the reader cut
  something? A file that makes the report lie defeats the caller as surely as a crash does. That covers hiding its
  encryption, hiding a cut, or getting the reader's own limit blamed on the file.
- **The host's logs and terminals.** Diagnostic and finding messages quote names and keywords from the file, and
  hosts log and print them. A quote is printable ASCII on one line and at most 127 bytes of what the file wrote, so
  what a host prints is the library's text, not the file's (*Reports*, below).

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
| What the reader derives | `GetObject`, `PdfReference.Resolve`, the `PdfObjectExtensions` accessors, `Trailer`, `Catalog`, `ObjectNumbers`, `PdfStream.GetRawBytes`, `Decode` | Values the file chose, handed to the caller's code |
| Messages | `PdfDiagnostic.Message` and `ToString`, `PdfValidationFinding.Message` and `ToString` | Text that quotes names and keywords from the file, escaped and cut |
| The validator | `PdfValidator.Validate` | The same document, plus bytes the validator reads itself: the file's tail, 64 bytes at each entry's offset, object stream headers |

Every surface still to come adds a boundary of its own: HTML, CSS and template data, the resource loader, font
programs, image files, content streams, XML, encryption, signatures and the network, DOCX packages and e-mail.
*Surfaces still to come* lists each one.

## Defenses every surface shares

### The guards

| `PdfReaderLimits` | Default | Code | Where it applies |
|---|---|---|---|
| `MaxDecodedStreamLength` | 256 MB | `limit.decoded-stream` | Every filter's output, each step of a chain separately (`PdfFilterPipeline`) |
| `MaxObjectLength` | 16 MB | `limit.object` | The window a regular object is parsed through, grown ×8 from 8 KB (`PdfFileReader.TryParseNumberedAt`) |
| `MaxXRefSectionLength` | 64 MB | `limit.xref-section-length` | The window a classic cross-reference table is read through |
| `MaxXRefSectionCount` | 1,024 | `limit.xref-section-count` | The `/Prev` links the chain follows |
| `MaxTrailerLength` | 64 KB | `limit.trailer` | A trailer the table's window cut, and a cross-reference stream's dictionary |

Reaching a guard keeps what fits and reports its code (`PdfLimitGuard.Reach`): once per position for an object, a
section or a trailer, and each time a stream is decoded past the bound. When the document was opened with
`ThrowOnLimit`, reaching a guard throws `PdfLimitExceededException` instead. A stream read from a document
decodes under that document's guards, however long after opening. `PdfReaderLimits` refuses a bound of zero or
less and takes a length past `Array.MaxLength` as that length. Held by `ReaderLimitsTests` throughout; the
mutation campaign opens with the defaults and never with `ThrowOnLimit`.

### Iteration, visited sets, and the stack

- **The parser recurses**, one frame per container level. Two things bound it: `PdfObjectParser.MaxDepth` (128)
  and a stack probe at each container. Past either, the container is skipped by counting brackets, with no
  recursion, and reported as `syntax.depth-exceeded`.
- **Loads nest.** An indirect `/Length`, an object stream's dictionary or a filter parameter loads another object
  while one is being parsed. `PdfFileReader.MaxNestedLoads` (64) and a stack probe before each load bound the
  chain; `_loading` turns a load that needs itself into null. While the cross-reference chain is read, nothing
  loads: what the chain refers to is read where a section already read places it, under the same bounds (see
  *Indexing*).
- **Every walk the validator makes across objects is iterative**, with a visited set by object number: the
  object graph, the page tree, the Arlington walk, name and number trees, and the dependencies between object
  streams. The searches inside one object recurse no deeper than the parser nested it.
- **Chains are followed once.** The `/Prev` chain keeps the offsets it has read. `PdfReference.Resolve` fetches
  at most `MaxChainLength` (32) objects, so a chain of 31 references to references already reads as null ([#173]).
  The index is rebuilt at most once per document (`_repaired`).
- **Reports are made once.** A guard is reported once per `(limit, position)`, a stream's length fault once per
  data start, and a search for a lost `endstream` runs once per data start.

### Everything read is bounded by the source

`PdfFileSource.GetWindow` returns an empty window for a range outside the source and clamps the rest.
`FileSource.Read` loops until the buffer is full or the file ends. A pooled window goes back to its pool at the
end of its `using`, and parsed values are copied out of it, so nothing a caller holds points into a rented buffer.
Held by `DocumentReaderTests.A_source_reads_nothing_outside_itself`,
`A_source_gives_an_empty_window_for_a_range_outside_itself_or_of_no_length`, and every test that reads through
`StrictCountingSource`, which throws on a read past its end.

### Reports

`PdfDiagnostics` keeps `PdfReaderOptions.DiagnosticCapacity` entries (1,000) and counts the rest in
`SuppressedCount`. `PdfValidationReport` keeps `PdfValidatorOptions.FindingCapacity` findings (1,000) and counts
the rest by severity and rule. Both bound the **number** of entries; *The validator*'s gaps say what building them
costs.

An entry's **size** is bounded by how it quotes the file ([#159]). Every name and keyword a message quotes goes
through `FileQuote`, which writes it as a PDF writer writes a name: printable ASCII on one line, every other byte, the
number sign and the delimiters as `#xx`. Past `FileQuote.MaxBytes` (127) it is cut, with the whole's length, from a
buffer on the stack, so a name of 64 MB costs its quote and no copy. A path of keys in a finding keeps its first and
last `RuleText.PathEnds` (4) steps. A string from the file is never quoted. Numbers are written in the invariant
culture. What the reader keeps of a stream's length fault for the document's life is the value it found, not words
about it. Held by `FileQuoteTests`; `PropertyTests.A_quote_of_the_file_is_printable_ascii_on_one_line_and_bounded_whatever_the_bytes`;
`HostileInputTests.Reads_streams_whose_length_names_a_name_of_millions_of_characters_within_an_allocation_budget`,
`Reads_fifty_streams_whose_length_names_one_long_name_within_an_allocation_budget`,
`Quotes_a_keyword_of_ten_megabytes_where_a_subsection_should_start_by_its_first_bytes`,
`Validating_an_object_whose_key_path_holds_megabyte_keys_stays_within_an_allocation_budget`; and
`CorpusReadingTests.Every_message_a_document_earns_is_printable_ascii_on_one_line_whatever_the_culture`, under a Persian
culture.

### What may be thrown

From a file, only a `PdfException`: `PdfFormatException` for an input that is empty or holds no object,
`PdfEncryptedException`, and `PdfLimitExceededException` when the caller asked for it. Everything else that
escapes is the caller's: an argument it passed, the I/O of its own file or stream, a document it disposed. Any
other exception a file can cause is a defect, and the ones found are in the gaps below.

### What the library never does with a file's content

It opens no path, URL or process that a file names. It loads no type and writes no log. The only host access in
the core is `File.OpenHandle` on the path the caller gives, read-only. A search of `src/` finds no `HttpClient`,
`Process`, `Assembly.Load`, `Type.GetType`, `Activator`, P/Invoke, `Console`, `Trace` or logger.

The only shared mutable state the library keeps is `PdfName`'s interned table. It is a `ConcurrentDictionary`, so
it is safe to share across threads, but it grows for the life of the process ([#37]). Every other static value is an
immutable record or singleton. Windows and Flate buffers are rented from the process-wide `ArrayPool<byte>.Shared`
and go back to it uncleared ([#180]). A `PdfDocument` is not thread-safe, and nothing detects two threads using one.

## The reader

Everything below reads bytes the file chose. The reader opens a file by indexing it. It reads the header, the tail
and the cross-reference chain, or it rebuilds the index by scanning the file when the chain fails. After that it
parses only the objects a caller asks for, each through a window of the file, and decodes a stream only when the
caller asks for its data.

### Opening and the file source

| Threat | Defense | Held by | Fuzzed |
|---|---|---|---|
| An empty input, or one that holds no object | `PdfDocument.Open` refuses it with `PdfFormatException` and releases a source it owns | `DocumentReaderTests.Refuses_an_empty_input`; `HostileInputTests.Refuses_an_input_that_contains_no_objects`; `ReaderLimitsTests.Opening_an_empty_input_releases_the_source_it_owns` | The second |
| Junk before the header; a header or a `startxref` searched for across the whole file | `%PDF-` is looked for in the first 4 KB, and the bytes before it shift every offset; a file without one is read from its first byte, under `header.missing`. `startxref` is looked for in the last 4 KB. One that is missing, negative or not a number rebuilds the index | `DocumentReaderTests.Reads_a_file_that_starts_with_junk_before_the_header`, `A_file_without_a_header_is_read_through_its_table_and_its_header_reported_as_a_repair`, `Rebuilds_the_index_of_a_file_whose_tail_was_lost` | Yes |
| A failure while indexing that leaks the file handle | A guard thrown while indexing releases a source the document owns. A rebuild that reaches a guard finishes the index before it throws | `ReaderLimitsTests.Opening_releases_a_source_it_owns_when_a_guard_throws`, `A_rebuild_that_reaches_a_guard_finishes_the_index_before_it_throws` | No |
| Ciphertext handed out as content | With `ThrowOnEncrypted`, on by default, `Open` refuses a trailer that holds `/Encrypt` with `PdfEncryptedException`. A rebuilt index can lose the key ([#154]) | `DocumentReaderTests.Refuses_an_encrypted_document_with_a_typed_exception` | No: encrypted seeds are left out |
| Opening that reads the whole file | The index comes from the cross-reference sections, and stream data is read only when asked for | `DocumentReaderTests.Does_not_read_stream_data_until_it_is_asked_for`; `CorpusReadingTests.Opening_does_not_read_the_content_of` | — |
| Use after dispose | `Dispose` is idempotent, `GetObject` throws `ObjectDisposedException` after it, and the validator refuses a disposed document | `DocumentReaderTests.Disposing_a_document_twice_is_harmless`; `ValidatorTests.Validating_a_disposed_document_throws_before_any_rule_runs` | — |
| Limits that allow nothing | Every `PdfReaderLimits` property refuses zero or less, and `PdfReaderOptions.Limits` refuses null | `ReaderLimitsTests.A_limit_of_zero_or_less_is_refused_and_named`, `The_options_refuse_limits_that_are_not_there` | — |

Known gaps:

- [#154] Encryption is decided by the merged trailer's `/Encrypt` key, unresolved. A rebuilt index loses one kept in a
  cross-reference stream and hands out ciphertext; one that resolves to null refuses an unencrypted file.
- [#180] Pooled buffers go back to the shared pool uncleared, and the windows a hostile file grew stay pooled, per
  thread, after the document is disposed.
- [#167] `ObjectNumbers` and `Diagnostics` are live: a read that rebuilds the index while a caller enumerates them
  throws `InvalidOperationException`.
- [#149] `Open(Stream)` copies a seekable stream into memory, where its documentation says only a non-seekable one is.
- [#52] A source whose `Read` returns fewer bytes than asked is taken as the end of the data.
- [#125] A `startxref` near the largest `long` wraps to a negative offset once the header's offset is added.
- [#190] A disposed document keeps answering through every member but `GetObject`, and `Open(Stream)` reads a
  `MemoryStream` from its start or from its position depending on how it was built.
- [#191] A reference taken before `Dispose` resolves through the reader, not the document, and skips its disposed
  check: it keeps resolving for a document opened from memory.

### Object syntax

| Threat | Defense | Held by | Fuzzed |
|---|---|---|---|
| The lexer looping, or reading past its buffer | Every token but the end of input consumes a byte. Look-aheads check the length, and a string that never closes ends at the buffer's end | `PropertyTests.The_lexer_terminates_on_any_bytes_and_stays_inside_the_buffer`; `LexerTests.Never_loops_on_a_stray_delimiter`, `Recovers_from_an_unterminated_literal_string` | Property, over buffers of about 100 bytes |
| Nesting that exhausts the stack | `PdfObjectParser.MaxDepth` (128) and a stack probe at each container. Past either, `SkipContainer` counts brackets without recursion, and `syntax.depth-exceeded` is reported | `ParserTests.Refuses_to_follow_nesting_deeper_than_its_limit`; `HostileInputTests.Survives_deeply_nested_containers_inside_an_object`, `A_file_nesting_objects_deeper_than_the_stack_allows_reads_them_as_null_without_crashing` | No |
| A token or an object of any length, or one that never closes | The window grows ×8 from 8 KB up to `MaxObjectLength`. Past it, the object is kept as far as it was read and `limit.object` is reported. The parses of one object add up to about 1.3 times the last window | `HostileInputTests.Survives_a_string_longer_than_the_largest_window`; `ReaderLimitsTests.An_object_is_read_no_further_than_its_bound`, `An_object_cut_by_its_bound_is_not_reported_as_damage`; `WindowEdgeTests.An_anomaly_inside_an_object_longer_than_the_window_is_reported_once` | Growth yes, the bound no |
| Look-ahead that makes parsing superlinear | Look-ahead is a constant number of tokens, so each token is lexed three times at most | `ParserTests.Distinguishes_a_reference_from_two_integers`, `Backtracks_when_two_integers_are_not_followed_by_R` | Property |
| A number past a `long` or a `double` | The number parser never wraps: an integer past a `long` reads as the real nearest to it, and a number past a double's range as an infinity, which the object parser reads as null under `syntax.number-out-of-range`, quoting the number only in a report the diagnostics keep. A real reads as the double nearest to its decimal. The shape — signs, then digits around at most one period — is checked before the framework's parse, which would read `NaN` and `Infinity`; each digit is read twice at most, and nothing is allocated | `LexerTests.Reads_an_integer_too_large_for_a_long_as_a_real`, `Reads_a_number_past_the_largest_double_as_an_infinity_its_reader_can_tell`, `Parsing_a_number_allocates_nothing_however_long_it_is`; `PropertyTests.The_integer_parser_reads_every_long_and_never_wraps_past_one`, `The_real_parser_reads_the_double_nearest_to_the_decimal`; `ParserTests.Reads_a_number_past_the_largest_real_as_null_and_reports_it`, `Reports_no_number_its_double_holds_however_it_rounds`; `HostileInputTests.Reads_a_number_fifteen_million_digits_long_in_time`, `Quotes_no_number_beyond_what_a_real_can_hold_once_the_diagnostics_are_full` | Property |
| An object number or a generation overflowing its cast | The parser builds a header only from a number in 1..`int.MaxValue`, and a reference only from one in 0..`int.MaxValue` — object 0 heads the free list and reads as null —, each with a generation in 0..65,535 (`PdfObjectId.MaxNumber` and `MaxGeneration`, with their reasons), checked before the cast. The index and object streams check theirs the same way, before narrowing: see *Indexing* and *Resolving objects and object streams* | `ParserTests.Refuses_an_object_header_whose_number_or_generation_is_out_of_range`, `Makes_no_reference_of_numbers_an_object_identifier_cannot_hold`; `HostileInputTests.Serves_no_object_under_a_number_past_a_long_that_once_wrapped_to_its_number` | No |
| Decoding strings and names past their buffer | A literal string's output is at most its input, and a hex string's at most half of it plus one. A `#xx` escape is decoded only when two hex digits follow inside the token. A name of up to 128 bytes is decoded on the stack, a longer one on the heap | `LexerTests.Decodes_the_octal_and_control_escapes_of_a_literal_string`, `Pads_an_odd_hexadecimal_string_with_a_trailing_zero`, `Reads_a_name_with_its_escapes_intact` | Property |
| Malformed syntax that throws | A key that is not a name, a missing value, an array closed by `>>`, a stray token: each becomes a null or a skipped token with `syntax.unexpected-token` | `PropertyTests.The_parser_answers_for_any_bytes_without_leaving_the_buffer`; `ParserTests.Drops_entries_whose_value_is_null_as_the_specification_requires` | Property |
| Names chosen to collide in a hash table | `PdfName` hashes as an ordinal string, which the runtime randomizes per process | — | — |
| A parsed value pointing into a pooled window | Values are copied out of the window: strings into arrays, names into strings, stream data as an offset into the file | Structural | — |

Known gaps:

- [#163] Nesting past 128 levels, and loads nested past 64, are cut though a valid file can reach both. They become
  guards.
- [#172] Malformed strings, names and repeated keys are read in silence. A string that never closes swallows the
  objects after it, and a repeated key keeps its last value.
- [#119] An array or dictionary left open until the end of the data is read in silence.
- [#36] `PdfString.ToText` misreads PDFDocEncoding, PDF 2.0 UTF-8 strings and UTF-16 language escapes.
- [#37] Interned names grow for the life of the process.

### Indexing: the cross-reference chain, trailers and the rebuild

| Threat | Defense | Held by | Fuzzed |
|---|---|---|---|
| A `/Prev` chain that loops | The offsets read are kept, and a repeat ends the chain with `xref.chain-cycle`, placed at an offset in the file | `DocumentReaderTests.Stops_when_the_chain_of_sections_loops`; `CrossReferenceChainTests.Reports_a_loop_in_the_chain_at_the_section_it_loops_back_to_whatever_precedes_the_header`, `Places_a_loop_back_to_a_section_named_outside_the_file_at_what_named_it`; `CrossReferenceRuleTests.A_prev_that_names_a_section_already_read_is_a_loop_and_an_error` | Yes |
| A chain without end | `MaxXRefSectionCount` counts the `/Prev` links | `ReaderLimitsTests.Reaching_a_guard_keeps_what_fits_and_names_the_property_that_lifts_it`; `CrossReferenceChainTests.Looks_for_an_object_the_index_lacks_when_a_guard_stopped_the_chain` | No |
| A section offset outside the file; `/Prev` or `/XRefStm` given as a reference | Offsets are checked against the file. A `/Prev` or an `/XRefStm` written as a reference is read where a section already read places its object, without loading it, and a real with no fractional part as its integer; anything else names no section | `CrossReferenceChainTests.Reports_a_section_that_prev_names_past_the_end_of_the_file_once_at_the_section_naming_it`, `Reports_a_prev_that_is_not_an_offset_and_finds_what_it_named_when_asked`; `CrossReferenceRuleTests.A_prev_written_as_a_reference_or_a_real_is_followed_and_reported`, `A_hybrid_file_whose_xrefstm_is_a_reference_is_read_whole` | Yes |
| A missing section tried at many places | Candidates within 512 bytes either side, nearest first, and 32 attempts at most per document (`MaxRelocationCandidates`) | `CrossReferenceChainTests.Reads_a_section_that_prev_names_a_few_bytes_off`; `HostileInputTests.Opening_a_chain_of_hybrid_sections_whose_streams_never_close_reads_a_bounded_amount` | Yes |
| A classic table of any length | Its window grows ×4 from 64 KB up to `MaxXRefSectionLength`, and the rows read are kept | `ReaderLimitsTests.A_table_whose_keywords_the_bound_cuts_reports_the_bound`; `WindowEdgeTests.A_classic_table_reads_whole_wherever_its_window_edge_falls_near_its_trailer` | No: seeds stay under 120 KB |
| A subsection claiming a huge count, or numbering objects past `int.MaxValue` | The count is at most 50,000,000 (`MaxSubsectionEntries`, with its reason), and the first number and the last, first + count − 1, are at most `int.MaxValue`; past either, the table is malformed. The rows are bounded by the window as well. A row giving an object in use a generation outside 0..65,535 is refused alone, and the rows after it read; no older section's row then stands for its object, which is the rebuild's to find. A free row is read whatever its generation, as producers give the free list's head 65,536 | `CrossReferenceRuleTests.A_subsection_header_without_a_first_number_and_a_count_it_can_hold_makes_the_table_malformed`, `A_subsection_numbering_rows_past_the_largest_object_number_makes_the_table_malformed`, `A_row_giving_an_object_in_use_a_generation_no_object_can_have_is_refused_alone`, `A_free_row_whose_generation_is_past_65535_is_read_in_silence`; `DocumentReaderTests.Serves_no_older_revision_of_an_object_whose_newest_row_is_refused` | Yes |
| A trailer, or a cross-reference stream's dictionary, of any length | `MaxTrailerLength` | `ReaderLimitsTests.A_trailer_that_the_table_bound_cuts_is_read_through_a_window_of_its_own`; `HostileInputTests.Opening_a_chain_of_trailers_that_never_close_reads_a_bounded_amount` | Yes |
| What the chain refers to — a cross-reference stream's dictionary written with references, an indirect `/Length`, a relocation candidate's, a trailer followed by `stream` — loading objects while the index is still being read, and a correction, a rebuild or a null cached then served afterward as what the file wrote | Nothing loads while the chain is read (`PdfFileReader.ReadWithoutLoading`): an object is read where a section already read places it, at the offset its row gives, under the reference's number and generation, within `MaxObjectLength` and `MaxNestedLoads`, and is not a stream; nothing is corrected, rebuilt, cached, recorded or reported, and a guard that cuts it leaves it unread. A value the rows need that cannot be read so makes the section malformed; a `/Length` is read and checked once the chain is, behind a copy of the index as the file wrote it. An object read at a section's offset that is no section leaves nothing of its reading | `DocumentReaderTests.Changes_nothing_of_the_index_while_the_chain_is_read`, `A_value_the_chain_reads_that_a_guard_cuts_leaves_the_section_whole`, `Reads_a_cross_reference_stream_whose_length_only_it_indexes_as_its_direct_twin`; `CrossReferenceRuleTests.A_cross_reference_stream_value_the_chain_cannot_read_leaves_the_index_as_the_file_wrote_it` | No |
| `/W` widths that overflow a field or give empty rows; `/Size` or `/Index` claiming rows the data lacks, or numbering objects no object can be; row fields an entry cannot hold | At least three widths, each an integer that is not negative, not all zero, narrowed only once it is known to be within the decoded data: a row longer than the data is one the data does not hold, and the widths' sum is then within a `long`. A field wider than 8 bytes is read when its leading bytes are zero, its every byte still read. `/Index` is an array of pairs of integers, each numbering its rows within 0..`int.MaxValue` with a count of at most 50,000,000, each read once; without `/Index`, `/Size` is a non-negative integer. A fault in this numbering makes the section malformed before any row is read. Rows are read only while they fit in the decoded data, and the shortfall is computed by division, where a `/Size` as large as a `long` cannot overflow. Each field is read as an unsigned 64-bit value, one wider than 8 bytes whose leading bytes are not all zero as the largest; a row is refused alone when no entry can hold it — an offset of 2^63 or more, a generation past 65,535, an object stream numbered outside 1..`int.MaxValue`, an index past `int.MaxValue`, no older section's row then standing for its object — and a type past 2 is ignored, as ISO 32000-1 asks | `CrossReferenceRuleTests.A_cross_reference_stream_whose_widths_or_ranges_cannot_be_read_is_malformed`, `A_cross_reference_stream_whose_size_declares_more_rows_than_it_holds_is_malformed`, `A_cross_reference_stream_row_whose_fields_no_entry_can_hold_is_refused_alone`, `A_cross_reference_stream_row_of_a_type_past_2_is_ignored_however_wide_its_type_field`, `A_cross_reference_stream_whose_fields_are_wider_than_8_bytes_is_read_when_their_leading_bytes_are_zero`, `A_cross_reference_stream_row_whose_wide_field_holds_more_than_64_bits_is_refused_alone`, `A_cross_reference_stream_whose_rows_are_wider_than_its_data_holds_fewer_rows_than_it_declares`; `HostileInputTests.Survives_a_cross_reference_stream_with_impossible_field_widths`; `CrossReferenceChainTests.Looks_for_an_object_a_cross_reference_stream_holds_no_row_for` | Seldom: the rows are compressed |
| A rebuild run again and again | At most one per document. A missing object rebuilds only when the index is known to be incomplete | `DocumentReaderTests.A_reference_the_chain_cannot_read_rebuilds_nothing_while_the_chain_is_read`; `CrossReferenceChainTests.Reads_a_reference_to_an_object_the_file_does_not_define_as_null_without_rebuilding` | Yes |
| The rebuild's scan | 1 MB pieces overlapping by 64 bytes, each piece after the first searched from where the one before could no longer hold a whole `obj`, so that a header is read once. A header is read backwards, each run of digits judged by its value however many zeros lead it, as the parser judges it: a number in 1..`int.MaxValue` and a generation in 0..65,535; the probe of the validator asks the same of a header at an entry's offset. The definitions it finds again of a number are counted, with the first ten numbers, in a fixed-size record, and reported once as `object.redefined` | `DocumentReaderTests.Rebuilds_an_index_whose_objects_lie_past_the_first_megabyte`, `A_rebuild_ignores_object_headers_numbered_zero_or_beyond_what_an_object_number_holds`, `A_rebuild_finds_a_header_whose_number_and_generation_zeros_lead`, `Counts_two_definitions_the_scan_finds_in_the_overlap_of_two_windows_as_one_redefinition`, `Reads_a_header_that_straddles_the_start_of_a_scan_window_under_its_own_number`; `HostileInputTests.Survives_a_file_made_only_of_object_headers`; `CrossReferenceRuleTests.An_entry_at_a_header_the_parser_refuses_is_broken` | Partly |
| A guard reached in the middle of a rebuild | The guard's exception is held, and thrown once the index is whole | `ReaderLimitsTests.A_rebuild_that_reaches_a_guard_finishes_the_index_before_it_throws`, `A_rebuild_that_reaches_a_guard_looking_for_the_catalog_still_finds_it` | No |
| Searches for the next object turning quadratic | `SortedOffsets` keeps sorted runs, a logarithm of them, and searches each by halves | `SortedOffsetsTests.The_first_offset_after_a_position_is_found_whatever_order_offsets_and_lookups_come_in` | Fixed-seed property |
| Validation judging an index the reader repaired | The chain's index is copied before the first change the reader makes, none being made while the chain is read | `StreamLengthTests.The_next_object_a_search_stops_at_is_where_the_index_as_written_places_it_though_a_deferred_length_corrected_an_entry`; `CrossReferenceRuleTests.The_entry_a_deferred_length_found_off_its_object_is_judged_as_the_file_wrote_it` | — |

Known gaps:

- [#164] Nothing bounds how many entries the index holds. A cross-reference stream of one-byte rows indexes an
  object per decoded byte, 268 million under the default guards.
- [#155] Object numbers chosen to collide put the index, the object cache and the validator's sets in one hash
  bucket, and every lookup walks it.
- [#171] A rebuild takes headers cut at a chunk's edge, or lying in stream data or strings, and serves another object
  under their number.
- [#183] A rebuild stops after two million headers in silence, and a sound file whose table a guard cut can reach it.
- [#220] A cross-reference stream whose `/Length` only it indexes is found by an `endstream` search through a window
  `MaxTrailerLength` bounds, a guard named for a dictionary; rows that spell `endstream` end its data early.
- [#221] A cross-reference stream row of a reserved type is skipped, letting an older section's row stand where
  ISO 32000-1 reads the object as null.
- [#156] An `/XRefStm` naming a section already read, or a classic table, leaves no record and no finding.
- [#47] Each section is read through a window of up to 64 KB, whatever its size.
- [#49] A rebuild reads a 64 KB window at every `trailer` keyword.
- [#118] A rebuilt index records every object at generation 0.
- [#188] Relocating a section the chain cannot read can land on one it already read: a loop and a shift the file does
  not have are reported, or a hybrid table taken for its own `/XRefStm` loses what only the stream indexes.
- [#189] A rebuild serves a direct definition over a newer copy in a later object stream, and takes `10 0 objx` for a
  header.
- [#197] Near the start of the file, the nearby search for an object or a section reaches up to 1,024 bytes past the
  offset named, not the 512 either side the rows above and below state.

### Resolving objects and object streams

| Threat | Defense | Held by | Fuzzed |
|---|---|---|---|
| An object whose load needs itself, or loads chained without end | `_loading` turns a load of an object being loaded into null. `PdfFileReader.MaxNestedLoads` (64) and a stack probe bound the chain, reported once as `syntax.depth-exceeded` | `HostileInputTests.Survives_an_object_whose_length_refers_to_itself`, `Survives_a_chain_of_lengths_longer_than_the_stack_is_deep`, `Reports_chains_that_run_too_deep_once` | The load path, not the bound |
| References to references that loop | `PdfReference.Resolve` gives up after 32 fetches and reads null, with no report ([#173]) | `ObjectModelTests.A_chain_of_references_that_loops_resolves_to_null` | No |
| An object stream that needs an object it holds, or two that need each other | While an object stream is decoded, an object it holds reads as null and is reported once as `stream.self-reference`; the null is remembered, so a later decode reads the same | `HostileInputTests.An_object_stream_that_needs_an_object_it_holds_reads_the_same_after_the_budget_lets_it_go`, `Two_object_streams_that_need_each_other_read_the_same_after_the_budget_lets_them_go` | Object-stream seeds, not the cycle |
| An offset table that lies | `/N` and `/First` are checked against each other and against the decoded data, and the header is read up to its first pair that is not two integers, or that gives a number outside 1..`int.MaxValue` or an offset outside 0..`int.MaxValue` — the reader, the rebuild and the validator alike —, each fault reported once as `object-stream.unreadable`. A wrong index falls back on a lookup by number built once, reported once for each object as `object-stream.member-moved`. A member's start is checked against the data | `ReaderTimingTests.An_object_stream_whose_every_index_is_wrong_is_read_in_linear_time`; `DocumentReaderTests.Reads_an_object_where_its_stream_s_header_lists_it_rather_than_where_the_index_says`, `Reports_an_object_stream_whose_dictionary_or_header_cannot_be_believed_where_its_data_starts`, `Reads_no_member_an_object_stream_header_numbers_as_no_object_can_be`, `Reads_no_member_an_object_stream_header_places_where_no_member_can_start`, `Indexes_no_member_a_rebuild_finds_listed_under_a_number_no_object_can_have`; `CrossReferenceRuleTests.An_object_stream_whose_header_lists_something_other_than_objects_cannot_be_read`; `Reports_an_object_at_another_index_of_its_stream_once_however_often_it_is_parsed` | Yes |
| Many large object streams held at once | `ObjectStreamBudget` (32 MB, with its reason): the oldest are let go and decoded again on demand | `HostileInputTests.Object_streams_are_kept_decoded_within_a_budget_and_decoded_again_when_needed` | No |
| Parsed objects piling up | The object cache is FIFO, bounded by count (`ObjectCacheCapacity`, 8,192), not by weight ([#37], [#170]), and keyed by number, so an object named under many generations is parsed once | `HostileInputTests.An_object_referenced_under_many_generations_is_read_once`; `StreamLengthTests.A_stream_whose_length_is_wrong_is_searched_and_reported_once_however_often_the_cache_lets_it_go` | No |
| An object that is not where its entry says | The entry is checked against the file, a header is looked for within 512 bytes, and then the index is rebuilt once: three counted attempts. This answers a stack overflow the fuzzing found in M01 | `DocumentReaderTests.Finds_objects_whose_recorded_offsets_are_wrong`, `Finds_an_object_whose_entry_points_past_the_end_of_the_file`, `An_object_a_rebuild_does_not_find_either_is_null` | Yes |
| A reference to what the index lacks, forcing a rebuild each time | A rebuild runs only when the index is known to be incomplete, and once | `CrossReferenceChainTests.Looks_for_an_object_the_index_lacks_when_a_guard_stopped_the_chain` | Yes |

Known gaps:

- [#160] An object in an object stream is parsed with no `MaxObjectLength` bound, and `/N` sizes the header's tables
  before an entry is read.
- [#161] A stream written inside an object stream decodes under the default guards, and keeps its object stream's
  decoded data alive past `ObjectStreamBudget`.
- [#165] Decodes nested through filter parameters each hold a whole output: up to 64 of them at once.
- [#181] Nothing bounds the work one document asks. Two large object streams evict each other on every member, and
  objects are parsed again through windows grown to 16 MB.
- [#173] A chain or a loop of references reads as null with no report, one reference short of its bound.
- [#37] The cache is bounded by count, not weight, and a parsed object weighs about thirty times its syntax.
- [#121] Each object is read through its own 8 KB window, however small.
- [#144] A damaged object stream reports its filter fault each time it is decoded.
- [#185] A reference's generation is compared with nothing: `5 1 R` reads `5 0 obj`, and `7 0 R` reads `7 2 obj`,
  with no report.

### Stream data and its length

| Threat | Defense | Held by | Fuzzed |
|---|---|---|---|
| A `/Length` that is negative, past `int.MaxValue`, not an integer or unresolvable | Only 0..`int.MaxValue` is taken as a length. Anything else sends the reader to the first `endstream` in the window | `StreamLengthTests.A_length_that_cannot_be_taken_is_reported_as_the_file_wrote_it` | Partly |
| An indirect `/Length` leading back to its stream, or chained | The loading set and `MaxNestedLoads`, as above | `HostileInputTests.Survives_an_object_whose_length_refers_to_itself`, `Survives_a_chain_of_lengths_longer_than_the_stack_is_deep` | No |
| A declared length past the file | It is checked against the file before the file is asked, and the length a stream is given is clamped to what the file holds | `HostileInputTests.Ignores_a_stream_length_larger_than_the_file`; `WindowEdgeTests.A_stream_whose_length_the_file_cannot_hold_is_read_to_its_endstream` | Yes |
| Confirming a length by reading past the buffer or the file | The look-ahead after the data is a fixed stack buffer of 13 bytes, or 64 past the window, refused outside the file and clamped to it | `WindowEdgeTests.A_stream_the_file_cuts_short_is_reported_without_asking_the_source_past_its_end`; `StreamLengthTests.A_stream_past_the_window_whose_length_endstream_follows_is_read_as_declared_for_a_few_bytes_more` | Property |
| A search for a lost `endstream` running into other objects | It stops at the next object the index places, the first object header the bytes hold, or the end of the file. When none of these comes first, the declared length is kept and reported | `StreamLengthTests.A_stream_past_the_window_with_no_endstream_before_the_next_object_keeps_its_declared_length`, `The_search_stops_at_the_next_object_of_an_index_rebuilt_as_the_document_opened` | Plausibly |
| Many searches over one stretch of the file | The searches past the window read at most four times the file in a document (`EndStreamSearchPasses`, with its reason). Past that, the declared length is kept unsearched | `StreamLengthTests.Streams_whose_data_share_a_stretch_read_the_file_a_bounded_number_of_times_over` | No |
| One search reading without end | Reads of at most 64 KB, each strictly after the last, ending at the bound, at the end of the file, at a header, or on an empty read | `StreamLengthTests.A_file_of_data_with_no_endstream_is_searched_once_to_its_end`, `A_source_that_returns_less_than_it_holds_ends_the_search` | No |
| The same search made at every new parse | The result is kept by data start for the life of the document | `StreamLengthTests.A_stream_whose_length_is_wrong_is_searched_and_reported_once_however_often_the_cache_lets_it_go` | — |
| A stream decoded long after opening escaping its document's guards | The stream's data carries its document's guard and diagnostics | `ReaderLimitsTests.A_stream_decoded_without_diagnostics_reports_the_bound_to_its_document` | Yes |

Known gaps:

- [#170] Raw data is bounded only by the file. Every cached stream keeps its own copy, overlapping streams multiply
  it, and data passed through undecoded escapes `MaxDecodedStreamLength`.
- [#174] A length is misjudged when more than four white-space bytes precede `endstream`, when the file cannot hold
  the `/Length`, or when the search inside the window crosses into the next object.
- [#138] A stream searched for its `endstream` can keep a length that depends on the object read first.
- [#53] Encoded data past about 2 GB cannot be represented, and a length just under `int.MaxValue` throws
  `OutOfMemoryException`.
- [#48] Data that decodes past about 2 GB cannot be read whole.

### Decoding

Flate data is inflated by native code: the runtime's `System.IO.Compression.Native`. Microsoft's builds link
zlib-ng into it, and distribution builds of .NET link the system's zlib. So a host that keeps its .NET runtime and
its zlib patched is keeping part of the reader patched.

| Threat | Defense | Held by | Fuzzed |
|---|---|---|---|
| A decompression bomb | Every decoder writes through `PdfBoundedOutput`, or a buffer capped like it, at `MaxDecodedStreamLength`. Each step of a chain is capped on its own, and nothing bounds how many steps there are ([#166]) | `FilterTests.Keeps_exactly_what_the_bound_allows_and_reports_only_a_bound_it_met`, `Bounds_a_filter_in_the_middle_of_a_chain_as_well_as_the_last`; `HostileInputTests.Decodes_a_run_length_stream_no_further_than_the_bound` | Not at the bound |
| An output buffer sized from the file | The first buffer is an estimate from the input, capped at the bound; it doubles and never grows past it | `FilterTests.A_filter_s_first_buffer_is_sized_by_its_bound_not_by_its_input`, `A_bounded_output_never_grows_past_its_bound_and_hands_back_a_full_buffer_as_it_is` | Yes |
| Predictor parameters that overflow or size a row before a check | The row length is computed in 64 bits and compared with the data before anything is allocated | `FilterTests.Leaves_the_data_as_decoded_when_the_predictor_describes_rows_it_cannot_hold` | Seeds only |
| An LZW code past the table | A fixed table of 4,096 codes. A code not yet defined stops decoding, keeps what came before and reports `filter.failed` | `FilterDamageTests.Decodes_an_lzw_code_up_to_the_next_one_to_define_and_stops_at_any_past_it`; `FilterTests.Stops_an_lzw_stream_at_a_code_it_has_not_defined_and_says_so` | Two seeds |
| Damaged Flate data read again and again | A form is read again only after a fault, into the same output. The byte-at-a-time pass covers only the 8 KB piece the fault was met in (`FlateInput.ChunkLength`). A stream costs at most eight readings, three of them byte by byte. This is accepted: damaged streams read about 40 times slower, still linear in the file (#135, comment of 2026-09-30) | `FilterDamageTests.Reads_sound_data_once`, `Reads_again_a_byte_at_a_time_only_the_piece_of_input_the_fault_was_met_in`, `Reading_damaged_data_again_allocates_no_second_output_and_sound_data_nothing_to_read_it_again_with` | Yes |
| The inflater's exceptions escaping | Only its complaints about the data (`InvalidDataException`, `IOException`) are faults; a preset-dictionary header is refused | `FilterDamageTests.Takes_only_the_inflater_s_complaints_about_its_data_as_faults`, `Reports_a_zlib_header_that_asks_for_a_preset_dictionary_instead_of_throwing` | Yes |
| Image codecs | DCT, JPX, JBIG2 and CCITT data is never decoded: the chain stops and the data comes back as it is. An unknown filter is `filter.unsupported`, and the chain stops there too: no filter after it reads data it cannot read ([#159]) | Structural; `FilterTests.Stops_a_chain_at_the_first_filter_it_does_not_know_and_reports_it_once` | — |

Known gaps:

- [#166] A `/Filter` array of any length decodes every step up to the bound: a 513-byte file allocates 3.3 GB as it
  opens. Over damaged Flate data, every entry pays the replay again.
- [#162] Predictors and filter parameters the reader does not apply decode to wrong bytes with no report.
- [#141] ASCII85, ASCIIHex and RunLength data that cannot be decoded is skipped in silence.
- [#146] A Flate reading allocates its output before the zlib header says it can go on.
- [#142], [#143], [#145] Damaged Flate data is repaired and checked less than it could be.
- [#192] LZW decoding allocates an array for nearly every code: several times what it decodes to, bounded by the
  output's guard.

## The validator

`PdfValidator.Validate` reads a document the caller opened, under that document's `PdfReaderOptions`. Whatever
it resolves goes through the reader and its guards, and joins the reader's cache. It adds reads of its own: the
file's last 1,024 bytes (`EndOfFileMarkerRule`), 64 bytes at every entry's offset (`CrossReferenceProbe`), and
each object stream's header, decoded again outside the reader's budget (`PdfFileReader.ReadObjectStreamHeader`).
It decodes no content or image stream.

The rules share five analyses, each built once per validation (`ValidationContext`): the cross-reference
probe, the page tree walk, the object graph, the Arlington walk and the dependencies between object streams.
Guarding the validator means guarding them.

| Threat | Defense | Held by | Fuzzed |
|---|---|---|---|
| A report that grows with the file's faults | The report keeps `FindingCapacity` findings (1,000) and counts the rest by severity; a negative capacity is refused. One finding per object for its missing references and null characters. The four Arlington rules tally per rule and model row, so they give at most four findings per row of the model | `ValidatorTests.The_report_keeps_as_many_findings_as_its_capacity_and_counts_them_all`, `A_capacity_of_zero_keeps_no_finding_and_still_counts_them`; `ObjectRuleTests.Several_references_to_nothing_in_one_object_are_one_finding_naming_the_first`; `ArlingtonRuleTests.Several_objects_with_one_fault_are_one_finding_at_the_first_with_their_count` | Yes |
| Cycles and depth in the object graph: `/Parent`, `/P`, destinations | `ObjectGraph` walks with an explicit stack and resolves each number once. The trailer's `/Prev` and `/XRefStm` are not followed | `PageTreeRuleTests.Back_links_through_parents_and_destinations_are_no_loop`; `ArlingtonRuleTests.A_long_outline_chain_is_walked_without_recursion` (20,000 items) | Yes |
| A page tree deeper than the stack, with cycles, or with nodes listed many times | `PageTreeWalk` keeps one heap frame per node on the path. A kid on the path is a loop and counts nothing. A node already walked is counted, not walked again | `PageTreeRuleTests.A_tree_nested_deeper_than_any_stack_is_walked` (20,000 levels), `A_kid_naming_the_node_that_lists_it_is_a_loop_and_an_error`, `Nodes_listed_twice_at_every_level_are_counted_without_being_walked_again` (2^40 pages in under 5 s) | Yes |
| The orphaned-page sweep rebuilding the index | `PageTreePageOrphanedRule` runs only on a chain read whole, with no loop, no cut, no repair and no broken entry | `PageTreeRuleTests.A_kid_whose_entry_leads_nowhere_is_the_entry_s_finding_and_still_takes_a_page_s_place` | Yes |
| Re-checking in the Arlington walk: shared trees, ties, `/Parent` chains | Breadth-first over a queue, each indirect object typed once. Tree nodes and arrays are expanded once. Ties are weighed once per candidate set. Inherited keys are memoized per ancestor and key, and a `/Parent` cycle ends the search. Back-links are checked, never followed | `ArlingtonRuleTests.A_name_tree_whose_kids_loop_is_walked_once`, `Tree_nodes_that_share_one_kids_array_expand_it_once`, `An_object_many_contexts_tie_on_is_weighed_once`, `A_parent_cycle_among_fields_ends_the_search_for_an_inherited_key`, `A_parent_types_nothing_even_under_a_key_a_wildcard_allows` | Yes |
| Judging what a guard cut | The Arlington walk and the object stream probe skip an object the reader cut at a limit, while the diagnostics have room to say so ([#169]); the page tree rules do not ([#175]). A guard reached under `ThrowOnLimit` throws out of `Validate` | `ArlingtonRuleTests.An_object_a_limit_cut_is_not_judged`; `CrossReferenceRuleTests.An_object_stream_a_limit_cut_before_its_header_ended_is_said_to_be_unchecked`; `ValidatorTests.A_guard_reached_during_validation_throws_when_the_document_was_opened_to_throw` | Defaults only |
| Circular object streams | `ObjectStreamDependencies` walks the decoding keys (`/Length`, `/Filter`, `/DecodeParms`, `/N`, `/First`) iteratively with seen sets, and decodes nothing itself | `ObjectRuleTests.Two_object_streams_that_each_need_an_object_of_the_other_are_both_circular`, `A_chain_of_object_streams_that_leads_elsewhere_is_not_circular` | Yes |
| Reading the file's bytes | The tail read is at most 1,024 bytes. The probe reads 64 bytes into a stack buffer per entry and refuses an offset outside the file, though a read near the end asks the source past it ([#167]). The header version echoed is at most 8 digits and dots | `EndOfFileMarkerRuleTests.Checking_reads_the_last_1024_bytes_and_nothing_else`; `CrossReferenceRuleTests.An_entry_outside_the_file_is_broken`; `PropertyTests.The_end_of_file_rule_reports_exactly_when_the_last_1024_bytes_hold_no_marker` | Yes |
| Shared state between validations | `PdfValidator` holds only its immutable options, rules hold no fields, and the Arlington tables are read-only spans | — | — |

The validator has no guard of its own. `FindingCapacity` is an option on the report, visible through
`SuppressedCount`, and not a `limit.*`.

Known gaps:

- [#167] `Validate` throws untyped exceptions: a page count that wraps, a negative entry offset handed to a location,
  and a probe that reads past a source's end.
- [#168] The analyses cost more than the file: a quadratic probe and dependency walk, faults kept before
  `FindingCapacity` applies, and values yet to visit held past the reader's cache. The first row of the table above
  holds for the report, not for what builds it.
- [#169] Once the diagnostics are full, a guard reached is neither reported nor seen, and the validator blames the
  file for the reader's cut.
- [#175] The page tree rules judge objects a guard cut, and a valid object header longer than 64 bytes is
  `xref.entry-broken`.
- [#173] The Arlington walk takes a reference to a reference for a missing key.
- [#218] A chain read whole that gives no row is rebuilt as the document opens, and no rule reports it.
- [#128] The object stream dependency walk reads an index that a rebuild changes under it.
- [#132] A document with no catalog at all earns no finding when its index was rebuilt at opening.

## What holds the defenses

- **Hand-written hostile tests**, on every commit: `HostileInputTests`, `ReaderLimitsTests`, `ReaderTimingTests`
  (a ratio, not a wall-clock budget), `WindowEdgeTests`, `StreamLengthTests`, `FilterTests`, `FilterDamageTests`,
  `DiagnosticsTests`, and the validation rule tests. They hold the bounds a mutation of a small file cannot reach.
- **Budgets on real documents, and on a generated index.** `CorpusReadingTests` holds what indexing a 1,000-page document and walking its
  page tree allocate, and each operation on every corpus document — opening it, reading every object, walking its
  pages, validating a damaged one — to 20 s. `CorpusValidationTests.Validating_the_largest_document_stays_within_its_budget`
  holds validation to 6 MB. `DocumentReaderTests.Opening_an_index_of_three_hundred_thousand_objects_stays_within_its_memory_budget`
  holds opening a valid index of 300,000 objects to 5 % over its figure, through a classic table and a cross-reference
  stream ([#193]). Every job that runs the tests on a change, and the documentation's deployment, stops after fifteen
  minutes. Throughput is not held in CI yet ([#38]).
- **The mutation campaign** (`FuzzingTests`, `FuzzingSeeds`, `.github/workflows/fuzz.yml`). It has three
  targets:
  - open a mutated document, resolve every indexed object and decode every stream without an image filter;
  - validate every mutant the reader opens, with no exception allowed at all;
  - parse the mutated bytes as one object, which in most seeds is only the first object's number ([#158]).

  The mutations are bit flips, digit rewrites, truncation, a random overwrite of 1 to 64 bytes, and a broken
  `endstream` or `obj`. Each input must finish within 5 seconds, checked once it returns, and allocate under 64 MB.
  The seeds are corpus documents of 120 KB at most, not encrypted, read under the default limits. Every commit
  runs 104 seeds 60 times each. Each night runs the smallest seed of each of the 32 reader structures plus 16 in
  rotation, 20,000 times each, so every seed is reached within five runs.
- **Properties** (`PropertyTests`, FsCheck): the lexer, the parser, the number parsers, text strings, window edges,
  the limits' validation and the end-of-file rule. 200 cases per commit with a fixed seed; 50,000 a night with a
  seed from the run number.
- **Referees**: qpdf, in a container, judges the corpus as we do (`QpdfRefereeTests`, `FlateRefereeTests`,
  `StreamLengthRefereeTests`, `ValidationRefereeTests`).

What none of them reaches is the subject of the gaps below. A mutation never makes a file longer, so nothing
size-dependent is fuzzed: the guards, the budgets, the rebuild's pieces. FsCheck's default sizes keep generated
buffers near 100 bytes and integers within ±100. No coverage-guided fuzzer exists yet ([M23] slice 11).

Known gaps:

- [#158] The parser target, three properties and three hostile-input tests hold less than their names claim, and
  several defenses have no test.
- [#176] The campaign never reaches RunLength, ASCIIHex, most predictor rows, any guard, encrypted files or inputs
  past 120 KB, and cannot see a read past the end.
- [#38] Throughput is not held in CI.
- [#194] The nightly campaign starts its seeds at 0 every night, so it runs the same mutants of a document each time.

## The package and its supply chain

- **The core has no dependency** (invariant 1): its project references no package and no other project. Review
  alone keeps it so ([#179]). The AOT and trimming analyzers run, and warnings fail the build. Unsafe code is off.
- **Workflows** declare read-only permissions at their top — `contents: read`, with `pull-requests: read` added
  in `commits.yml`, and `read-all` in `scorecards.yml`, whose one job replaces it with the four permissions it
  needs — and raise a permission per job where one is needed, except `docs.yml`, which grants `pages: write` and
  `id-token: write` to the whole workflow ([#177]). Every action is pinned to a commit SHA. CI and the commit
  checks run on `pull_request`, so a fork's pull request gets no secret and no write token. The one
  `pull_request_target` workflow, Dependabot's auto-merge, checks out no code.
- **Publishing** uses trusted publishing ([ADR 25](adr/0025-trusted-publishing-rather-than-an-api-key.md)): a
  key valid one hour, exchanged for the job's OIDC token just before the push, and no NuGet secret stored. The
  `AdCodicem.*` prefix is reserved on nuget.org. Builds are deterministic and carry their sources' location and
  symbols.
- **Test data is pinned**: remote corpus documents by size and SHA-256, the Arlington model by a lock file that a
  test checks.
- **Measured, not asserted**: OpenSSF Scorecard runs weekly and on every push to `main`. CodeQL runs as GitHub's
  default setup, which the ruleset on `main` requires.

Known gaps:

- [#177] The release jobs restore, build and test while holding the right to publish, and the stable one runs an
  unlocked npm tree beside the NuGet key.
- [#178] A branch can publish a package without review: nothing ties the `nuget` environment to `main`.
- [#179] Nothing but review checks that the core has no dependency.
- [#43] The NuGet restore is not locked.
- [#41] The stable release cannot push through the ruleset, and the ruleset requires no CI check by name.
- [#45] The release packages carry no attestation.
- [#44] The project has no OpenSSF Best Practices badge.
- [#196] Dependabot's auto-merge refuses only what it recognizes as a major bump, and nothing requires CI before the
  merge it queues.
- [#195] The corpus build's Python producers are pinned by version, without hashes, and the packages they pull in are
  not pinned.

## Surfaces still to come

Each surface below is left for the milestone that opens it to write, in the form of the sections above, in the
same change. The table says which milestone opens each surface first and what the specifications decide today.
The last column also names the questions they leave open, which that milestone settles before it writes code.

| Surface | Opens in | Decided by | What the specifications decide, and what they leave open |
|---|---|---|---|
| XMP packets: the first XML the library parses | [M02] slice 5 ([#61]) | ADR 34 | [M03] and [M14] give the parser's settings: DTDs prohibited, no resolver, iterative, size under the stream's guard, a billion-laughs and an external-entity packet read as malformed. Slice 5 comes first and must adopt them. |
| The writer: values the reader recovered, the incremental copy, the files a save writes | [M03] | ADR 6, 13, 18 | Nesting written without recursion past the parser's depth. A lying `/Length` not propagated. A save writes a temporary file beside its target, and saving over the source is refused. `OpenAsync(Stream)` buffers a non-seekable stream whole, with no bound stated. |
| Revisions and signature coverage: `/ByteRange`, `/Contents` | [M04] | ADR 18 | Byte ranges checked against the file before use, none sizing an allocation. A revision opened on a slice that ends at its end. Iterative walks. |
| Repair | [M05] | ADR 22 | Hostile documents end in a report or a typed exception within time and allocation budgets. No bounds table yet. |
| Copying objects between documents, catalog structures, embedded files and portfolios | [M06], [M07] | ADR 17, 37 | Copies never follow `/Parent`, `/P` or outline links, with an explicit stack. Attachments are streamed under the source's guards. Open: how a file name from the PDF becomes an output path in the tool's `attachments … extract`, and the depth bound on nested portfolios, whose value is not given. |
| Image files as pages: JPEG, JPEG 2000, TIFF, PNG | [M07] | ADR 42 | Container parsers in the core, bounded and fuzzed. Open: `PdfImagePageOptions` carries no limits record for a file read outside any document. |
| Content streams: tokenizing, rewriting, interpreting | [M07], [M08], [M15] | ADR 15, 34 | Guards on operations, glyphs and graphics state depth arrive with the interpreter ([M15]). Open: received content rewritten through `PdfContentBuilder`, whose state machine throws `InvalidOperationException` on sequences damaged pages hold. |
| Font programs: sfnt, CFF charstrings, WOFF, WOFF2, Type 1, CMaps | [M08] | ADR 11, 34, 43 | `MaxFontLength` (64 MB) and `MaxCharstringOperations` (65,536) join `PdfReaderLimits` (ADR 34, amended). Composite glyphs are walked iteratively, and the WOFF2 sizes are checked before allocation. Fonts are fuzzed nightly. |
| Barcode payloads, and `barcode:` URLs from template data | [M10], [M12] | ADR 38 | Payloads end in the typed exception or a parse error within a time and allocation budget. |
| Annotations, rich text (`/RC`) and optional content | [M11] | ADR 34, 37 | `/RC` is parsed with `XmlReader`, DTDs prohibited. Walks are iterative with visited sets. |
| HTML and CSS | [M12] | ADR 2, 4, 37, 38 | Scripting disabled. Guards on box depth, pages, imports, custom property expansion and selector cost. Open: the length of a `@counter-style` representation, and the source's length when it is given as one string. |
| The resource loader and web fonts | [M12] | ADR 11, 38 | Deny by default. Addresses are checked after name resolution, redirects are kept within an allowed origin, and `file:` is confined to a root. Resource guards apply. Open: how `CachingResourceResolver`, keyed by URL, composes with each render's policy, and which reader options a PDF fetched as a page image opens under. |
| Native code in `AdCodicem.Pdf.Html`: HarfBuzz reading fonts, Skia decoding images | [M12] | ADR 5, 43 | Images are checked against `MaxImagePixels` by our own header parser before Skia sees them. Open: the tables HarfBuzz reads and M08's parser does not (GSUB, GPOS, morx) are neither validated nor bounded. [M25] keeps every font program away from native parsers. |
| SVG | [M12] | ADR 37, 38 | `MaxSvgElements`, DTDs prohibited in referenced files. Open: group nesting depth, and cycles among `clipPath`, `mask`, `pattern` and `marker`. |
| Factur-X, CII, UBL and XRechnung XML | [M14] | ADR 9, 37 | XMP's reader settings, schemas from embedded copies only. Open: the third-party model's own XML settings, and escaping in the HTML rendition. |
| Markup the library generates from file text: Markdown, JSON, HTML renditions; regular-expression search | [M15] | ADR 15, 38 | Text escaped so it cannot make structure; `NonBacktracking` by default. Open: [M31] does not yet state escaping for the values it places into CSS. |
| ICC profiles | [M15], [M29] | ADR 34, 42 | Bounded header and tag-table readers, CLUT sizes computed in 64 bits before allocation, and fuzzing over fourteen nights. |
| Encryption and passwords | [M16] | ADR 41 | `/O`, `/U`, `/OE`, `/UE`, `/Perms` and `/Length` are checked before use, and decryption runs block by block through pooled buffers. Open: whether passwords and file keys are cleared from memory and kept out of exceptions and diagnostics. The managed MD5, RC4 and AES are not constant-time. |
| Forms, scripts and XFA; FDF and XFDF | [M16] | ADR 34, 37 | Scripts are recognized by pattern and never run. XFA and XFDF are parsed with `XmlReader`, DTDs prohibited. Field trees are walked iteratively. |
| E-mail: EML and MSG | [M18] | ADR 34, 38 | `EmailLimits` guards. CFB chains cut at the sector count, LZFu sizes checked before allocation, every remote reference refused. |
| Redaction and sanitization | [M19] | ADR 22, 37 | Always a full rewrite. A stream cut at a guard fails verification rather than passing it. |
| Conformance profiles over hostile documents | [M20], [M28] | ADR 36, 45 | Bounded header readers; the engine proved total by FsCheck. |
| Received documents converted to PDF/A | [M21] | ADR 22, 41 | `MaxAttachmentDepth` (3), with the attached streams' hashes. |
| Image codecs: CCITT, JBIG2, JPEG, JPEG 2000; color spaces and functions | [M22] | ADR 34, 35, 42 | `MaxImageWorkingSet`, `MaxJpegScans`, JBIG2 counts in 64 bits, a bounded PostScript stack, every decoder fuzzed. Open: the cost of a type 4 function evaluated per sample, and the size of the lookup table for an n-input `DeviceN`. |
| OCR text layers: hOCR, ALTO, TSV | [M22] | ADR 42 | `XmlReader` with DTDs prohibited; `MaxInputLength` 64 MB, typed, since it is the caller's input. |
| Optimization, the piecewise decode, hardening | [M23] | ADR 34, 35 | Cancellation within 100 ms on hostile input, the cache weighed in bytes, coverage-guided fuzzing, the container profile. |
| Comparison and templates | [M24] | ADR 15 | Bounded edits per window, `NonBacktracking`. |
| Rasterization | [M25] | ADR 16, 42, 43 | `MaxRasterPixels`, band and layer bytes, bounded mesh subdivision. No font program or codec reaches Skia. |
| Signing: TSA and remote-signing responses, deferred requests | [M26] | ADR 18, 41 | Network off by default, responses read up to a bound, the prepared file checked again before the second step. |
| Signature validation: CMS, X.509, OCSP, CRL, trusted lists, and the URLs they name | [M27] | ADR 41 | OCSP and CRL parsed by our own `AsnReader` code under explicit bounds, fuzzed from the day they are written. Path building uses a visited set. Open: no address, scheme or redirect policy is stated for the URLs a signed file names (caIssuers, OCSP, CRL). ADR 38's rules cover only the HTML engine. |
| DOCX packages | [M31] | ADR 37, 38 | ZIP limits checked before reading, part names validated, DTDs refused in every part, external relationships refused by default. |
| JSON inputs: plans, models, policies, templates, requests | [M06] onward | ADR 8 | `Utf8JsonReader` and source generation, no reflection. Open: several specifications state no depth or size bound. |

## Out of scope

- **Active content** ([ADR 37](adr/0037-out-of-scope-active-content-and-pdf-to-office.md)). JavaScript is never
  executed, dynamic XFA never rendered, 3D and rich media never authored. A file that carries them is read,
  validated and manipulated like any other, and what cannot be kept is reported.
- **The caller's own code and choices.** A `PdfFileSource`, a resolver, a codec, an OCR engine, a key source or an
  `HttpClient` the caller supplies is trusted. A caller that raises a guard, or chooses `Unbounded`, lets a file
  use what those bounds allow. That is by design, not a vulnerability.
- **A file changed under an open document.** `Open(string)` shares the file for reading, which Unix treats as
  advisory, and `Open(ReadOnlyMemory<byte>)` reads the caller's memory in place. Bytes that change after opening
  are read as they are then. A caller who cannot keep them still copies them first.
- **Concurrent use of one `PdfDocument`.** It is not thread-safe, as its documentation says, and nothing detects
  two threads using it.
- **Hard limits on a process.** The library does not sandbox itself. Until [M23]'s container profile, a host
  that must bound time or memory absolutely runs the library in a process or container it can stop.
- **What a page shows as opposed to what the file says.** The reader returns what the file holds. Whether a
  page's appearance misleads (an overlay, a field over signed text, white text) is for the milestones that
  render, compare or judge signatures to report.
- **Timing side channels.** None exists today. [M16] records that its managed MD5, RC4 and AES are not
  constant-time.

## Keeping this document

- A change that opens or widens a surface completes its section in the same pull request (`CLAUDE.md`). A new
  bound is classified when it is added (ADR 34). A defense that is missing is filed as a `debt` issue and named
  under *Known gaps*, never written as if it held.
- A defense named here is one that was read in the code. A test named here is one that was read.
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
[#45]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/45
[#47]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/47
[#48]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/48
[#49]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/49
[#52]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/52
[#53]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/53
[#61]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/61
[#118]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/118
[#119]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/119
[#121]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/121
[#125]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/125
[#128]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/128
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
[#172]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/172
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
[#190]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/190
[#191]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/191
[#192]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/192
[#193]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/193
[#194]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/194
[#195]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/195
[#196]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/196
[#197]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/197
[#218]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/218
[#220]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/220
[#221]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/221
